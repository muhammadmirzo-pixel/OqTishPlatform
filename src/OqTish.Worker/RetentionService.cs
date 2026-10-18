using Microsoft.Extensions.Options;
using OqTish.Application.Notifications;

namespace OqTish.Worker;

/// <summary>
/// Nightly retention (OD-18): once at startup and then daily at <c>Retention:RunAtLocalTime</c> (default 03:30
/// Asia/Tashkent) it runs <see cref="RetentionHandler"/> and logs the deleted row counts. <c>Retention:Enabled=false</c>
/// turns it off. A failing run is logged (exception type only) and retried at the next run time.
/// </summary>
internal sealed partial class RetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<RetentionOptions> options,
    ILogger<RetentionService> logger,
    TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            LogDisabled(logger);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            await Task.Delay(FinanceSummaryService.DelayUntilNextRun(clock.GetUtcNow(), options.Value.RunAtLocalTime), clock, stoppingToken);
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var r = await scope.ServiceProvider.GetRequiredService<RetentionHandler>().RunAsync(ct);
            LogCompleted(logger, r.OtpChallenges, r.Sessions, r.OutboxMessages, r.LinkTokens, r.DevMessages);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Retention disabled (Retention:Enabled=false)")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retention deleted {Otp} OTP challenge(s), {Sessions} session(s), {Outbox} outbox message(s), {Tokens} link token(s), {Dev} dev message(s)")]
    private static partial void LogCompleted(ILogger logger, int otp, int sessions, int outbox, int tokens, int dev);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retention run failed ({ExceptionType}); retrying at the next run")]
    private static partial void LogFailed(ILogger logger, string exceptionType);
}
