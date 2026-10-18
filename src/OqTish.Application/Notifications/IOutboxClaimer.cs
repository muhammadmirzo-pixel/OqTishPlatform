namespace OqTish.Application.Notifications;

/// <summary>A message claimed by one dispatcher run (status Processing, lease running).</summary>
public sealed record ClaimedOutboxMessage(Guid Id, Guid ClaimToken, int Attempt);

/// <summary>
/// Claims due outbox rows for delivery (Infrastructure: one statement
/// <c>UPDATE … FROM (SELECT … FOR UPDATE SKIP LOCKED LIMIT n)</c> with database time). Due = Pending with
/// <c>next_attempt_at ≤ now</c>, or Processing whose lease (<c>locked_until</c>) expired (a crashed dispatcher).
/// Claiming sets Processing, a new claim token, <c>locked_until = now + lease</c> and increments <c>attempts</c>.
/// Concurrent dispatchers never claim the same row.
/// </summary>
public interface IOutboxClaimer
{
    Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimDueAsync(int batchSize, TimeSpan lease, CancellationToken cancellationToken);
}

/// <summary>
/// Development-only capture of fake deliveries into <c>notify.dev_messages</c> (so the API's dev endpoint shows what
/// the Worker sent). No-op outside Development.
/// </summary>
public interface IDevMessageSink
{
    bool Enabled { get; }

    /// <summary>Application name of this process (rows captured by other processes are merged by /api/dev/sms).</summary>
    string Origin { get; }

    Task CaptureAsync(string channel, string recipient, string text, string? buttonsJson, CancellationToken cancellationToken);
}
