namespace OqTish.Application.Notifications.Telegram;

/// <summary>
/// Telegram Bot API port. Implementations: <c>TelegramBotApiClient</c> (HTTPS, Infrastructure) and
/// <c>FakeTelegramBotClient</c> (in-memory, Development/tests). Calls never throw for API or network errors; they
/// return a <see cref="TelegramError"/> classified for the dispatcher. Implementations must never log the bot
/// token, the request URL (it contains the token) or message text.
/// </summary>
public interface ITelegramBotClient
{
    Task<TelegramResult<long>> SendMessageAsync(TelegramOutgoingMessage message, CancellationToken cancellationToken);

    /// <summary>Long polling. <paramref name="offset"/> = last handled update_id + 1.</summary>
    Task<TelegramResult<IReadOnlyList<TelegramUpdate>>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken cancellationToken);

    Task<TelegramResult<bool>> SetWebhookAsync(string url, string secretToken, CancellationToken cancellationToken);

    Task<TelegramResult<bool>> DeleteWebhookAsync(CancellationToken cancellationToken);

    Task<TelegramResult<TelegramBotInfo>> GetMeAsync(CancellationToken cancellationToken);
}

/// <summary>An inline keyboard button: exactly one of <see cref="Url"/> or <see cref="WebAppUrl"/>.</summary>
public sealed record TelegramButton(string Text, string? Url = null, string? WebAppUrl = null);

/// <summary>Plain-text message (no parse mode, so user-provided names need no escaping) with an optional inline keyboard.</summary>
public sealed record TelegramOutgoingMessage(long ChatId, string Text, IReadOnlyList<IReadOnlyList<TelegramButton>>? Buttons = null);

/// <summary>The subset of a Bot API <c>Update</c> the bot uses (private text messages).</summary>
public sealed record TelegramUpdate(long UpdateId, TelegramIncomingMessage? Message);

public sealed record TelegramIncomingMessage(long MessageId, long ChatId, string ChatType, long? FromId, bool FromIsBot, string? LanguageCode, string? Text);

public sealed record TelegramBotInfo(long Id, string? Username);

public enum TelegramFailureKind
{
    /// <summary>Timeout, network error, 5xx, 401/404 (configuration) or 409 — retry later.</summary>
    Transient = 1,

    /// <summary>429 Too Many Requests — retry after <see cref="TelegramError.RetryAfter"/>.</summary>
    RateLimited = 2,

    /// <summary>403 (bot blocked, user deactivated) or 400 "chat not found" — the chat link must be deactivated.</summary>
    ChatUnavailable = 3,

    /// <summary>Other 400 — the request itself is wrong; retrying cannot help.</summary>
    Permanent = 4,
}

/// <summary>
/// A classified Bot API failure. <see cref="Code"/> is stable (<c>telegram.timeout</c>, <c>telegram.http_500</c>,
/// <c>telegram.blocked</c> …); <see cref="ProviderStatus"/> is the HTTP status or <c>timeout</c>/<c>network</c>;
/// <see cref="Description"/> is Telegram's description (scrubbed, ≤ 200 chars) — never message text.
/// </summary>
public sealed record TelegramError(TelegramFailureKind Kind, string Code, string ProviderStatus, string? Description, TimeSpan? RetryAfter = null);

public sealed record TelegramResult<T>(T? Value, TelegramError? Error)
{
    [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;
}

public static class TelegramResult
{
    public static TelegramResult<T> Ok<T>(T value) => new(value, null);

    public static TelegramResult<T> Fail<T>(TelegramError error) => new(default, error);
}
