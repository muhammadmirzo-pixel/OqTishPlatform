using OqTish.Domain.Common;

namespace OqTish.Domain.Notifications;

public enum DeliveryChannel
{
    None = 0,
    Telegram = 1,
    Sms = 2,
}

public enum DeliveryOutcome
{
    /// <summary>The provider accepted the message.</summary>
    Sent = 1,

    /// <summary>Transient failure (timeout, 5xx, 429, SMS gateway error): the message is retried with backoff.</summary>
    Retry = 2,

    /// <summary>Permanent failure for this recipient (e.g. Bot API 400): not retried.</summary>
    Rejected = 3,

    /// <summary>No usable channel for this recipient (no active Telegram link and no SMS fallback).</summary>
    Skipped = 4,
}

/// <summary>
/// One delivery try of an outbox message to one recipient (<c>notify.delivery_attempts</c>). Written right after
/// the provider call, so a retry of a multi-recipient message never re-sends to a recipient that already has a
/// final outcome (per-recipient idempotency; a crash between the provider call and this row can still duplicate —
/// delivery is at-least-once). Never contains message text, phones or chat ids: <see cref="RecipientKey"/> is
/// <c>user:{id}</c> or <c>contact</c> (walk-in contact phone).
/// </summary>
public sealed class DeliveryAttempt
{
    public const int MaxErrorLength = 300;

    private DeliveryAttempt()
    {
        RecipientKey = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid OutboxMessageId { get; private set; }

    /// <summary>The message's attempt number (claim count) this try belongs to.</summary>
    public int AttemptNumber { get; private set; }

    public string RecipientKey { get; private set; }

    public DeliveryChannel Channel { get; private set; }

    public DeliveryOutcome Outcome { get; private set; }

    /// <summary>Provider status, e.g. HTTP <c>200</c>/<c>403</c>/<c>429</c>, <c>timeout</c>, <c>error</c>.</summary>
    public string? ProviderStatus { get; private set; }

    public string? ErrorCode { get; private set; }

    /// <summary>Scrubbed provider description (no message text, phone, token).</summary>
    public string? Error { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static DeliveryAttempt Create(
        Guid outboxMessageId, int attemptNumber, string recipientKey, DeliveryChannel channel, DeliveryOutcome outcome,
        string? providerStatus, string? errorCode, string? error, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientKey);
        return new DeliveryAttempt
        {
            Id = IdGenerator.New(),
            OutboxMessageId = outboxMessageId,
            AttemptNumber = attemptNumber,
            RecipientKey = recipientKey,
            Channel = channel,
            Outcome = outcome,
            ProviderStatus = Truncate(providerStatus, 32),
            ErrorCode = Truncate(errorCode, 64),
            Error = Truncate(error, MaxErrorLength),
            CreatedAt = now,
        };
    }

    private static string? Truncate(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}

/// <summary>Long-polling state of the bot (<c>notify.telegram_bot_state</c>): next <c>getUpdates</c> offset per bot id.</summary>
public sealed class TelegramBotState
{
    private TelegramBotState()
    {
    }

    public long BotId { get; private set; }

    public long UpdateOffset { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static TelegramBotState Create(long botId, long offset, DateTimeOffset now) => new() { BotId = botId, UpdateOffset = offset, UpdatedAt = now };

    public void Advance(long offset, DateTimeOffset now)
    {
        if (offset > UpdateOffset)
        {
            UpdateOffset = offset;
            UpdatedAt = now;
        }
    }
}

/// <summary>
/// Development-only capture of what the Fake Telegram client / Fake SMS sender would have sent
/// (<c>notify.dev_messages</c>), so <c>GET /api/dev/telegram</c> in the API also shows messages sent by the Worker
/// process. Only written when the host environment is Development (Fake senders are refused in Production).
/// </summary>
public sealed class DevCapturedMessage
{
    private DevCapturedMessage()
    {
        Channel = string.Empty;
        Recipient = string.Empty;
        Text = string.Empty;
        Origin = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary><c>Telegram</c> or <c>Sms</c>.</summary>
    public string Channel { get; private set; }

    /// <summary>Chat id or E.164 phone.</summary>
    public string Recipient { get; private set; }

    public string Text { get; private set; }

    /// <summary>Inline keyboard as JSON, or null.</summary>
    public string? Buttons { get; private set; }

    /// <summary>Application name of the process that captured it (API / Worker).</summary>
    public string Origin { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static DevCapturedMessage Create(string channel, string recipient, string text, string? buttons, string origin, DateTimeOffset now) => new()
    {
        Id = IdGenerator.New(),
        Channel = channel,
        Recipient = recipient,
        Text = text,
        Buttons = buttons,
        Origin = origin,
        CreatedAt = now,
    };
}
