using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OqTish.Application.Booking;
using OqTish.Application.Common;
using OqTish.Application.Finance;
using OqTish.Application.Staff;
using OqTish.Domain.Common;
using OqTish.Domain.Finance;
using OqTish.Domain.Identity;
using OqTish.Domain.Staff;
using OqTish.Infrastructure.Persistence;
using OqTish.IntegrationTests.Booking;

namespace OqTish.IntegrationTests.Finance;

/// <summary>
/// A finance fixture inside an existing database: a <see cref="BookingWorld"/> (Approved clinic, Asia/Tashkent branch,
/// doctors, receptionist = <see cref="BookingWorld.StaffUserId"/>, patients) plus a clinic-wide FinanceManager and a
/// platform admin with <c>platform.finance.settle</c> (the TOTP rule is off in <see cref="BookingHost"/>). Slots for
/// bookings are handed out by <see cref="NextSlot"/> so appointments never collide.
/// </summary>
public sealed class FinanceWorld
{
    private int _slot;

    private FinanceWorld(BookingWorld world, Guid financeUserId, Guid adminUserId)
    {
        W = world;
        FinanceUserId = financeUserId;
        AdminUserId = adminUserId;
    }

    public BookingWorld W { get; }

    public Guid ClinicId => W.ClinicId;

    public Guid BranchId => W.BranchId;

    public Guid ReceptionUserId => W.StaffUserId;

    public Guid FinanceUserId { get; }

    public Guid AdminUserId { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<FinanceWorld> CreateAsync(OqTishDbContext db, int doctors = 1, int patients = 2)
    {
        ArgumentNullException.ThrowIfNull(db);
        var world = await BookingWorld.CreateAsync(db, doctors, patients);
        var now = DateTimeOffset.UtcNow;
        var finance = User.Create("Moliya menejeri", now);
        var admin = User.Create("Platforma admini", now);
        db.Users.AddRange(finance, admin);
        await db.SaveChangesAsync(Ct);
        db.ClinicMemberships.Add(ClinicMembership.Create(world.ClinicId, finance.Id, StaffRole.FinanceManager, MembershipScope.Clinic, now));
        db.PlatformAdminGrants.Add(PlatformAdminGrant.Create(admin.Id, PlatformPermissions.FinanceSettle, null, now));
        await db.SaveChangesAsync(Ct);
        return new FinanceWorld(world, finance.Id, admin.Id);
    }

    /// <summary>A free future start on the 15-minute grid (08:00–19:00 branch time, then the next days).</summary>
    public DateTimeOffset NextSlot()
    {
        var n = Interlocked.Increment(ref _slot) - 1;
        return W.At(8 + (n % 22 / 2), n % 2 * 30, days: n / 22);
    }

    /// <summary>A past start for completed visits (distinct per call, 2–30 days ago, never overlapping).</summary>
    public DateTimeOffset NextPastStart() => DateTimeOffset.UtcNow.AddDays(-2).AddHours(-Interlocked.Increment(ref _slot));
}

public static class FinanceCalls
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static Money Uzs(decimal amount) => Money.Uzs(amount);

    /// <summary>Patient request → staff confirm with the first doctor. Returns the appointment id (Confirmed, in the future).</summary>
    public static async Task<Guid> ConfirmedAppointmentAsync(this BookingHost host, FinanceWorld f, int patient = 0)
    {
        ArgumentNullException.ThrowIfNull(f);
        var booked = await host.BookAsync(f.W, f.W.Patients[patient], f.NextSlot());
        Assert.True(booked.IsSuccess, booked.Error?.Code);
        var confirmed = await host.ConfirmAsync(f.W, booked.Value.Id, f.W.Doctors[0]);
        Assert.True(confirmed.IsSuccess, confirmed.Error?.Code);
        return booked.Value.Id;
    }

    /// <summary>Confirmed appointment moved into the past, then completed by the receptionist with <paramref name="charged"/> (runs the BE3 hook).</summary>
    public static async Task<Guid> CompletedAppointmentAsync(this BookingHost host, FinanceWorld f, Money charged, int patient = 0)
    {
        ArgumentNullException.ThrowIfNull(host);
        var id = await host.ConfirmedAppointmentAsync(f, patient);
        await BookingCalls.ShiftAsync(host.ConnectionString, id, f.NextPastStart());
        var completed = await host.RunAsync(f.ReceptionUserId, sp => sp.GetRequiredService<StaffAppointmentHandlers>()
            .CompleteAsync(f.ClinicId, id, new CompleteCommand(charged), Ct));
        Assert.True(completed.IsSuccess, completed.Error?.Code);
        return id;
    }

