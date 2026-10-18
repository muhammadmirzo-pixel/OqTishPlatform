extern alias WorkerHost;

using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OqTish.Application.Finance;
using OqTish.Application.Tenancy;
using OqTish.Domain.Finance;
using OqTish.IntegrationTests.Booking;
using OqTish.IntegrationTests.Infrastructure;
using WorkerHost::OqTish.Worker;
using static OqTish.IntegrationTests.Finance.FinanceCalls;

namespace OqTish.IntegrationTests.Finance;

/// <summary>Development finance seed (idempotent, balanced, clearly labelled demo rule) and the Worker summary job.</summary>
public class FinanceSeedAndWorkerTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<int[]> CountsAsync(ApiFactory factory)
    {
        await using var db = factory.CreateDbContext();
        return
        [
            await db.CommissionRules.CountAsync(Ct), await db.ServiceCompletions.CountAsync(Ct), await db.Payments.CountAsync(Ct),
            await db.JournalEntries.CountAsync(Ct), await db.LedgerLines.CountAsync(Ct),
        ];
    }

    [Fact]
    public async Task Finance_demo_seed_is_idempotent_balanced_and_labelled_demo()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateApiClient();
        (await client.PostAsync("/api/dev/seed", null, Ct)).AssertStatus(HttpStatusCode.OK);
        var first = await CountsAsync(factory);
        (await client.PostAsync("/api/dev/seed", null, Ct)).AssertStatus(HttpStatusCode.OK);
        Assert.Equal(first, await CountsAsync(factory));
        Assert.Equal([1, 2, 2], first[..3]);

        await using var db = factory.CreateDbContext();
        var clinic = await db.Clinics.SingleAsync(c => c.Name == DemoData.Clinics[0].Name, Ct);
        var rule = await db.CommissionRules.SingleAsync(Ct);
        Assert.Equal((clinic.Id, CommissionBasis.CollectedPayment, 0.1000m), (rule.ClinicId, rule.Basis, rule.Rate!.Value));
        Assert.StartsWith("DEMO-", rule.ContractReference, StringComparison.Ordinal);
        Assert.Equal(2, await db.JournalEntries.CountAsync(e => e.EntryType == JournalEntryType.Commission, Ct));
        await FinanceInvariants.AssertAsync(factory.ConnectionString);
    }

    [Fact]
    public async Task Worker_summary_job_rebuilds_the_cache_from_the_ledger()
    {
        await using var host = await BookingHost.CreateAsync(postgres);
        await using var db = host.CreateDbContext();
        var f = await FinanceWorld.CreateAsync(db);
        var appointment = await host.CompletedAppointmentAsync(f, Uzs(90_000.00m));
        Assert.True((await host.PayAsync(f, appointment, Uzs(90_000.00m))).IsSuccess);

        var service = new FinanceSummaryService(host.Services.GetRequiredService<IServiceScopeFactory>(), Options.Create(new FinanceOptions()),
            NullLogger<FinanceSummaryService>.Instance, TimeProvider.System);
        await service.RunOnceAsync(Ct);

        var row = await db.DailyFinancialSummaries.AsNoTracking().SingleAsync(s => s.ClinicId == f.ClinicId, Ct);
        Assert.Equal((1, 90_000.00m, 90_000.00m), (row.Visits, row.Charged, row.Collected));
        Assert.True(row.LedgerWatermark > 0);
    }

    [Fact]
    public void Next_run_is_the_next_local_run_time_in_tashkent()
    {
        var now = new DateTimeOffset(2026, 10, 18, 20, 0, 0, TimeSpan.Zero); // 01:00 local
        Assert.Equal(TimeSpan.FromHours(1), FinanceSummaryService.DelayUntilNextRun(now, "02:00"));
        Assert.Equal(TimeSpan.FromHours(23), FinanceSummaryService.DelayUntilNextRun(now.AddHours(2), "02:00")); // 03:00 → tomorrow 02:00
        Assert.Equal(TimeSpan.FromHours(1), FinanceSummaryService.DelayUntilNextRun(now, "garbage")); // default 02:00
    }
}
