using Microsoft.Extensions.Options;
using OqTish.Application.Identity.Handlers;
using OqTish.Application.Identity.Telegram;
using OqTish.Application.Notifications.Telegram;

namespace OqTish.Worker;

/// <summary>
/// Bot long polling (<c>Telegram:Mode=Polling</c>, the default for local work). Runs only when Telegram is enabled (flag,
/// token, username). On start it removes any webhook (getUpdates is refused while one is set), then loops
/// <see cref="TelegramPoller.PollOnceAsync"/>. Errors back off 5 s (409 conflict — another poller or a webhook — 30 s).
/// </summary>
internal sealed partial class TelegramPollingService(
    IServiceScopeFactory scopeFactory,
    IOptions<TelegramOptions> options,
    ITelegramBotClient telegram,
    ILogger<TelegramPollingService> logger,
    TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!TelegramConfigQuery.IsEnabled(o) || o.IsWebhookMode)
        {
            LogNotPolling(logger, TelegramConfigQuery.IsEnabled(o) ? "webhook mode" : "Telegram disabled");
            return;
        }

        var deleted = await telegram.DeleteWebhookAsync(stoppingToken);
        if (!deleted.IsSuccess)
        {
            LogWebhookDeleteFailed(logger, deleted.Error.Code);
        }

        LogStarted(logger);
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan? backoff = null;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<TelegramPoller>().PollOnceAsync(stoppingToken);
                if (!result.IsSuccess)
                {
                    LogPollFailed(logger, result.Error.Code);
                    backoff = result.Error.Code == "telegram.conflict" ? TimeSpan.FromSeconds(30) : result.Error.RetryAfter ?? TimeSpan.FromSeconds(5);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogPollFailed(logger, ex.GetType().Name);
                backoff = TimeSpan.FromSeconds(5);
            }

            if (backoff is { } delay)
            {
                await Task.Delay(delay, clock, stoppingToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Telegram polling not started ({Reason})")]
    private static partial void LogNotPolling(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Telegram long polling started")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram deleteWebhook failed ({Code})")]
    private static partial void LogWebhookDeleteFailed(ILogger logger, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram polling failed ({Code}); backing off")]
    private static partial void LogPollFailed(ILogger logger, string code);
}
