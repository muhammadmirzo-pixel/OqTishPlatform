using OqTish.Domain.Common;

namespace OqTish.Domain.Notifications;

/// <summary>
/// Lifecycle of an outbox message (BE4 dispatcher, docs/SERVICES.md §5):
/// Pending → Processing (claimed with a lease) → Sent | Skipped | Pending (retry with backoff) | Failed (max
/// attempts / permanent error) | Cancelled (stale reminder, cancelled by Booking). A Processing row whose lease
/// expired (worker crash) is claimed again. An operator can move Failed back to Pending.
/// </summary>
public enum OutboxStatus
{
    Pending = 1,
    Processing = 2,
    Sent = 3,
    Failed = 4,
    Cancelled = 5,

    /// <summary>Nothing to deliver: no recipient or no channel (e.g. no Telegram link and no SMS fallback for the type).</summary>
    Skipped = 6,
}

/// <summary>
/// A message to be delivered after the business transaction commits (ADR-008). Written in the same
/// transaction as the change that caused it; <see cref="DedupKey"/> is UNIQUE so a replayed change
/// enqueues nothing new. Dispatching (claim with SKIP LOCKED + lease, retries, backoff) is done by the Worker's
/// <c>OutboxDispatcher</c>, which updates the row with conditional statements on <see cref="ClaimToken"/>.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
        Type = string.Empty;
        DedupKey = string.Empty;
        Payload = "{}";
    }

    public Guid Id { get; private set; }

    /// <summary>Message type, e.g. <c>appointment.confirmed.patient</c>.</summary>
    public string Type { get; private set; }

    public string DedupKey { get; private set; }

    /// <summary>JSON (jsonb): ids plus minimal display data. Never phones or tokens.</summary>
    public string Payload { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset NotBefore { get; private set; }

    public OutboxStatus Status { get; private set; }

    /// <summary>Number of dispatch claims so far (incremented when a dispatcher claims the row).</summary>
    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    /// <summary>Scrubbed error text of the last failed attempt (codes and provider status only, never message text or phones).</summary>
    public string? LastError { get; private set; }

    /// <summary>Stable code of the last problem, e.g. <c>telegram.http_500</c>, <c>notify.no_channel</c>, <c>reminder.stale</c>.</summary>
    public string? LastErrorCode { get; private set; }

    /// <summary>Channel(s) used by the last attempt (<c>Telegram</c>, <c>Sms</c>, <c>Telegram,Sms</c>).</summary>
    public string? Channel { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>Token of the dispatcher run that holds the row while <see cref="OutboxStatus.Processing"/>.</summary>
    public Guid? ClaimToken { get; private set; }

    /// <summary>Lease end while Processing; after it another dispatcher may reclaim the row (crash recovery).</summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    public static OutboxMessage Create(string type, string dedupKey, string payloadJson, DateTimeOffset now, DateTimeOffset? notBefore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(dedupKey);
        var due = notBefore ?? now;
        return new OutboxMessage
        {
            Id = IdGenerator.New(),
            Type = type,
            DedupKey = dedupKey,
            Payload = payloadJson,
            CreatedAt = now,
            NotBefore = due,
            Status = OutboxStatus.Pending,
            NextAttemptAt = due,
        };
    }

    /// <summary>Operator retry (admin): Failed → Pending with a fresh attempt budget, due now.</summary>
    public bool RetryManually(DateTimeOffset now)
    {
        if (Status != OutboxStatus.Failed)
        {
            return false;
        }

        Status = OutboxStatus.Pending;
        Attempts = 0;
        NextAttemptAt = now;
        ClaimToken = null;
        LockedUntil = null;
        return true;
    }

    public void Cancel() => Status = OutboxStatus.Cancelled;
}
