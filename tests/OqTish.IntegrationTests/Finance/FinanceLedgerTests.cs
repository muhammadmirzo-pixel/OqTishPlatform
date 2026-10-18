using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OqTish.Application.Booking;
using OqTish.Application.Common;
using OqTish.Application.Finance;
using OqTish.Domain.Booking;
using OqTish.Domain.Finance;
using OqTish.IntegrationTests.Booking;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Finance.FinanceCalls;

namespace OqTish.IntegrationTests.Finance;

/// <summary>
/// TESTING.md §4 / FINANCIAL_RULES.md §8 worked cases F1–F15 with the exact expected numbers (fixture parameters,
/// not the contract), plus the commission bases, period lock and L3. Real handlers, transactions and PostgreSQL
/// (<see cref="BookingHost"/>); where database time cannot be controlled (rule boundaries, posting dates, months)
/// the real <see cref="LedgerPoster"/> is called with explicit event times. Ledger invariants (L2) are asserted at
/// the end of every test.
/// </summary>
public class FinanceLedgerTests(PostgresFixture postgres)
{
    private static readonly string[] SettlementActions =
        ["finance.settlement.created", "finance.settlement.voided", "finance.settlement.finalized", "finance.settlement.paid"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (DateOnly, DateOnly) AroundToday() => (Today().AddDays(-1), Today().AddDays(1));

    [Fact]
    public async Task F1_F2_F3_two_payments_a_partial_refund_and_ten_percent_of_collected()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var f = await FinanceWorld.CreateAsync(db);
        var rule = await host.AddRuleAsync(f, CommissionBasis.CollectedPayment, rate: 0.1000m);
        var appointment = await host.CompletedAppointmentAsync(f, Uzs(500_000.00m));

        // F1
        Assert.Equal(500_000.00m, await host.AccountSumAsync(f.ClinicId, LedgerAccount.PatientReceivable, appointment));
        Assert.True((await host.PayAsync(f, appointment, Uzs(200_000.00m))).IsSuccess);
        Assert.Equal(300_000.00m, await host.AccountSumAsync(f.ClinicId, LedgerAccount.PatientReceivable, appointment));
        var second = (await host.PayAsync(f, appointment, Uzs(300_000.00m))).Value;
        Assert.Equal(0.00m, await host.AccountSumAsync(f.ClinicId, LedgerAccount.PatientReceivable, appointment));
        Assert.Equal(500_000.00m, await host.AccountSumAsync(f.ClinicId, LedgerAccount.ClinicCollectedCash));

        // F2
        var refund = await host.RefundAsync(f, second.Id, Uzs(100_000.00m));
        Assert.True(refund.IsSuccess, refund.Error?.Code);
        Assert.Equal((PaymentDirection.Refund, second.Id), (refund.Value.Direction, refund.Value.RefundOfPaymentId));
        var (from, to) = AroundToday();
        var totals = await host.TotalsAsync(f.ClinicId, from, to);
        Assert.Equal((500_000.00m, 100_000.00m, 400_000.00m), (totals.Collected, totals.Refunded, totals.Collected - totals.Refunded));
        var balance = (await host.BalanceAsync(f, appointment)).Value;
        Assert.Equal(("500000.00", "500000.00", "100000.00", "0.00"),
            (balance.Charged.ToAmountString(), balance.Paid.ToAmountString(), balance.Refunded.ToAmountString(), balance.Outstanding.ToAmountString()));

        // F3: +20 000.00, +30 000.00, −10 000.00; net 40 000.00; rate_snapshot 0.1000 on each
        var lines = await host.CommissionLinesAsync(f.ClinicId);
        Assert.Equal([20_000.00m, 30_000.00m, -10_000.00m], lines.Select(l => l.Amount).ToArray());
        Assert.All(lines, l => Assert.Equal((0.1000m, rule), (l.Rate!.Value, l.RuleId!.Value)));
        Assert.Equal(40_000.00m, lines.Sum(l => l.Amount));
        Assert.Equal(40_000.00m, totals.Commission);
        await FinanceInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task F4_same_idempotency_key_sequentially_returns_the_original_and_a_different_body_is_422()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var f = await FinanceWorld.CreateAsync(db);
        var appointment = await host.ConfirmedAppointmentAsync(f);

        var first = await host.PayAsync(f, appointment, Uzs(150_000.00m), key: "pay-1");
        var replay = await host.PayAsync(f, appointment, Uzs(150_000.00m), key: "pay-1");
        Assert.Equal(first.Value.Id, replay.Value.Id);
        Assert.Equal(1, await db.Payments.CountAsync(p => p.AppointmentId == appointment, Ct));
        Assert.Equal(1, await db.JournalEntries.CountAsync(e => e.PaymentId == first.Value.Id && e.EntryType == JournalEntryType.PaymentRecorded, Ct));

        var mismatch = await host.PayAsync(f, appointment, Uzs(1.00m), key: "pay-1");
        Assert.Equal("common.idempotency_mismatch", mismatch.Error!.Code);

        var noKey = await host.RunAsync(f.ReceptionUserId, sp => sp.GetRequiredService<PaymentHandlers>()
            .RecordAsync(f.ClinicId, appointment, new RecordPaymentCommand(Uzs(1m), PaymentMethod.Cash, null, null), null, Ct));
        Assert.Equal(CommonErrors.ValidationCode, noKey.Error!.Code);
        await FinanceInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task F5_F6_rounding_happens_once_per_line_and_totals_are_sums_of_rounded_lines()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();

        // F5: 7.5 % of 333 333.33 = 24 999.99975 → 25 000.00
        var a = await FinanceWorld.CreateAsync(db);
        await host.AddRuleAsync(a, CommissionBasis.CollectedPayment, rate: 0.0750m);
        Assert.True((await host.PayAsync(a, await host.ConfirmedAppointmentAsync(a), Uzs(333_333.33m))).IsSuccess);
        Assert.Equal([25_000.00m], (await host.CommissionLinesAsync(a.ClinicId)).Select(l => l.Amount).ToArray());

        // F6: 3 × 0.10 at 15 % → 0.015 → 0.02 each, total 0.06 (not round(0.30 × 0.15) = 0.05)
        var b = await FinanceWorld.CreateAsync(db);
        await host.AddRuleAsync(b, CommissionBasis.CollectedPayment, rate: 0.1500m);
        var appointment = await host.ConfirmedAppointmentAsync(b);
        for (var i = 0; i < 3; i++)
        {
            Assert.True((await host.PayAsync(b, appointment, Uzs(0.10m))).IsSuccess);
        }

        var lines = await host.CommissionLinesAsync(b.ClinicId);
        Assert.Equal([0.02m, 0.02m, 0.02m], lines.Select(l => l.Amount).ToArray());
        Assert.Equal(0.06m, lines.Sum(l => l.Amount));
        await FinanceInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task F7_the_rule_is_selected_by_the_payment_time_not_the_completion_time()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var f = await FinanceWorld.CreateAsync(db);
        var boundary = Local(2026, 3, 15, 0, 0);
        await host.AddRuleAsync(f, CommissionBasis.CollectedPayment, rate: 0.1000m, from: Local(2026, 1, 1, 0, 0), to: boundary);
        var rule8 = await host.AddRuleAsync(f, CommissionBasis.CollectedPayment, rate: 0.0800m, from: boundary);
        var appointmentId = await host.ConfirmedAppointmentAsync(f);
        var a = await db.Appointments.AsNoTracking().SingleAsync(x => x.Id == appointmentId, Ct);

        await host.InTransactionAsync(f.ReceptionUserId, (sp, t) => sp.GetRequiredService<LedgerPoster>().PostCompletionAsync(
            new AppointmentCompletedContext(a.Id, a.ClinicId, a.BranchId, a.ServiceId, a.DoctorId!.Value, a.PatientUserId, Uzs(100_000.00m), a.QuotedPriceMoney, a.StartsAt,
                Local(2026, 3, 14, 23, 50), f.ReceptionUserId), t));
        await host.BackdatedPaymentAsync(f, a.Id, Uzs(100_000.00m), Local(2026, 3, 15, 0, 10));

        var line = Assert.Single(await host.CommissionLinesAsync(f.ClinicId));
        Assert.Equal((8_000.00m, 0.0800m, rule8, new DateOnly(2026, 3, 15)), (line.Amount, line.Rate!.Value, line.RuleId!.Value, line.Date));
        Assert.Equal(new DateOnly(2026, 3, 14), (await db.ServiceCompletions.SingleAsync(c => c.AppointmentId == a.Id, Ct)).PostingDateLocal);
        await FinanceInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task F8_the_reporting_day_is_the_branch_local_date()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var f = await FinanceWorld.CreateAsync(db);
        var appointment = await host.ConfirmedAppointmentAsync(f);
        var late = await host.BackdatedPaymentAsync(f, appointment, Uzs(1_000.00m), new DateTimeOffset(2026, 3, 31, 18, 30, 0, TimeSpan.Zero)); // 23:30 local
        var afterMidnight = await host.BackdatedPaymentAsync(f, appointment, Uzs(1_000.00m), new DateTimeOffset(2026, 3, 31, 19, 30, 0, TimeSpan.Zero)); // 00:30 local

        Assert.Equal(new DateOnly(2026, 3, 31), (await db.JournalEntries.SingleAsync(e => e.SourceEventKey == LedgerPoster.PaymentKey(late), Ct)).PostingDateLocal);
        Assert.Equal(new DateOnly(2026, 4, 1), (await db.JournalEntries.SingleAsync(e => e.SourceEventKey == LedgerPoster.PaymentKey(afterMidnight), Ct)).PostingDateLocal);
        Assert.Equal(new DateOnly(2026, 3, 31), FinanceTime.LocalDate(new DateTimeOffset(2026, 3, 31, 18, 30, 0, TimeSpan.Zero), "Asia/Tashkent"));
        await FinanceInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task F9_an_april_refund_of_a_march_payment_leaves_march_unchanged_and_is_a_prior_period_refund_in_april()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var f = await FinanceWorld.CreateAsync(db);
        var appointment = await host.ConfirmedAppointmentAsync(f);
        var payment = await host.BackdatedPaymentAsync(f, appointment, Uzs(100_000.00m), Local(2026, 3, 10, 12, 0));
        var (marchFrom, marchTo) = (new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
        var marchBefore = await host.TotalsAsync(f.ClinicId, marchFrom, marchTo);

        await host.BackdatedRefundAsync(f, payment, Uzs(30_000.00m), Local(2026, 4, 5, 12, 0));

        Assert.Equal(marchBefore, await host.TotalsAsync(f.ClinicId, marchFrom, marchTo));
        Assert.Equal((100_000.00m, 0m), (marchBefore.Collected, marchBefore.Refunded));
        var april = await host.TotalsAsync(f.ClinicId, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));
        Assert.Equal((0m, 30_000.00m, 30_000.00m), (april.Collected, april.Refunded, april.PriorPeriodRefunded));
        await FinanceInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task F10_a_failed_payment_is_stored_as_failed_with_no_ledger_lines()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var f = await FinanceWorld.CreateAsync(db);
        await host.AddRuleAsync(f, CommissionBasis.CollectedPayment, rate: 0.1000m);
        var appointment = await host.ConfirmedAppointmentAsync(f);

        var failed = await host.PayAsync(f, appointment, Uzs(50_000.00m), PaymentMethod.Card, status: PaymentStatus.Failed);

        Assert.Equal(PaymentStatus.Failed, failed.Value.Status);
        Assert.Equal(0, await db.JournalEntries.CountAsync(e => e.ClinicId == f.ClinicId, Ct));
        Assert.Equal(0, await db.PaymentAllocations.CountAsync(x => x.PaymentId == failed.Value.Id, Ct));
        Assert.Equal("0.00", (await host.BalanceAsync(f, appointment)).Value.Paid.ToAmountString());
        await FinanceInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task F11_a_mistyped_cash_payment_is_corrected_by_a_reversal_and_a_new_payment()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var f = await FinanceWorld.CreateAsync(db);
        await host.AddRuleAsync(f, CommissionBasis.CollectedPayment, rate: 0.1000m);
        var appointment = await host.CompletedAppointmentAsync(f, Uzs(300_000.00m));
        var wrong = (await host.PayAsync(f, appointment, Uzs(30_000.00m))).Value;
        var originalKey = LedgerPoster.PaymentKey(wrong.Id);
        var originalLines = await db.LedgerLines.AsNoTracking()
            .Where(l => db.JournalEntries.Any(e => e.Id == l.JournalEntryId && e.SourceEventKey == originalKey))
            .OrderBy(l => l.Amount).Select(l => new { l.Id, l.Amount }).ToListAsync(Ct);

        var failed = await host.FailAsync(f, wrong.Id, "Noto'g'ri summa kiritildi");
        Assert.Equal(PaymentStatus.Failed, failed.Value.Status);
        Assert.True((await host.PayAsync(f, appointment, Uzs(300_000.00m))).IsSuccess);

        var original = await db.JournalEntries.AsNoTracking().Include(e => e.Lines).SingleAsync(e => e.SourceEventKey == originalKey, Ct);
        Assert.Equal(originalLines.Select(l => (l.Id, l.Amount)), original.Lines.OrderBy(l => l.Amount).Select(l => (l.Id, l.Amount))); // untouched
        var reversal = await db.JournalEntries.AsNoTracking().Include(e => e.Lines).SingleAsync(e => e.ReversesEntryId == original.Id, Ct);
        Assert.Equal(JournalEntryType.PaymentReversed, reversal.EntryType);
        Assert.Equal(-30_000.00m, reversal.Lines.Single(l => l.Account == LedgerAccount.ClinicCollectedCash).Amount);
        Assert.Equal(300_000.00m, await host.AccountSumAsync(f.ClinicId, LedgerAccount.ClinicCollectedCash));
        Assert.Equal(0.00m, await host.AccountSumAsync(f.ClinicId, LedgerAccount.PatientReceivable, appointment));
        Assert.Equal([3_000.00m, -3_000.00m, 30_000.00m], (await host.CommissionLinesAsync(f.ClinicId)).Select(l => l.Amount).ToArray());
        Assert.Equal(1, await db.AuditLog.CountAsync(x => x.Action == "finance.payment.failed" && x.TargetId == wrong.Id.ToString(), Ct));
        Assert.Equal("finance.invalid_payment_state", (await host.FailAsync(f, wrong.Id)).Error!.Code); // once only
        await FinanceInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task F13_daily_summaries_of_march_sum_exactly_to_the_ledger_of_march()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var f = await FinanceWorld.CreateAsync(db);
        await host.AddRuleAsync(f, CommissionBasis.CollectedPayment, rate: 0.0750m, from: Local(2026, 1, 1, 0, 0));
        var appointment = await host.ConfirmedAppointmentAsync(f);
        var amounts = new[] { 333_333.33m, 0.10m, 125_000.55m, 99_999.99m, 0.01m };
        var payments = new List<Guid>();
        for (var i = 0; i < amounts.Length; i++)
        {
            payments.Add(await host.BackdatedPaymentAsync(f, appointment, Uzs(amounts[i]), Local(2026, 3, 1 + (i * 7), 9 + i, 15), i % 2 == 0 ? PaymentMethod.Cash : PaymentMethod.Card));
        }

        await host.BackdatedRefundAsync(f, payments[0], Uzs(33_333.33m), Local(2026, 3, 30, 23, 59));
        await host.BackdatedPaymentAsync(f, appointment, Uzs(7_777.77m), new DateTimeOffset(2026, 3, 31, 19, 1, 0, TimeSpan.Zero)); // April 1 local: outside March

        var (from, to) = (new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
        var rebuild = await host.RunAsync(null, sp => sp.GetRequiredService<RebuildDailySummariesHandler>().RunAsync(Ct, from, to));
        Assert.Empty(rebuild.Mismatches);

        var summaries = await db.DailyFinancialSummaries.AsNoTracking().Where(s => s.ClinicId == f.ClinicId && s.LocalDate >= from && s.LocalDate <= to).ToListAsync(Ct);
        var cache = summaries.Aggregate(DailyTotals.Zero, (acc, s) => acc.Plus(s.Totals));
        Assert.Equal(await host.TotalsAsync(f.ClinicId, from, to), cache);

        await using var c = new NpgsqlConnection(host.ConnectionString);
        await c.OpenAsync(Ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT coalesce(sum(l.amount) FILTER (WHERE l.account LIKE 'ClinicCollected%'), 0),
                   coalesce(-sum(l.amount) FILTER (WHERE l.account = 'ClinicRefundsPaid'), 0),
                   coalesce(-sum(l.amount) FILTER (WHERE l.account = 'PlatformCommissionRevenue'), 0)
            FROM finance.ledger_lines l JOIN finance.journal_entries e ON e.id = l.journal_entry_id
            WHERE e.clinic_id = @c AND e.posting_date_local BETWEEN '2026-03-01' AND '2026-03-31'
            """, c);
        cmd.Parameters.AddWithValue("c", f.ClinicId);
        await using var r = await cmd.ExecuteReaderAsync(Ct);
        await r.ReadAsync(Ct);
        Assert.Equal((r.GetDecimal(0), r.GetDecimal(1), r.GetDecimal(2)), (cache.Collected, cache.Refunded, cache.Commission));
        Assert.Equal(amounts.Sum(), cache.Collected);
        await FinanceInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task F14_a_clinic_without_a_rule_accrues_zero_commission_and_is_flagged()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var f = await FinanceWorld.CreateAsync(db);
        for (var i = 0; i < 10; i++)
        {
            var appointment = await host.CompletedAppointmentAsync(f, Uzs(100_000.00m));
            Assert.True((await host.PayAsync(f, appointment, Uzs(100_000.00m))).IsSuccess);
        }

        Assert.Empty(await host.CommissionLinesAsync(f.ClinicId));
        var today = Today();
        var monthly = (await host.RunAsync(f.FinanceUserId, sp => sp.GetRequiredService<FinanceReportQueries>().MonthlyAsync(f.ClinicId, today.Year, today.Month, null, Ct))).Value;
        Assert.Equal(("0.00", false, 10, "1000000.00"), (monthly.Commission.ToAmountString(), monthly.CommissionRuleConfigured, monthly.Visits, monthly.Collected.ToAmountString()));

        var lastMonth = today.AddMonths(-1);
        var settlement = await host.RunAsync(f.AdminUserId, sp => sp.GetRequiredService<SettlementHandlers>()
            .CreateAsync(new CreateSettlementCommand(f.ClinicId, lastMonth.Year, lastMonth.Month, null), Ct));
        Assert.Equal(("0.00", false, 0), (settlement.Value.AmountDue.ToAmountString(), settlement.Value.CommissionRuleConfigured, settlement.Value.ItemCount));
        await FinanceInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task F15_a_refund_larger_than_the_net_paid_amount_is_422_and_writes_nothing()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var f = await FinanceWorld.CreateAsync(db);
        var appointment = await host.ConfirmedAppointmentAsync(f);
        var payment = (await host.PayAsync(f, appointment, Uzs(100_000.00m))).Value;
        Assert.True((await host.RefundAsync(f, payment.Id, Uzs(60_000.00m))).IsSuccess);
        var counts = (await db.Payments.CountAsync(Ct), await db.JournalEntries.CountAsync(Ct), await db.LedgerLines.CountAsync(Ct), await db.AuditLog.CountAsync(Ct));

        var tooMuch = await host.RefundAsync(f, payment.Id, Uzs(40_000.01m));

        Assert.Equal("finance.refund_exceeds_paid", tooMuch.Error!.Code);
        Assert.Equal(counts, (await db.Payments.CountAsync(Ct), await db.JournalEntries.CountAsync(Ct), await db.LedgerLines.CountAsync(Ct), await db.AuditLog.CountAsync(Ct)));
        Assert.True((await host.RefundAsync(f, payment.Id, Uzs(40_000.00m))).IsSuccess); // exactly the rest is fine
        Assert.Equal("finance.refund_exceeds_paid", (await host.RefundAsync(f, payment.Id, Uzs(0.01m))).Error!.Code);
        await FinanceInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task Completed_service_new_patient_and_confirmed_appointment_bases_and_proportional_reversal_of_a_price_concession()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var f = await FinanceWorld.CreateAsync(db);
        await host.AddRuleAsync(f, CommissionBasis.CompletedService, rate: 0.0500m);
        await host.AddRuleAsync(f, CommissionBasis.NewPatient, fee: Uzs(20_000.00m));
        await host.AddRuleAsync(f, CommissionBasis.ConfirmedAppointment, fee: Uzs(5_000.00m));

        await host.CompletedAppointmentAsync(f, Uzs(200_000.00m), patient: 0);   // confirm 5 000, completed 10 000, new patient 20 000
        var second = await host.CompletedAppointmentAsync(f, Uzs(100_000.00m), patient: 0); // confirm 5 000, completed 5 000 (not new)
        await host.CompletedAppointmentAsync(f, Uzs(0.00m), patient: 1);       // confirm 5 000, nothing for 0, new patient 20 000
        var payment = (await host.PayAsync(f, second, Uzs(100_000.00m))).Value;
        Assert.True((await host.RefundAsync(f, payment.Id, Uzs(40_000.00m))).IsSuccess); // concession 40 % → −2 000

        // Entries saved by one SaveChanges may get identities in any order: compare as a multiset.
        var lines = (await host.CommissionLinesAsync(f.ClinicId)).Select(l => l.Amount).Order().ToArray();
        decimal[] expected = [5_000.00m, 10_000.00m, 20_000.00m, 5_000.00m, 5_000.00m, 5_000.00m, 20_000.00m, -2_000.00m];
        Assert.Equal(expected.Order().ToArray(), lines);
        Assert.Equal(-60_000.00m, await host.AccountSumAsync(f.ClinicId, LedgerAccount.ClinicServiceRevenue, second)); // 100 000 charged − 40 000 concession
        Assert.Equal(0.00m, await host.AccountSumAsync(f.ClinicId, LedgerAccount.PatientReceivable, second));

        // A walk-in is created Confirmed without the confirm hook: no ConfirmedAppointment fee.
        var walkIn = await host.RunAsync(f.ReceptionUserId, sp => sp.GetRequiredService<WalkInAppointmentHandler>().HandleAsync(f.ClinicId,
            new WalkInCommand(f.BranchId, f.W.Service30, f.NextSlot(), f.W.Doctors[0], "Mijoz", "+998901112233"), null, Ct));
        Assert.True(walkIn.IsSuccess, walkIn.Error?.Code);
        Assert.False(await db.JournalEntries.AnyAsync(e => e.SourceEventKey == LedgerPoster.ConfirmedCommissionKey(walkIn.Value.Id), Ct));
        await FinanceInvariants.AssertAsync(host.ConnectionString);
        await BookingInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task Prepayment_before_completion_is_patient_credit_and_a_refund_returns_the_credit_first()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var f = await FinanceWorld.CreateAsync(db);
        var appointment = await host.ConfirmedAppointmentAsync(f);
        var payment = (await host.PayAsync(f, appointment, Uzs(200_000.00m), PaymentMethod.Transfer)).Value;
        Assert.Equal("-200000.00", (await host.BalanceAsync(f, appointment)).Value.Outstanding.ToAmountString());

        await BookingCalls.ShiftAsync(host.ConnectionString, appointment, f.NextPastStart());
        Assert.True((await host.RunAsync(f.ReceptionUserId, sp => sp.GetRequiredService<StaffAppointmentHandlers>()
            .CompleteAsync(f.ClinicId, appointment, new CompleteCommand(Uzs(150_000.00m)), Ct))).IsSuccess);
        Assert.Equal("-50000.00", (await host.BalanceAsync(f, appointment)).Value.Outstanding.ToAmountString());

        Assert.True((await host.RefundAsync(f, payment.Id, Uzs(80_000.00m))).IsSuccess); // 50 000 credit returned + 30 000 price reduction
        Assert.Equal(0.00m, await host.AccountSumAsync(f.ClinicId, LedgerAccount.PatientReceivable, appointment));
        Assert.Equal(-120_000.00m, await host.AccountSumAsync(f.ClinicId, LedgerAccount.ClinicServiceRevenue, appointment));
        Assert.Equal(200_000.00m, await host.AccountSumAsync(f.ClinicId, LedgerAccount.ClinicCollectedTransfer, appointment));
        Assert.Equal(-80_000.00m, await host.AccountSumAsync(f.ClinicId, LedgerAccount.ClinicRefundsPaid, appointment));
        await FinanceInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task Period_lock_redirects_postings_dated_into_a_finalized_month_to_the_next_open_month()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var f = await FinanceWorld.CreateAsync(db);
        await host.AddRuleAsync(f, CommissionBasis.CollectedPayment, rate: 0.1000m, from: Local(2025, 1, 1, 0, 0));
        var month = Today().AddMonths(-2);
        var (y, m) = (month.Year, month.Month);
        var appointment = await host.ConfirmedAppointmentAsync(f);
        await host.BackdatedPaymentAsync(f, appointment, Uzs(100_000.00m), Local(y, m, 10, 12, 0));

        var created = await host.RunAsync(f.AdminUserId, sp => sp.GetRequiredService<SettlementHandlers>().CreateAsync(new CreateSettlementCommand(f.ClinicId, y, m, null), Ct));
        Assert.Equal(("10000.00", 1), (created.Value.AmountDue.ToAmountString(), created.Value.ItemCount));
        var finalized = await host.RunAsync(f.AdminUserId, sp => sp.GetRequiredService<SettlementHandlers>().FinalizeAsync(created.Value.Id, Ct));
        Assert.Equal(SettlementStatus.Finalized, finalized.Value.Status);
        var monthBefore = await host.TotalsAsync(f.ClinicId, new DateOnly(y, m, 1), new DateOnly(y, m, 1).AddMonths(1).AddDays(-1));

        var late = await host.BackdatedPaymentAsync(f, appointment, Uzs(50_000.00m), Local(y, m, 20, 12, 0));

        var nextOpen = new DateOnly(y, m, 1).AddMonths(1);
        Assert.Equal(nextOpen, (await db.JournalEntries.SingleAsync(e => e.SourceEventKey == LedgerPoster.PaymentKey(late), Ct)).PostingDateLocal);
        Assert.Equal(nextOpen, (await db.JournalEntries.SingleAsync(e => e.SourceEventKey == LedgerPoster.PaymentCommissionKey(late), Ct)).PostingDateLocal);
        Assert.Equal(monthBefore, await host.TotalsAsync(f.ClinicId, new DateOnly(y, m, 1), nextOpen.AddDays(-1))); // the locked month is never restated
        var monthly = (await host.RunAsync(f.FinanceUserId, sp => sp.GetRequiredService<FinanceReportQueries>().MonthlyAsync(f.ClinicId, y, m, null, Ct))).Value;
        Assert.True(monthly.Locked);
        Assert.Equal("finance.period_locked", (await host.RunAsync(f.AdminUserId, sp => sp.GetRequiredService<SettlementHandlers>()
            .VoidAsync(created.Value.Id, new SettlementReasonCommand("xato"), Ct))).Error!.Code);
        Assert.Equal("finance.settlement_exists", (await host.RunAsync(f.AdminUserId, sp => sp.GetRequiredService<SettlementHandlers>()
            .CreateAsync(new CreateSettlementCommand(f.ClinicId, y, m, null), Ct))).Error!.Code);
        await FinanceInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task Settlement_lifecycle_paid_posts_clearing_and_void_releases_items_for_a_new_settlement()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var f = await FinanceWorld.CreateAsync(db);
        await host.AddRuleAsync(f, CommissionBasis.CollectedPayment, rate: 0.1000m, from: Local(2025, 1, 1, 0, 0));
        var month = Today().AddMonths(-1);
        var appointment = await host.ConfirmedAppointmentAsync(f);
        await host.BackdatedPaymentAsync(f, appointment, Uzs(200_000.00m), Local(month.Year, month.Month, 3, 10, 0));
        Task<Result<SettlementDto>> Admin(Func<SettlementHandlers, Task<Result<SettlementDto>>> call) =>
            host.RunAsync(f.AdminUserId, sp => call(sp.GetRequiredService<SettlementHandlers>()));

        var draft = (await Admin(h => h.CreateAsync(new CreateSettlementCommand(f.ClinicId, month.Year, month.Month, "30 kun ichida"), Ct))).Value;
        Assert.Equal(("20000.00", SettlementStatus.Draft, "30 kun ichida"), (draft.AmountDue.ToAmountString(), draft.Status, draft.PaymentTerms));
        Assert.Equal(SettlementStatus.Voided, (await Admin(h => h.VoidAsync(draft.Id, new SettlementReasonCommand("Qayta hisoblash"), Ct))).Value.Status);
        Assert.Equal(0, await db.SettlementItems.CountAsync(i => i.SettlementId == draft.Id, Ct));

        var again = (await Admin(h => h.CreateAsync(new CreateSettlementCommand(f.ClinicId, month.Year, month.Month, null), Ct))).Value;
        Assert.Equal(("20000.00", 1), (again.AmountDue.ToAmountString(), again.ItemCount));
        Assert.Equal("finance.invalid_settlement_state", (await Admin(h => h.MarkPaidAsync(again.Id, new MarkSettlementPaidCommand(DateTimeOffset.UtcNow, "PP-1"), Ct))).Error!.Code);
        await Admin(h => h.FinalizeAsync(again.Id, Ct));
        var paid = (await Admin(h => h.MarkPaidAsync(again.Id, new MarkSettlementPaidCommand(DateTimeOffset.UtcNow.AddMinutes(-5), "To'lov topshiriqnomasi 17"), Ct))).Value;
        Assert.Equal((SettlementStatus.Paid, "To'lov topshiriqnomasi 17"), (paid.Status, paid.PaidReference));
        Assert.Equal(0.00m, await host.AccountSumAsync(f.ClinicId, LedgerAccount.PlatformCommissionReceivable));
        Assert.Equal(20_000.00m, await host.AccountSumAsync(f.ClinicId, LedgerAccount.SettlementClearing));
        Assert.Equal("finance.invalid_settlement_state", (await Admin(h => h.FinalizeAsync(again.Id, Ct))).Error!.Code);
        var current = Today();
        Assert.Equal(CommonErrors.ValidationCode, (await Admin(h => h.CreateAsync(new CreateSettlementCommand(f.ClinicId, current.Year, current.Month, null), Ct))).Error!.Code);
        Assert.Equal(5, await db.AuditLog.CountAsync(a => a.ClinicId == f.ClinicId && SettlementActions.Contains(a.Action), Ct)); // created, voided, created, finalized, paid
        await FinanceInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task L3_rebuilding_the_summaries_equals_the_stored_summaries_and_a_tampered_row_is_overwritten_and_audited()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var f = await FinanceWorld.CreateAsync(db);
        await host.AddRuleAsync(f, CommissionBasis.CollectedPayment, rate: 0.1000m);
        var appointment = await host.CompletedAppointmentAsync(f, Uzs(120_000.00m));
        var payment = (await host.PayAsync(f, appointment, Uzs(120_000.00m), PaymentMethod.Card)).Value;
        Assert.True((await host.RefundAsync(f, payment.Id, Uzs(20_000.00m))).IsSuccess);
        Task<SummaryRebuildResult> Rebuild() => host.RunAsync(null, sp => sp.GetRequiredService<RebuildDailySummariesHandler>().RunAsync(Ct));

        var first = await Rebuild();
        Assert.Empty(first.Mismatches);
        var second = await Rebuild();
        Assert.Equal((0, 0), (second.Written, second.Mismatches.Count)); // rebuild = stored
        var (from, to) = AroundToday();
        var stored = (await db.DailyFinancialSummaries.AsNoTracking().Where(s => s.ClinicId == f.ClinicId).ToListAsync(Ct)).Aggregate(DailyTotals.Zero, (a, s) => a.Plus(s.Totals));
        Assert.Equal(await host.TotalsAsync(f.ClinicId, from, to), stored);
        var today = Today();
        Assert.True((await host.RunAsync(f.FinanceUserId, sp => sp.GetRequiredService<FinanceReportQueries>().MonthlyAsync(f.ClinicId, today.Year, today.Month, null, Ct))).Value.Reconciled);

        await using (var c = new NpgsqlConnection(host.ConnectionString))
        {
            await c.OpenAsync(Ct);
            await using var cmd = new NpgsqlCommand("UPDATE finance.daily_financial_summaries SET collected = collected + 1 WHERE clinic_id = @c", c);
            cmd.Parameters.AddWithValue("c", f.ClinicId);
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync(Ct));
        }

        Assert.False((await host.RunAsync(f.FinanceUserId, sp => sp.GetRequiredService<FinanceReportQueries>().MonthlyAsync(f.ClinicId, today.Year, today.Month, null, Ct))).Value.Reconciled);
        var third = await Rebuild();
        var mismatch = Assert.Single(third.Mismatches);
        Assert.Equal(mismatch.Ledger.Collected + 1, mismatch.Stored.Collected);
        Assert.Equal(1, await db.AuditLog.CountAsync(a => a.Action == "finance.summary.mismatch" && a.ClinicId == f.ClinicId, Ct));
        Assert.Empty((await Rebuild()).Mismatches);
        await FinanceInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task Payments_are_refused_for_appointments_that_are_not_confirmed_or_completed()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var f = await FinanceWorld.CreateAsync(db);
        var pending = (await host.BookAsync(f.W, f.W.Patients[0], f.NextSlot())).Value;
        Assert.Equal("finance.appointment_not_payable", (await host.PayAsync(f, pending.Id, Uzs(1_000.00m))).Error!.Code);
        Assert.Equal(FinanceErrors.AmountInvalidCode, (await host.PayAsync(f, pending.Id, Uzs(0.00m))).Error!.Code);
        Assert.Equal(0, await db.Payments.CountAsync(Ct));
        Assert.Equal(AppointmentStatus.Pending, (await db.Appointments.SingleAsync(a => a.Id == pending.Id, Ct)).Status);
    }
}