    public static Task<Result<PaymentDto>> PayAsync(this BookingHost host, FinanceWorld f, Guid appointmentId, Money amount, PaymentMethod method = PaymentMethod.Cash,
        string? key = null, Guid? user = null, PaymentStatus? status = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(f);
        return host.RunAsync(user ?? f.ReceptionUserId, sp => sp.GetRequiredService<PaymentHandlers>()
            .RecordAsync(f.ClinicId, appointmentId, new RecordPaymentCommand(amount, method, null, status), key ?? Guid.NewGuid().ToString("N"), Ct));
    }

    public static Task<Result<PaymentDto>> RefundAsync(this BookingHost host, FinanceWorld f, Guid paymentId, Money amount, string? key = null, Guid? user = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(f);
        return host.RunAsync(user ?? f.FinanceUserId, sp => sp.GetRequiredService<PaymentHandlers>()
            .RefundAsync(f.ClinicId, paymentId, new RefundCommand(amount, "Bemor iltimosi"), key ?? Guid.NewGuid().ToString("N"), Ct));
    }

    public static Task<Result<PaymentDto>> FailAsync(this BookingHost host, FinanceWorld f, Guid paymentId, string reason = "Karta qaytdi")
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(f);
        return host.RunAsync(f.ReceptionUserId, sp => sp.GetRequiredService<PaymentHandlers>().FailAsync(f.ClinicId, paymentId, new FailPaymentCommand(reason), Ct));
    }

    public static Task<Result<BalanceDto>> BalanceAsync(this BookingHost host, FinanceWorld f, Guid appointmentId)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(f);
        return host.RunAsync(f.FinanceUserId, sp => sp.GetRequiredService<PaymentHandlers>().BalanceAsync(f.ClinicId, appointmentId, Ct));
    }

    /// <summary>Inserts an approved rule directly (fixture parameters, not a contract).</summary>
    public static async Task<Guid> AddRuleAsync(this BookingHost host, FinanceWorld f, CommissionBasis basis, decimal? rate = null, Money? fee = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(f);
        await using var db = host.CreateDbContext();
        var rule = CommissionRule.Create(f.ClinicId, basis, rate, fee, from ?? DateTimeOffset.UtcNow.AddYears(-1), to, "TEST fixture", f.AdminUserId, DateTimeOffset.UtcNow);
        db.CommissionRules.Add(rule);
        await db.SaveChangesAsync(Ct);
        return rule.Id;
    }

    /// <summary>Runs <paramref name="work"/> inside one transaction of a DI scope acting as <paramref name="user"/>.</summary>
    public static Task InTransactionAsync(this BookingHost host, Guid? user, Func<IServiceProvider, CancellationToken, Task> work)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(work);
        return host.RunAsync(user, async sp =>
        {
            var result = await sp.GetRequiredService<ITransactionRunner>().RunAsync(async t =>
            {
                await work(sp, t);
                return Result.Success;
            }, Ct);
            Assert.True(result.IsSuccess);
            return true;
        });
    }

    /// <summary>
    /// A payment recorded at an explicit (past) instant through the real poster — for rule-selection, posting-date and
    /// period tests where database time cannot be controlled.
    /// </summary>
    public static async Task<Guid> BackdatedPaymentAsync(this BookingHost host, FinanceWorld f, Guid appointmentId, Money amount, DateTimeOffset recordedAt,
        PaymentMethod method = PaymentMethod.Cash)
    {
        ArgumentNullException.ThrowIfNull(f);
        var id = Guid.Empty;
        await host.InTransactionAsync(f.ReceptionUserId, async (sp, t) =>
        {
            var db = sp.GetRequiredService<IOqTishDb>();
            var a = await db.Appointments.AsNoTracking().FirstAsync(x => x.Id == appointmentId, t);
            var payment = Payment.RecordIn(a.ClinicId, a.BranchId, a.Id, amount, method, PaymentStatus.Recorded, "backdated:" + Guid.NewGuid().ToString("N"), null,
                f.ReceptionUserId, recordedAt);
            db.Payments.Add(payment);
            await db.SaveChangesAsync(t);
            await sp.GetRequiredService<LedgerPoster>().PostPaymentAsync(payment, t);
            id = payment.Id;
        });
        return id;
    }

    /// <summary>A refund recorded at an explicit instant through the real poster.</summary>
    public static async Task<Guid> BackdatedRefundAsync(this BookingHost host, FinanceWorld f, Guid paymentId, Money amount, DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(f);
        var id = Guid.Empty;
        await host.InTransactionAsync(f.FinanceUserId, async (sp, t) =>
        {
            var db = sp.GetRequiredService<IOqTishDb>();
            var original = await db.Payments.AsNoTracking().FirstAsync(p => p.Id == paymentId, t);
            var refund = Payment.RecordRefund(original, amount, "Qaytarish", "backdated:" + Guid.NewGuid().ToString("N"), f.FinanceUserId, recordedAt);
            db.Payments.Add(refund);
            await db.SaveChangesAsync(t);
            await sp.GetRequiredService<LedgerPoster>().PostRefundAsync(refund, original, t);
            id = refund.Id;
        });
        return id;
    }

    /// <summary>Signed commission accrual/reversal amounts (PlatformCommissionReceivable lines) of the clinic in posting order.</summary>
    public static async Task<List<(decimal Amount, decimal? Rate, Guid? RuleId, DateOnly Date)>> CommissionLinesAsync(this BookingHost host, Guid clinicId)
    {
        ArgumentNullException.ThrowIfNull(host);
        await using var db = host.CreateDbContext();
        var rows = await (
                from l in db.LedgerLines
                join e in db.JournalEntries on l.JournalEntryId equals e.Id
                where e.ClinicId == clinicId && l.Account == LedgerAccount.PlatformCommissionReceivable
                      && (e.EntryType == JournalEntryType.Commission || e.EntryType == JournalEntryType.CommissionReversal)
                orderby e.Sequence
                select new { l.Amount, l.RateSnapshot, l.CommissionRuleId, e.PostingDateLocal })
            .ToListAsync(Ct);
        return [.. rows.Select(r => (r.Amount, r.RateSnapshot, r.CommissionRuleId, r.PostingDateLocal))];
    }

    /// <summary>Σ of one account over the clinic's ledger (optionally one appointment).</summary>
    public static async Task<decimal> AccountSumAsync(this BookingHost host, Guid clinicId, LedgerAccount account, Guid? appointmentId = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        await using var db = host.CreateDbContext();
        return await (
                from l in db.LedgerLines
                join e in db.JournalEntries on l.JournalEntryId equals e.Id
                where e.ClinicId == clinicId && l.Account == account && (appointmentId == null || e.AppointmentId == appointmentId)
                select (decimal?)l.Amount)
            .SumAsync(Ct) ?? 0m;
    }

    public static async Task<DailyTotals> TotalsAsync(this BookingHost host, Guid clinicId, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(host);
        return await host.RunAsync(null, async sp =>
            LedgerTotals.Sum((await sp.GetRequiredService<LedgerTotals>().ComputeAsync(clinicId, null, from, to, Ct)).Values));
    }

    public static DateOnly Today() => BookingTime.LocalDate(DateTimeOffset.UtcNow, BookingWorld.Zone);

    /// <summary>UTC instant of a branch-local (Asia/Tashkent) wall-clock time.</summary>
    public static DateTimeOffset Local(int year, int month, int day, int hour, int minute = 0) =>
        BookingTime.ToUtc(new DateOnly(year, month, day), new TimeSpan(hour, minute, 0), BookingWorld.Zone)!.Value;
}

