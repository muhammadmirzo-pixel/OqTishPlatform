using Microsoft.Extensions.Options;
using OqTish.Application.Identity.Handlers;
using OqTish.Application.Identity.Telegram;
using OqTish.Application.Notifications.Telegram;

namespace OqTish.Api.Common;

/// <summary>
/// <c>Telegram:Mode=Webhook</c>: registers <c>{WebhookBaseUrl}/api/telegram/webhook/{path}</c> with the secret token via
/// setWebhook in the background after startup (never blocks or fails startup; a failure is logged by code and can be
/// fixed by restarting). Does nothing in Polling mode or when Telegram is disabled.
/// </summary>
internal sealed partial class TelegramWebhookRegistrar(
    IOptions<TelegramOptions> options,
    ITelegramBotClient telegram,
    ILogger<TelegramWebhookRegistrar> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!TelegramConfigQuery.IsEnabled(o) || !o.IsWebhookMode)
        {
            return;
        }

        var url = $"{o.WebhookBaseUrl.TrimEnd('/')}/api/telegram/webhook/{o.WebhookPathSegment}";
        var result = await telegram.SetWebhookAsync(url, o.WebhookSecret, stoppingToken);
        if (result.IsSuccess)
        {
            LogRegistered(logger);
        }
        else
        {
            LogFailed(logger, result.Error.Code);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Telegram webhook registered")]
    private static partial void LogRegistered(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram setWebhook failed ({Code})")]
    private static partial void LogFailed(ILogger logger, string code);
}
