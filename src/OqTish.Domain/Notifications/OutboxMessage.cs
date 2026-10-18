using OqTish.Domain.Common;

namespace OqTish.Domain.Notifications;

public enum OutboxStatus
{
    Pending = 1,
    Processing = 2,
    Sent = 3,
    Failed = 4,
    Cancelled = 5,
}

/// <summary>
/// A message to be delivered after the business transaction commits (ADR-008). Written in the same
/// transaction as the change that caused it; <see cref="DedupKey"/> is UNIQUE so a replayed change
/// enqueues nothing new. Dispatching (claim with SKIP LOCKED, retries, backoff) is the Worker's job.
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

    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    /// <summary>Scrubbed error text of the last failed attempt.</summary>
    public string? LastError { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

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

    public void MarkSent(DateTimeOffset now)
    {
        Status = OutboxStatus.Sent;
        SentAt = now;
        Attempts++;
        LastError = null;
    }

    public void MarkAttemptFailed(string scrubbedError, DateTimeOffset nextAttemptAt, bool final)
    {
        Attempts++;
        LastError = scrubbedError.Length > 500 ? scrubbedError[..500] : scrubbedError;
        NextAttemptAt = nextAttemptAt;
        Status = final ? OutboxStatus.Failed : OutboxStatus.Pending;
    }

    public void Cancel() => Status = OutboxStatus.Cancelled;
}
