using System.Globalization;

namespace OqTish.Application.Identity.Telegram;

/// <summary>
/// Configuration section <c>Telegram</c>. <see cref="BotToken"/> and <see cref="WebhookSecret"/> are secrets
/// (env only). See docs/SERVICES.md §5 "Telegram botni ulash".
/// </summary>
public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    public bool Enabled { get; set; }

    public string BotToken { get; set; } = string.Empty;

    /// <summary>Bot username without '@', used for deep links and the Login Widget.</summary>
    public string BotUsername { get; set; } = string.Empty;

    /// <summary>Public HTTPS URL of the Mini App (e.g. <c>https://oqtish.uz/tg</c>); used for web_app buttons.</summary>
    public string MiniAppUrl { get; set; } = string.Empty;

    /// <summary>Maximum age of Mini App <c>initData</c> / widget <c>auth_date</c> (default 24 h).</summary>
    public int AuthMaxAgeSeconds { get; set; } = 86_400;

    /// <summary><see cref="TelegramModes.Polling"/> (default; the Worker long-polls getUpdates) or <see cref="TelegramModes.Webhook"/>.</summary>
    public string Mode { get; set; } = TelegramModes.Polling;

    /// <summary>
    /// <c>Real</c> (Bot API over HTTPS) or <c>Fake</c> (in-memory capture, Development/tests only — refused in
    /// Production). Empty = Fake in Development, Real elsewhere.
    /// </summary>
    public string Client { get; set; } = string.Empty;

    /// <summary>
    /// Webhook mode: the <c>secret_token</c> Telegram sends in <c>X-Telegram-Bot-Api-Secret-Token</c> (32–256 chars
    /// of A–Z, a–z, 0–9, '_' and '-'). The secret URL path segment is derived from it
    /// (<see cref="WebhookPathSegment"/>).
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Webhook mode: public HTTPS base URL of the API (e.g. <c>https://oqtish.uz</c>); the API calls setWebhook on startup.</summary>
    public string WebhookBaseUrl { get; set; } = string.Empty;

    /// <summary>Bot API base URL (tests may point it elsewhere).</summary>
    public string ApiBaseUrl { get; set; } = "https://api.telegram.org";

    /// <summary>Timeout of one Bot API call (sendMessage, setWebhook, getMe).</summary>
    public int RequestTimeoutSeconds { get; set; } = 15;

    /// <summary>Long-poll timeout of getUpdates.</summary>
    public int PollingTimeoutSeconds { get; set; } = 25;

    /// <summary>Incoming updates handled per chat per minute; the rest are ignored (flood protection).</summary>
    public int ChatRateLimitPerMinute { get; set; } = 20;

    /// <summary>A 429 with <c>retry_after</c> up to this many seconds is waited out and retried once inside the client.</summary>
    public int MaxInlineRetryAfterSeconds { get; set; } = 5;

    /// <summary>The numeric bot id (the part of the token before ':'), or null. Needed by the Login Widget popup (FRONTEND §9.2).</summary>
    public long? BotId =>
        BotToken.IndexOf(':', StringComparison.Ordinal) is var i and > 0
        && long.TryParse(BotToken.AsSpan(0, i), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;

    public bool IsWebhookMode => string.Equals(Mode, TelegramModes.Webhook, StringComparison.OrdinalIgnoreCase);

    /// <summary>Secret path segment of <c>POST /api/telegram/webhook/{segment}</c>: hex(SHA-256("oqtish-webhook-path:" + secret))[..32].</summary>
    public string WebhookPathSegment => string.IsNullOrEmpty(WebhookSecret)
        ? string.Empty
        : Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("oqtish-webhook-path:" + WebhookSecret)))[..32];
}

public static class TelegramModes
{
    public const string Polling = "Polling";
    public const string Webhook = "Webhook";
}

public static class TelegramClients
{
    public const string Real = "Real";
    public const string Fake = "Fake";
}
