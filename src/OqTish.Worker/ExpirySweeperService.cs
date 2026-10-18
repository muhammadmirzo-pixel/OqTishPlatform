using Microsoft.Extensions.Options;
using OqTish.Application.Booking;

namespace OqTish.Worker;

/// <summary>
/// Expires overdue Pending requests every <c>Booking:SweeperIntervalSeconds</c> (default 30 s) through
/// <see cref="ExpirePendingAppointmentsHandler"/> (SKIP LOCKED batches, conditional update, blocks released,
/// history, outbox Expired notices). Correctness never depends on it: confirm/cancel compare the deadline with
/// database time and booking sweeps on write; the sweeper only makes side effects (notices, freed time) prompt.
/// A failing run is logged (type only) and retried on the next tick.
/// </summary>
internal sealed partial class ExpirySweeperService(
    IServiceScopeFactory scopeFactory,
    IOptions<BookingOptions> options,
    ILogger<ExpirySweeperService> logger,
    TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.SweeperIntervalSeconds));
        using var timer = new PeriodicTimer(interval, clock);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var expired = await scope.ServiceProvider.GetRequiredService<ExpirePendingAppointmentsHandler>().RunAsync(stoppingToken);
                if (expired > 0)
                {
                    LogExpired(logger, expired);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Expiry sweeper expired {Count} pending request(s)")]
    private static partial void LogExpired(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Expiry sweeper run failed ({ExceptionType}); retrying on the next tick")]
    private static partial void LogFailed(ILogger logger, string exceptionType);
}
