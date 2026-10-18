using Microsoft.Extensions.Options;
using OqTish.Application.Notifications;

namespace OqTish.Worker;

/// <summary>
/// Runs <see cref="OutboxDispatcher"/> every <c>Notifications:DispatchIntervalSeconds</c> (default 2 s). A full batch is
/// followed immediately by the next one. Each batch has its own DI scope. A failing run is logged (exception type only)
/// and retried on the next tick; rows of a crashed run are reclaimed after their lease.
/// </summary>
internal sealed partial class OutboxDispatcherService(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationOptions> options,
    ILogger<OutboxDispatcherService> logger,
    TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.DispatchIntervalSeconds));
        var batch = Math.Clamp(options.Value.BatchSize, 1, 500);
        using var timer = new PeriodicTimer(interval, clock);
        do
        {
            try
            {
                int claimed;
                do
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    claimed = await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().RunOnceAsync(stoppingToken);
                }
                while (claimed >= batch && !stoppingToken.IsCancellationRequested);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox dispatch run failed ({ExceptionType}); retrying on the next tick")]
    private static partial void LogFailed(ILogger logger, string exceptionType);
}
