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
}
