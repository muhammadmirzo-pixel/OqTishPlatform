using System.Collections.Concurrent;
using OqTish.Application.Identity.Sms;

namespace OqTish.Infrastructure.Sms;

public sealed record FakeSmsMessage(string Phone, string Text, DateTimeOffset SentAt);

/// <summary>
/// In-memory mailbox of the <see cref="FakeSmsSender"/> (singleton). Read by <c>GET /api/dev/sms</c>
/// (Development only) and by integration tests. Keeps the last 50 messages per phone.
/// </summary>
public sealed class FakeSmsMailbox
{
    private const int MaxPerPhone = 50;
    private readonly ConcurrentDictionary<string, ConcurrentQueue<FakeSmsMessage>> _messages = new(StringComparer.Ordinal);

    public void Add(FakeSmsMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var queue = _messages.GetOrAdd(message.Phone, _ => new ConcurrentQueue<FakeSmsMessage>());
        queue.Enqueue(message);
        while (queue.Count > MaxPerPhone && queue.TryDequeue(out _))
        {
        }
    }

    /// <summary>Messages for an E.164 phone, newest first.</summary>
    public IReadOnlyList<FakeSmsMessage> For(string e164Phone) =>
        _messages.TryGetValue(e164Phone, out var queue) ? [.. queue.Reverse()] : [];

    /// <summary>When set, <see cref="FakeSmsSender"/> throws (to test failure handling).</summary>
    public bool FailSends { get; set; }
}

/// <summary>Development/test SMS sender: stores messages in <see cref="FakeSmsMailbox"/>; never logs them.</summary>
internal sealed class FakeSmsSender(FakeSmsMailbox mailbox, TimeProvider clock) : ISmsSender
{
    public Task SendAsync(string e164Phone, string text, CancellationToken cancellationToken)
    {
        if (mailbox.FailSends)
        {
            throw new SmsDeliveryException("Fake SMS failure (FailSends).");
        }

        mailbox.Add(new FakeSmsMessage(e164Phone, text, clock.GetUtcNow()));
        return Task.CompletedTask;
    }
}
