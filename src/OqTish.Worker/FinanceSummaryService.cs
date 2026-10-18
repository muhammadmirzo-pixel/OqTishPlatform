using System.Globalization;
using Microsoft.Extensions.Options;
using OqTish.Application.Booking;
using OqTish.Application.Finance;
using OqTish.Domain.Tenancy;

namespace OqTish.Worker;

/// <summary>
/// Nightly finance summary rebuild + reconciliation (FINANCIAL_RULES §7): once at startup and then every day at
/// <c>Finance:SummaryRunAtLocalTime</c> (default 02:00 Asia/Tashkent) it runs <see cref="RebuildDailySummariesHandler"/>
/// over the trailing <c>Finance:SummaryWindowDays</c>. Every mismatch between the cache and the ledger is logged as a
/// warning (branch id, date — no personal data), overwritten from the ledger and audited by the handler. A failing
/// run is logged (exception type only) and retried at the next run time.
/// </summary>
internal sealed partial class FinanceSummaryService(
    IServiceScopeFactory scopeFactory,
    IOptions<FinanceOptions> options,
    ILogger<FinanceSummaryService> logger,
    TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            await Task.Delay(DelayUntilNextRun(clock.GetUtcNow(), options.Value.SummaryRunAtLocalTime), clock, stoppingToken);
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<RebuildDailySummariesHandler>().RunAsync(ct);
            foreach (var m in result.Mismatches)
            {
                LogMismatch(logger, m.BranchId, m.Date);
            }

            LogCompleted(logger, result.From, result.To, result.Written, result.Mismatches.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex.GetType().Name);
        }
    }

    /// <summary>Time until the next local HH:mm in Asia/Tashkent (at least one minute).</summary>
    internal static TimeSpan DelayUntilNextRun(DateTimeOffset now, string? runAt)
    {
        if (!TimeOnly.TryParseExact(runAt, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            time = new TimeOnly(2, 0);
        }

        var zone = BookingTime.Zone(Branch.DefaultTimeZone);
        var today = BookingTime.LocalDate(now, zone);
        var next = BookingTime.ToUtc(today, time.ToTimeSpan(), zone) ?? now.AddDays(1);
        if (next <= now)
        {
            next = BookingTime.ToUtc(today.AddDays(1), time.ToTimeSpan(), zone) ?? now.AddDays(1);
        }

        var delay = next - now;
        return delay < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : delay;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Finance summary mismatch for branch {BranchId} on {Date}: cache overwritten from the ledger")]
    private static partial void LogMismatch(ILogger logger, Guid branchId, DateOnly date);

    [LoggerMessage(Level = LogLevel.Information, Message = "Finance summaries rebuilt for {From}..{To}: {Written} row(s) written, {Mismatches} mismatch(es)")]
    private static partial void LogCompleted(ILogger logger, DateOnly from, DateOnly to, int written, int mismatches);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Finance summary rebuild failed ({ExceptionType}); retrying at the next run")]
    private static partial void LogFailed(ILogger logger, string exceptionType);
}