/// <summary>
/// TESTING.md §4 L2 and related ledger invariants, checked on database state at the end of every finance test:
/// every journal entry has ≥ 2 lines summing to 0 per currency; refunds never exceed their payment; a Failed payment
/// has a net-zero ledger effect; every settlement item is a commission receivable line of its settlement's month.
/// </summary>
public static class FinanceInvariants
{
    private const string Sql = """
        SELECT
          (SELECT count(*) FROM finance.journal_entries e
             WHERE (SELECT count(*) FROM finance.ledger_lines l WHERE l.journal_entry_id = e.id) < 2
                OR EXISTS (SELECT 1 FROM finance.ledger_lines l WHERE l.journal_entry_id = e.id GROUP BY l.currency HAVING sum(l.amount) <> 0)) AS unbalanced,
          (SELECT count(*) FROM finance.payments p
             WHERE p.direction = 'In' AND (SELECT coalesce(sum(r.amount), 0) FROM finance.payments r WHERE r.refund_of_payment_id = p.id) > p.amount) AS over_refunded,
          (SELECT count(*) FROM finance.payments p
             WHERE p.status = 'Failed' AND (SELECT coalesce(sum(l.amount), 0) FROM finance.ledger_lines l JOIN finance.journal_entries e ON e.id = l.journal_entry_id
                                            WHERE e.payment_id = p.id AND l.account LIKE 'ClinicCollected%') <> 0) AS failed_with_effect,
          (SELECT count(*) FROM finance.settlement_items i
             JOIN finance.settlements s ON s.id = i.settlement_id
             JOIN finance.ledger_lines l ON l.id = i.ledger_line_id
             JOIN finance.journal_entries e ON e.id = l.journal_entry_id
             WHERE l.account <> 'PlatformCommissionReceivable' OR e.posting_date_local < s.period_start OR e.posting_date_local >= s.period_end) AS bad_items
        """;

    public static async Task AssertAsync(string connectionString)
    {
        await using var c = new NpgsqlConnection(connectionString);
        await c.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand(Sql, c);
        await using var r = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await r.ReadAsync(TestContext.Current.CancellationToken);
        var values = (r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3));
        Assert.True(values == (0, 0, 0, 0), $"Ledger invariants violated: unbalanced={values.Item1}, overRefunded={values.Item2}, failedWithEffect={values.Item3}, badSettlementItems={values.Item4}");
    }
}
