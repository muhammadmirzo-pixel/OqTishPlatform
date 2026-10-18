using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OqTish.Application.Common;
using OqTish.Application.Finance;
using OqTish.Application.Staff;
using OqTish.Domain.Common;
using OqTish.Domain.Finance;
using OqTish.Domain.Identity;
using OqTish.Domain.Staff;
using OqTish.IntegrationTests.Booking;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Finance.FinanceCalls;

namespace OqTish.IntegrationTests.Finance;

/// <summary>
/// Finance concurrency catalogue (TESTING.md §4 F4, F12 and the period lock): each test loops
/// <see cref="Iterations"/> times over a fresh fixture in the same database, releases the racing calls together
/// (<see cref="Race"/>) and asserts database state plus the ledger invariants.
/// </summary>
[Trait("Category", "Concurrency")]
public class FinanceConcurrencyTests(PostgresFixture postgres)
{
    public const int Iterations = 50;

    private static readonly string[] RefundOutcomes = ["finance.refund_exceeds_paid"];

    private static readonly decimal[] PossibleAmounts = [10_000.00m, 14_000.00m];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void Report(string name, Stopwatch sw) =>
        TestContext.Current.TestOutputHelper?.WriteLine($"{name}: {Iterations} iterations in {sw.Elapsed.TotalSeconds:F1} s");

