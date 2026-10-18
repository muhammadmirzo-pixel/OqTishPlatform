namespace OqTish.Application.Common;

/// <summary>
/// Enqueues a message into <c>notify.outbox_messages</c> inside the current transaction
/// (<c>INSERT … ON CONFLICT (dedup_key) DO NOTHING</c>). Must be called inside
/// <see cref="ITransactionRunner"/>; throws otherwise. Delivery is done by the Worker (BE4).
/// </summary>
public interface IOutbox
{
    /// <param name="type">Message type, e.g. <c>appointment.confirmed.patient</c>.</param>
    /// <param name="dedupKey">Globally unique key, e.g. <c>appointment:{id}:confirmed:patient</c>.</param>
    /// <param name="payload">Serialised as camelCase JSON. Ids and minimal display data only.</param>
    /// <param name="notBefore">Earliest delivery time (reminders); null = now.</param>
    /// <returns>True if inserted, false if a message with the same dedup key already exists.</returns>
    Task<bool> EnqueueAsync(string type, string dedupKey, object payload, DateTimeOffset? notBefore, CancellationToken cancellationToken);
}
