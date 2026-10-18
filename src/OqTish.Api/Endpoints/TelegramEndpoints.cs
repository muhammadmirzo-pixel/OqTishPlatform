using System.Text.Json;
using Microsoft.Extensions.Options;
using OqTish.Api.Common;
using OqTish.Application.Common;
using OqTish.Application.Common.Security;
using OqTish.Application.Identity;
using OqTish.Application.Identity.Handlers;
using OqTish.Application.Identity.Telegram;
using OqTish.Application.Notifications.Telegram;
using OqTish.Infrastructure.Telegram;

namespace OqTish.Api.Endpoints;

/// <summary>/api/telegram — public Telegram configuration and the bot webhook (docs/API.md §6).</summary>
public static partial class TelegramEndpoints
{
    public const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";

    public static readonly Error WebhookUnauthorized = Error.Unauthorized("telegram.webhook_unauthorized", "Missing or wrong webhook secret token.");

    public static IEndpointRouteBuilder MapTelegramEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/telegram/config", (TelegramConfigQuery query) => TypedResults.Ok(query.Handle()))
            .AllowAnonymous()
            .WithTags("Telegram")
            .WithName("GetTelegramConfig").WithSummary("{ enabled, botUsername, miniAppUrl, botId } for the UI")
            .Produces<TelegramConfigDto>();

        // Webhook mode only. Path = hex(SHA-256("oqtish-webhook-path:" + secret))[..32]; wrong path, Polling mode or
        // Telegram disabled → 404; right path with a missing/wrong X-Telegram-Bot-Api-Secret-Token → 401. Exempt from the
        // CSRF header (ClientHeaderMiddleware). Always 200 once authenticated so Telegram does not redeliver a bad update.
        app.MapPost("/api/telegram/webhook/{secretPath}", async (string secretPath, HttpContext http, IOptions<TelegramOptions> options,
                TelegramBotService bot, ILoggerFactory loggers, CancellationToken ct) =>
            {
                var o = options.Value;
                if (!TelegramConfigQuery.IsEnabled(o) || !o.IsWebhookMode || string.IsNullOrEmpty(o.WebhookSecret)
                    || !SecureTokens.FixedTimeEquals(secretPath ?? string.Empty, o.WebhookPathSegment))
                {
                    return ProblemResults.ToResult(CommonErrors.NotFound, http);
                }

                if (!SecureTokens.FixedTimeEquals(http.Request.Headers[SecretHeader].ToString(), o.WebhookSecret))
                {
                    return ProblemResults.ToResult(WebhookUnauthorized, http);
                }

                var logger = loggers.CreateLogger("OqTish.Api.TelegramWebhook");
                try
                {
                    using var doc = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: ct);
                    if (TelegramJson.ParseUpdate(doc.RootElement) is { } update)
                    {
                        await bot.HandleAsync(update, ct);
                    }
                }
                catch (JsonException)
                {
                    LogBadUpdate(logger, nameof(JsonException));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogBadUpdate(logger, ex.GetType().Name);
                }

                return Results.Ok();
            })
            .AllowAnonymous()
            .WithTags("Telegram")
            .WithName("TelegramWebhook").WithSummary("Bot updates from Telegram (Telegram:Mode=Webhook; secret path + X-Telegram-Bot-Api-Secret-Token)")
            .Produces(StatusCodes.Status200OK).ProducesProblem(401).ProducesProblem(404);
        return app;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram webhook update not handled ({ExceptionType})")]
    private static partial void LogBadUpdate(ILogger logger, string exceptionType);
}