    [Fact]
    public async Task F4_concurrent_duplicates_with_one_idempotency_key_give_one_payment_and_one_journal_entry()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < Iterations; i++)
        {
            await using var db = host.CreateDbContext();
            var f = await FinanceWorld.CreateAsync(db);
            await host.AddRuleAsync(f, CommissionBasis.CollectedPayment, rate: 0.1000m);
            var appointment = await host.ConfirmedAppointmentAsync(f);
            var command = new RecordPaymentCommand(Money.Uzs(250_000.00m), PaymentMethod.Card, "TERM-1", null);
            var results = await Race.RunAsync(host, [.. Enumerable.Range(0, 5).Select(_ =>
                ((Guid?)f.ReceptionUserId, (Func<IServiceProvider, Task<Result<PaymentDto>>>)(sp =>
                    sp.GetRequiredService<PaymentHandlers>().RecordAsync(f.ClinicId, appointment, command, "same-key", Ct))))]);

            Assert.All(results, r => Assert.True(r.IsSuccess, r.Error?.Code));
            Assert.Single(results.Select(r => r.Value.Id).Distinct());
            Assert.Equal(1, await db.Payments.CountAsync(p => p.AppointmentId == appointment, Ct));
            Assert.Equal(1, await db.JournalEntries.CountAsync(e => e.AppointmentId == appointment && e.EntryType == JournalEntryType.PaymentRecorded, Ct));
            Assert.Equal(1, await db.JournalEntries.CountAsync(e => e.AppointmentId == appointment && e.EntryType == JournalEntryType.Commission, Ct));
        }

        Report("F4", sw);
        await FinanceInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task F4_the_same_key_used_by_another_user_of_the_clinic_is_refused_by_the_database_backstop()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var f = await FinanceWorld.CreateAsync(db);
        var appointment = await host.ConfirmedAppointmentAsync(f);
        Assert.True((await host.PayAsync(f, appointment, Uzs(10_000.00m), key: "shared-key")).IsSuccess);

        var other = await host.PayAsync(f, appointment, Uzs(10_000.00m), key: "shared-key", user: f.FinanceUserId);

        Assert.Equal("common.idempotency_mismatch", other.Error!.Code);
        Assert.Equal(1, await db.Payments.CountAsync(p => p.AppointmentId == appointment, Ct));
        await FinanceInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task Concurrent_refunds_of_one_payment_never_exceed_the_paid_amount()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < Iterations; i++)
        {
            await using var db = host.CreateDbContext();
            var f = await FinanceWorld.CreateAsync(db);
            await host.AddRuleAsync(f, CommissionBasis.CollectedPayment, rate: 0.1000m);
            var appointment = await host.ConfirmedAppointmentAsync(f);
            var payment = (await host.PayAsync(f, appointment, Uzs(100_000.00m))).Value;
            var results = await Race.RunAsync(host, [.. Enumerable.Range(0, 5).Select(k =>
                ((Guid?)f.FinanceUserId, (Func<IServiceProvider, Task<Result<PaymentDto>>>)(sp =>
                    sp.GetRequiredService<PaymentHandlers>().RefundAsync(f.ClinicId, payment.Id, new RefundCommand(Money.Uzs(30_000.00m), "Qaytarish"), $"refund-{k}", Ct))))]);

            Assert.Equal(3, results.Count(r => r.IsSuccess));
            Assert.All(results.Where(r => !r.IsSuccess), r => Assert.Contains(r.Error!.Code, RefundOutcomes));
            Assert.Equal(90_000.00m, await db.Payments.Where(p => p.RefundOfPaymentId == payment.Id).SumAsync(p => p.Amount, Ct));
            Assert.Equal(-9_000.00m, (await host.CommissionLinesAsync(f.ClinicId)).Where(l => l.Amount < 0).Sum(l => l.Amount));
        }

        Report("Concurrent refunds", sw);
        await FinanceInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task F12_two_admins_create_the_same_monthly_settlement_concurrently_201_and_409()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        var month = Today().AddMonths(-1);
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < Iterations; i++)
        {
            await using var db = host.CreateDbContext();
            var f = await FinanceWorld.CreateAsync(db);
            var secondAdmin = User.Create("Ikkinchi admin", DateTimeOffset.UtcNow);
            db.Users.Add(secondAdmin);
            await db.SaveChangesAsync(Ct);
            db.PlatformAdminGrants.Add(PlatformAdminGrant.Create(secondAdmin.Id, PlatformPermissions.FinanceSettle, null, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(Ct);
            await host.AddRuleAsync(f, CommissionBasis.CollectedPayment, rate: 0.1000m, from: Local(2025, 1, 1, 0, 0));
            var appointment = await host.ConfirmedAppointmentAsync(f);
            await host.BackdatedPaymentAsync(f, appointment, Uzs(100_000.00m), Local(month.Year, month.Month, 5, 10, 0));
            await host.BackdatedPaymentAsync(f, appointment, Uzs(50_000.00m), Local(month.Year, month.Month, 6, 10, 0));
            var command = new CreateSettlementCommand(f.ClinicId, month.Year, month.Month, null);

            var results = await Race.RunAsync(host, [.. new[] { f.AdminUserId, secondAdmin.Id }.Select(admin =>
                ((Guid?)admin, (Func<IServiceProvider, Task<Result<SettlementDto>>>)(sp => sp.GetRequiredService<SettlementHandlers>().CreateAsync(command, Ct))))]);

            var created = Assert.Single(results, r => r.IsSuccess);
            Assert.Equal("finance.settlement_exists", Assert.Single(results, r => !r.IsSuccess).Error!.Code);
            Assert.Equal(("15000.00", 2), (created.Value.AmountDue.ToAmountString(), created.Value.ItemCount));
            Assert.Equal(1, await db.Settlements.CountAsync(s => s.ClinicId == f.ClinicId, Ct));
            Assert.Equal(2, await db.SettlementItems.CountAsync(x => x.ClinicId == f.ClinicId, Ct));
        }

        Report("F12", sw);
        await FinanceInvariants.AssertAsync(host.ConnectionString);
    }

    /// <summary>
    /// A back-dated posting into month M racing the finalization of M: either the posting commits first and the
    /// finalize collects its commission line, or it waits for the exclusive period lock and is redirected to M+1 —
    /// never an unsettled commission line inside a finalized month.
    /// </summary>
    [Fact]
    public async Task Finalize_racing_a_posting_into_the_same_month_never_leaves_an_unsettled_line_in_the_locked_month()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        var month = Today().AddMonths(-2);
        var (start, end) = (new DateOnly(month.Year, month.Month, 1), new DateOnly(month.Year, month.Month, 1).AddMonths(1));
        var redirected = 0;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < Iterations; i++)
        {
            await using var db = host.CreateDbContext();
            var f = await FinanceWorld.CreateAsync(db);
            await host.AddRuleAsync(f, CommissionBasis.CollectedPayment, rate: 0.1000m, from: Local(2025, 1, 1, 0, 0));
            var appointment = await host.ConfirmedAppointmentAsync(f);
            await host.BackdatedPaymentAsync(f, appointment, Uzs(100_000.00m), Local(month.Year, month.Month, 3, 10, 0));
            var draft = (await host.RunAsync(f.AdminUserId, sp => sp.GetRequiredService<SettlementHandlers>()
                .CreateAsync(new CreateSettlementCommand(f.ClinicId, month.Year, month.Month, null), Ct))).Value;
            var a = await db.Appointments.AsNoTracking().SingleAsync(x => x.Id == appointment, Ct);

            await Race.RunAsync<bool>(host,
            [
                (f.AdminUserId, async sp => (await sp.GetRequiredService<SettlementHandlers>().FinalizeAsync(draft.Id, Ct)).IsSuccess),
                (f.ReceptionUserId, async sp => (await sp.GetRequiredService<ITransactionRunner>().RunAsync(async t =>
                {
                    var payment = Payment.RecordIn(a.ClinicId, a.BranchId, a.Id, Money.Uzs(40_000.00m), PaymentMethod.Cash, PaymentStatus.Recorded, null, null,
                        f.ReceptionUserId, Local(month.Year, month.Month, 25, 10, 0));
                    var dbs = sp.GetRequiredService<IOqTishDb>();
                    dbs.Payments.Add(payment);
                    await dbs.SaveChangesAsync(t);
                    await sp.GetRequiredService<LedgerPoster>().PostPaymentAsync(payment, t);
                    return Result.Success;
                }, Ct)).IsSuccess),
            ]);

            var settlement = await db.Settlements.AsNoTracking().SingleAsync(s => s.Id == draft.Id, Ct);
            Assert.Equal(SettlementStatus.Finalized, settlement.Status);
            var unsettledInMonth = await (
                    from l in db.LedgerLines
                    join e in db.JournalEntries on l.JournalEntryId equals e.Id
                    where e.ClinicId == f.ClinicId && l.Account == LedgerAccount.PlatformCommissionReceivable
                          && e.PostingDateLocal >= start && e.PostingDateLocal < end && !db.SettlementItems.Any(x => x.LedgerLineId == l.Id)
                    select l.Id)
                .CountAsync(Ct);
            Assert.Equal(0, unsettledInMonth);
            var items = await db.SettlementItems.Where(x => x.SettlementId == settlement.Id).SumAsync(x => x.Amount, Ct);
            Assert.Equal(settlement.AmountDue, items);
            Assert.Contains(settlement.AmountDue, PossibleAmounts);
            redirected += settlement.AmountDue == 10_000.00m ? 1 : 0;
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"posting redirected to the next month in {redirected}/{Iterations} iterations");
        Report("Finalize vs posting", sw);
        await FinanceInvariants.AssertAsync(host.ConnectionString);
    }
}
