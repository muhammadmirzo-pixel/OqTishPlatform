using Npgsql;
using OqTish.Domain.Finance;
using OqTish.IntegrationTests.Booking;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Finance.FinanceCalls;

namespace OqTish.IntegrationTests.Finance;

/// <summary>
/// Database guarantees of migration <c>Finance</c>: L1 append-only ledger (even for the owner), L2 deferred balance
/// trigger, commission-rule exclusion constraint and "only end" trigger, the settlement unique indexes and the
/// composite tenant FKs.
/// </summary>
public class FinanceSchemaTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<NpgsqlConnection> OpenAsync(string cs)
    {
        var c = new NpgsqlConnection(cs);
        await c.OpenAsync(Ct);
        return c;
    }

    private static async Task ExecAsync(NpgsqlConnection c, string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        await cmd.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<string> SqlStateAsync(Func<Task> action) => (await Assert.ThrowsAsync<PostgresException>(action)).SqlState;

    private const string InsertEntry = """
        INSERT INTO finance.journal_entries (id, clinic_id, branch_id, entry_type, source_event_key, posting_date_local, occurred_at, created_at)
        VALUES (@e, @c, @b, 'PaymentRecorded', @k, '2026-03-01', now(), now())
        """;

    private const string InsertLine = """
        INSERT INTO finance.ledger_lines (id, journal_entry_id, clinic_id, account, party, amount, currency)
        VALUES (gen_random_uuid(), @e, @c, @a, 'Clinic', @amount, 'UZS')
        """;

    [Fact]
    public async Task L1_ledger_rows_cannot_be_updated_deleted_or_truncated_even_by_the_owner()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var f = await FinanceWorld.CreateAsync(db);
        Assert.True((await host.PayAsync(f, await host.ConfirmedAppointmentAsync(f), Uzs(1_000.00m))).IsSuccess);
        await using var c = await OpenAsync(host.ConnectionString);

        foreach (var sql in new[]
                 {
                     "UPDATE finance.ledger_lines SET amount = amount * 2", "DELETE FROM finance.ledger_lines", "TRUNCATE finance.ledger_lines CASCADE",
                     "UPDATE finance.journal_entries SET description = 'x'", "DELETE FROM finance.journal_entries", "TRUNCATE finance.journal_entries CASCADE",
                 })
        {
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await SqlStateAsync(() => ExecAsync(c, sql)));
        }
    }

    [Fact]
    public async Task L2_unbalanced_or_single_line_entries_are_rejected_at_commit()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var f = await FinanceWorld.CreateAsync(db);
        await using var c = await OpenAsync(host.ConnectionString);

        async Task PostAsync(params decimal[] amounts)
        {
            await using var tx = await c.BeginTransactionAsync(Ct);
            var e = Guid.NewGuid();
            await ExecAsync(c, InsertEntry, ("e", e), ("c", f.ClinicId), ("b", f.BranchId), ("k", "test:" + e));
            foreach (var amount in amounts)
            {
                await ExecAsync(c, InsertLine, ("e", e), ("c", f.ClinicId), ("a", nameof(LedgerAccount.ClinicCollectedCash)), ("amount", amount));
            }

            await tx.CommitAsync(Ct);
        }

        Assert.Equal(PostgresErrorCodes.CheckViolation, await SqlStateAsync(() => PostAsync(100m, -99.99m)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, await SqlStateAsync(() => PostAsync(100m)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, await SqlStateAsync(() => PostAsync()));
        Assert.Equal(PostgresErrorCodes.CheckViolation, await SqlStateAsync(() => PostAsync(100m, 0m, -100m)));
        await PostAsync(100m, -60m, -40m);
    }

    [Fact]
    public async Task Commission_rules_cannot_overlap_and_can_only_be_ended()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var f = await FinanceWorld.CreateAsync(db);
        var march = Local(2026, 3, 1, 0, 0);
        var april = Local(2026, 4, 1, 0, 0);
        var rule = await host.AddRuleAsync(f, CommissionBasis.CollectedPayment, rate: 0.1000m, from: march, to: april);
        await host.AddRuleAsync(f, CommissionBasis.CollectedPayment, rate: 0.0800m, from: april); // adjacent: allowed
        await host.AddRuleAsync(f, CommissionBasis.CompletedService, rate: 0.0500m, from: march); // other basis: allowed
        await using var c = await OpenAsync(host.ConnectionString);

        Assert.Equal(PostgresErrorCodes.ExclusionViolation, await SqlStateAsync(() => ExecAsync(c, """
            INSERT INTO finance.commission_rules (id, clinic_id, basis, rate, currency, rounding, effective_from, contract_reference, approved_by_user_id, created_at)
            VALUES (gen_random_uuid(), @c, 'CollectedPayment', 0.05, 'UZS', 'AwayFromZero', @from, 'X', @u, now())
            """, ("c", f.ClinicId), ("from", Local(2026, 3, 20, 0, 0).UtcDateTime), ("u", f.AdminUserId))));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await SqlStateAsync(() => ExecAsync(c, "UPDATE finance.commission_rules SET rate = 0.2 WHERE id = @id", ("id", rule))));
        Assert.Equal(PostgresErrorCodes.CheckViolation, await SqlStateAsync(() => ExecAsync(c,
            "UPDATE finance.commission_rules SET effective_to = effective_to + interval '1 day' WHERE id = @id", ("id", rule))));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await SqlStateAsync(() => ExecAsync(c, "DELETE FROM finance.commission_rules WHERE id = @id", ("id", rule))));
        await ExecAsync(c, "UPDATE finance.commission_rules SET effective_to = effective_to - interval '1 day', ended_at = now() WHERE id = @id", ("id", rule));
    }

    [Fact]
    public async Task One_active_settlement_per_clinic_month_and_a_ledger_line_is_settled_once()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var f = await FinanceWorld.CreateAsync(db);
        await using var c = await OpenAsync(host.ConnectionString);
        const string insert = """
            INSERT INTO finance.settlements (id, clinic_id, period_start, period_end, status, amount_due, currency, created_by_user_id, created_at, updated_at, voided_at)
            VALUES (@id, @c, '2026-03-01', '2026-04-01', @s, 0, 'UZS', @u, now(), now(), @v)
            """;
        var voided = Guid.NewGuid();
        await ExecAsync(c, insert, ("id", voided), ("c", f.ClinicId), ("s", "Voided"), ("u", f.AdminUserId), ("v", DateTime.UtcNow));
        await ExecAsync(c, insert, ("id", Guid.NewGuid()), ("c", f.ClinicId), ("s", "Draft"), ("u", f.AdminUserId), ("v", DBNull.Value));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, await SqlStateAsync(() =>
            ExecAsync(c, insert, ("id", Guid.NewGuid()), ("c", f.ClinicId), ("s", "Draft"), ("u", f.AdminUserId), ("v", DBNull.Value))));
        Assert.Equal(PostgresErrorCodes.CheckViolation, await SqlStateAsync(() => ExecAsync(c, """
            INSERT INTO finance.settlements (id, clinic_id, period_start, period_end, status, amount_due, currency, created_by_user_id, created_at, updated_at)
            VALUES (gen_random_uuid(), @c, '2026-05-02', '2026-06-02', 'Draft', 0, 'UZS', @u, now(), now())
            """, ("c", f.ClinicId), ("u", f.AdminUserId))));

        await host.AddRuleAsync(f, CommissionBasis.CollectedPayment, rate: 0.1000m);
        Assert.True((await host.PayAsync(f, await host.ConfirmedAppointmentAsync(f), Uzs(1_000.00m))).IsSuccess);
        await using var line = new NpgsqlCommand("SELECT id FROM finance.ledger_lines WHERE clinic_id = @c AND account = 'PlatformCommissionReceivable' LIMIT 1", c);
        line.Parameters.AddWithValue("c", f.ClinicId);
        var lineId = (Guid)(await line.ExecuteScalarAsync(Ct))!;
        const string item = "INSERT INTO finance.settlement_items (id, clinic_id, settlement_id, ledger_line_id, amount, currency) VALUES (gen_random_uuid(), @c, @s, @l, 100, 'UZS')";
        await ExecAsync(c, item, ("c", f.ClinicId), ("s", voided), ("l", lineId));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, await SqlStateAsync(() => ExecAsync(c, item, ("c", f.ClinicId), ("s", voided), ("l", lineId))));
    }

    [Fact]
    public async Task Composite_tenant_fks_reject_a_payment_for_another_clinics_appointment()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var a = await FinanceWorld.CreateAsync(db);
        var b = await FinanceWorld.CreateAsync(db);
        var appointmentOfB = await host.ConfirmedAppointmentAsync(b);
        await using var c = await OpenAsync(host.ConnectionString);

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, await SqlStateAsync(() => ExecAsync(c, """
            INSERT INTO finance.payments (id, clinic_id, branch_id, appointment_id, direction, method, amount, currency, status, collected_by, recorded_by_user_id, recorded_at)
            VALUES (gen_random_uuid(), @c, @b, @a, 'In', 'Cash', 10, 'UZS', 'Recorded', 'Clinic', @u, now())
            """, ("c", a.ClinicId), ("b", a.BranchId), ("a", appointmentOfB), ("u", a.ReceptionUserId))));
        Assert.Equal(PostgresErrorCodes.CheckViolation, await SqlStateAsync(() => ExecAsync(c, """
            INSERT INTO finance.payments (id, clinic_id, branch_id, appointment_id, direction, method, amount, currency, status, collected_by, recorded_by_user_id, recorded_at)
            VALUES (gen_random_uuid(), @c, @b, @a, 'In', 'Cash', 0, 'UZS', 'Recorded', 'Clinic', @u, now())
            """, ("c", b.ClinicId), ("b", b.BranchId), ("a", appointmentOfB), ("u", b.ReceptionUserId))));
    }
}
