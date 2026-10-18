using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using OqTish.IntegrationTests.Infrastructure;

namespace OqTish.IntegrationTests.Persistence;

public class MigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task All_migrations_apply_to_a_clean_database()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await postgres.CreateEmptyDatabaseAsync(ct);

        await using var db = PostgresFixture.CreateDbContext(connectionString);
        Assert.NotEmpty(db.Database.GetMigrations());

        await db.Database.MigrateAsync(ct);

        Assert.Empty(await db.Database.GetPendingMigrationsAsync(ct));
    }

    [Fact]
    public async Task Required_postgres_extensions_are_installed_by_migrations()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await postgres.CreateMigratedDatabaseAsync(ct);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT extname FROM pg_extension", connection);
        var installed = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                installed.Add(reader.GetString(0));
            }
        }

        Assert.Contains("btree_gist", installed);
        Assert.Contains("citext", installed);
    }

    [Fact]
    public void Model_has_no_changes_missing_from_migrations()
    {
        // Fails when an entity/configuration changed but nobody ran `dotnet ef migrations add`.
        using var db = PostgresFixture.CreateDbContext("Host=unused;Database=unused");
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Latest_migration_rolls_back_and_reapplies()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await postgres.CreateMigratedDatabaseAsync(ct);
        await using var db = PostgresFixture.CreateDbContext(connectionString);
        var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        var migrations = db.Database.GetMigrations().ToList();

        await migrator.MigrateAsync(migrations[^2], cancellationToken: ct);
        Assert.Single(await db.Database.GetPendingMigrationsAsync(ct));
        await migrator.MigrateAsync(cancellationToken: ct);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(ct));
    }

    [Fact]
    public async Task Audit_log_is_append_only_even_for_the_owner()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await postgres.CreateMigratedDatabaseAsync(ct);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using (var insert = new NpgsqlCommand("INSERT INTO audit.audit_log (id, occurred_at, actor_kind, action) VALUES (gen_random_uuid(), now(), 'System', 'test')", connection))
        {
            await insert.ExecuteNonQueryAsync(ct);
        }

        foreach (var sql in new[] { "UPDATE audit.audit_log SET action = 'x'", "DELETE FROM audit.audit_log", "TRUNCATE audit.audit_log" })
        {
            await using var cmd = new NpgsqlCommand(sql, connection);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync(ct));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
        }
    }

    [Fact]
    public async Task Tenant_composite_foreign_keys_reject_cross_clinic_rows()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await postgres.CreateMigratedDatabaseAsync(ct);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        const string setup = """
            INSERT INTO identity.users (id, status, security_stamp, created_at, updated_at) VALUES ('00000000-0000-0000-0000-0000000000a1', 'Active', 's', now(), now());
            INSERT INTO tenancy.clinics (id, name, verification_status, created_at, updated_at) VALUES
              ('00000000-0000-0000-0000-0000000000c1', 'A', 'Approved', now(), now()),
              ('00000000-0000-0000-0000-0000000000c2', 'B', 'Approved', now(), now());
            INSERT INTO tenancy.branches (id, clinic_id, name, district, address, is_active, created_at, updated_at) VALUES
              ('00000000-0000-0000-0000-0000000000b1', '00000000-0000-0000-0000-0000000000c1', 'A1', 'd', 'a', true, now(), now()),
              ('00000000-0000-0000-0000-0000000000b2', '00000000-0000-0000-0000-0000000000c2', 'B1', 'd', 'a', true, now(), now());
            INSERT INTO staff.clinic_memberships (id, clinic_id, user_id, role, scope, status, created_at, updated_at) VALUES
              ('00000000-0000-0000-0000-0000000000e1', '00000000-0000-0000-0000-0000000000c1', '00000000-0000-0000-0000-0000000000a1', 'Receptionist', 'Branches', 'Active', now(), now());
            """;
        await using (var cmd = new NpgsqlCommand(setup, connection))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await using (var locks = new NpgsqlCommand("SELECT count(*) FROM tenancy.branch_booking_locks", connection))
        {
            Assert.Equal(2L, await locks.ExecuteScalarAsync(ct));
        }

        await using var cross = new NpgsqlCommand("""
            INSERT INTO staff.branch_access (clinic_id, membership_id, branch_id)
            VALUES ('00000000-0000-0000-0000-0000000000c1', '00000000-0000-0000-0000-0000000000e1', '00000000-0000-0000-0000-0000000000b2')
            """, connection);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => cross.ExecuteNonQueryAsync(ct));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);
    }

    private static async Task ExecAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<string> FailsWithAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await ExecAsync(connection, "SAVEPOINT probe", ct);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(connection, sql, ct));
        await ExecAsync(connection, "ROLLBACK TO SAVEPOINT probe", ct);
        return ex.SqlState + ":" + ex.ConstraintName;
    }

    /// <summary>
    /// BE2 schema (migration Booking): S18 composite tenant FKs on appointments, CHECK constraints, the doctor
    /// time-block exclusion constraint (active only, half-open periods), and the append-only status history.
    /// </summary>
    [Fact]
    public async Task Booking_schema_enforces_tenant_fks_checks_no_overlap_and_append_only_history()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await postgres.CreateMigratedDatabaseAsync(ct);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await ExecAsync(connection, """
            INSERT INTO identity.users (id, status, security_stamp, created_at, updated_at) VALUES ('00000000-0000-0000-0000-0000000000a1', 'Active', 's', now(), now());
            INSERT INTO tenancy.clinics (id, name, verification_status, created_at, updated_at) VALUES
              ('00000000-0000-0000-0000-0000000000c1', 'A', 'Approved', now(), now()),
              ('00000000-0000-0000-0000-0000000000c2', 'B', 'Approved', now(), now());
            INSERT INTO tenancy.branches (id, clinic_id, name, district, address, is_active, created_at, updated_at) VALUES
              ('00000000-0000-0000-0000-0000000000b1', '00000000-0000-0000-0000-0000000000c1', 'A1', 'd', 'a', true, now(), now()),
              ('00000000-0000-0000-0000-0000000000b2', '00000000-0000-0000-0000-0000000000c2', 'B1', 'd', 'a', true, now(), now());
            INSERT INTO catalog.services (id, clinic_id, name, category, is_active, created_at, updated_at) VALUES
              ('00000000-0000-0000-0000-000000000051', '00000000-0000-0000-0000-0000000000c1', 'S', 'other', true, now(), now());
            """, ct);
        await ExecAsync(connection, """
            INSERT INTO catalog.branch_services (clinic_id, branch_id, service_id, price, currency, price_kind, duration_minutes, is_active, created_at, updated_at) VALUES
              ('00000000-0000-0000-0000-0000000000c1', '00000000-0000-0000-0000-0000000000b1', '00000000-0000-0000-0000-000000000051', 100, 'UZS', 'Fixed', 30, true, now(), now());
            INSERT INTO scheduling.doctors (id, clinic_id, display_name, is_active, created_at, updated_at) VALUES
              ('00000000-0000-0000-0000-0000000000d1', '00000000-0000-0000-0000-0000000000c1', 'D', true, now(), now());
            INSERT INTO scheduling.doctor_branches (clinic_id, doctor_id, branch_id) VALUES
              ('00000000-0000-0000-0000-0000000000c1', '00000000-0000-0000-0000-0000000000d1', '00000000-0000-0000-0000-0000000000b1');
            INSERT INTO scheduling.doctor_services (clinic_id, doctor_id, service_id) VALUES
              ('00000000-0000-0000-0000-0000000000c1', '00000000-0000-0000-0000-0000000000d1', '00000000-0000-0000-0000-000000000051');
            """, ct);
        const string columns = "INSERT INTO booking.appointments (id, clinic_id, branch_id, service_id, patient_user_id, doctor_id, starts_at, ends_at, status, expires_at, quoted_price, currency, price_kind, contact_name, source, created_at, updated_at, status_changed_at) VALUES ";
        string Appt(string id, string branch, string status, string doctor = "NULL", string expires = "now() + interval '30 minutes'", string patient = "'00000000-0000-0000-0000-0000000000a1'", string source = "Patient") =>
            columns + $"('{id}', '00000000-0000-0000-0000-0000000000c1', '{branch}', '00000000-0000-0000-0000-000000000051', {patient}, {doctor}, '2030-01-01T10:00Z', '2030-01-01T10:30Z', '{status}', {expires}, 100, 'UZS', 'Fixed', 'X', '{source}', now(), now(), now())";

        await ExecAsync(connection, "BEGIN", ct);
        // S18: an appointment of clinic A pointing at clinic B's branch violates the composite FK.
        Assert.StartsWith("23503:", await FailsWithAsync(connection, Appt("00000000-0000-0000-0000-0000000000e9", "00000000-0000-0000-0000-0000000000b2", "Pending"), ct), StringComparison.Ordinal);
        Assert.Equal("23514:ck_appointments_doctor_required", await FailsWithAsync(connection, Appt("00000000-0000-0000-0000-0000000000e8", "00000000-0000-0000-0000-0000000000b1", "Confirmed"), ct));
        Assert.Equal("23514:ck_appointments_pending_deadline", await FailsWithAsync(connection, Appt("00000000-0000-0000-0000-0000000000e7", "00000000-0000-0000-0000-0000000000b1", "Pending", expires: "NULL"), ct));
        Assert.Equal("23514:ck_appointments_source_patient", await FailsWithAsync(connection, Appt("00000000-0000-0000-0000-0000000000e6", "00000000-0000-0000-0000-0000000000b1", "Pending", patient: "NULL"), ct));
        await ExecAsync(connection, Appt("00000000-0000-0000-0000-0000000000e1", "00000000-0000-0000-0000-0000000000b1", "Pending"), ct);
        await ExecAsync(connection, Appt("00000000-0000-0000-0000-0000000000e2", "00000000-0000-0000-0000-0000000000b1", "Pending"), ct);

        string Block(string id, string appointment, string from, string to, bool active = true) =>
            "INSERT INTO scheduling.doctor_time_blocks (id, clinic_id, branch_id, doctor_id, starts_at, ends_at, kind, appointment_id, is_active, created_at, updated_at) VALUES " +
            $"('{id}', '00000000-0000-0000-0000-0000000000c1', '00000000-0000-0000-0000-0000000000b1', '00000000-0000-0000-0000-0000000000d1', '{from}', '{to}', 'Hold', '{appointment}', {active}, now(), now())";
        await ExecAsync(connection, Block("00000000-0000-0000-0000-0000000000f1", "00000000-0000-0000-0000-0000000000e1", "2030-01-01T10:00Z", "2030-01-01T10:30Z"), ct);
        Assert.Equal("23P01:ex_doctor_time_blocks_no_overlap", await FailsWithAsync(connection,
            Block("00000000-0000-0000-0000-0000000000f2", "00000000-0000-0000-0000-0000000000e2", "2030-01-01T10:15Z", "2030-01-01T10:45Z"), ct));
        await ExecAsync(connection, Block("00000000-0000-0000-0000-0000000000f3", "00000000-0000-0000-0000-0000000000e2", "2030-01-01T10:30Z", "2030-01-01T11:00Z"), ct); // adjacent: allowed
        Assert.StartsWith("23505:", await FailsWithAsync(connection,
            Block("00000000-0000-0000-0000-0000000000f4", "00000000-0000-0000-0000-0000000000e2", "2030-01-02T10:00Z", "2030-01-02T10:30Z"), ct), StringComparison.Ordinal); // one block per appointment
        await ExecAsync(connection, "UPDATE scheduling.doctor_time_blocks SET is_active = false WHERE id = '00000000-0000-0000-0000-0000000000f3'", ct);
        Assert.Equal("23514:ck_doctor_time_blocks_interval", await FailsWithAsync(connection,
            "UPDATE scheduling.doctor_time_blocks SET ends_at = starts_at WHERE id = '00000000-0000-0000-0000-0000000000f1'", ct));
        Assert.StartsWith("23514:", await FailsWithAsync(connection,
            "INSERT INTO scheduling.doctor_time_blocks (id, clinic_id, doctor_id, starts_at, ends_at, kind, is_active, created_at, updated_at) VALUES " +
            "(gen_random_uuid(), '00000000-0000-0000-0000-0000000000c1', '00000000-0000-0000-0000-0000000000d1', '2030-02-01T10:00Z', '2030-02-01T11:00Z', 'Hold', true, now(), now())", ct), StringComparison.Ordinal);

        await ExecAsync(connection, "INSERT INTO booking.appointment_status_history (id, clinic_id, appointment_id, to_status, actor_kind, occurred_at) VALUES " +
            "(gen_random_uuid(), '00000000-0000-0000-0000-0000000000c1', '00000000-0000-0000-0000-0000000000e1', 'Pending', 'Patient', now())", ct);
        foreach (var sql in new[] { "UPDATE booking.appointment_status_history SET reason_code = 'x'", "DELETE FROM booking.appointment_status_history", "TRUNCATE booking.appointment_status_history" })
        {
            Assert.StartsWith(PostgresErrorCodes.InsufficientPrivilege + ":", await FailsWithAsync(connection, sql, ct), StringComparison.Ordinal);
        }

        await ExecAsync(connection, "ROLLBACK", ct);
    }
}
