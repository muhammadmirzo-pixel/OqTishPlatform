using System.Collections.Concurrent;
using OqTish.Application.Notifications;
using OqTish.Application.Notifications.Telegram;

namespace OqTish.Infrastructure.Telegram;

public sealed record FakeTelegramMessage(long MessageId, long ChatId, string Text, IReadOnlyList<IReadOnlyList<TelegramButton>>? Buttons, DateTimeOffset SentAt);

/// <summary>
/// In-memory state of the <see cref="FakeTelegramBotClient"/> (singleton): messages "sent" (last 500), updates queued
/// for getUpdates, the registered webhook, and a failure hook for tests (<see cref="FailNext"/>, <see cref="SendBehavior"/>).
/// </summary>
public sealed class FakeTelegramMailbox
{
    private const int MaxMessages = 500;
    private readonly ConcurrentQueue<FakeTelegramMessage> _sent = new();
    private readonly ConcurrentQueue<TelegramUpdate> _updates = new();
    private readonly ConcurrentQueue<TelegramError> _failures = new();
    private long _messageId;

    public IReadOnlyList<FakeTelegramMessage> Sent => [.. _sent.Reverse()];

    /// <summary>Messages to one chat, newest first.</summary>
    public IReadOnlyList<FakeTelegramMessage> For(long chatId) => [.. _sent.Reverse().Where(m => m.ChatId == chatId)];

    /// <summary>Optional per-message hook: return an error to make that send fail.</summary>
    public Func<TelegramOutgoingMessage, TelegramError?>? SendBehavior { get; set; }

    public string? WebhookUrl { get; internal set; }

    public string? WebhookSecret { get; internal set; }

    public int SendCalls => Volatile.Read(ref _sendCalls);

    private int _sendCalls;

    /// <summary>The next <paramref name="times"/> sends fail with <paramref name="error"/>.</summary>
    public void FailNext(TelegramError error, int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            _failures.Enqueue(error);
        }
    }

    public void EnqueueUpdate(TelegramUpdate update) => _updates.Enqueue(update);

    public void Clear()
    {
        _sent.Clear();
        _updates.Clear();
        _failures.Clear();
        SendBehavior = null;
    }

    internal TelegramError? NextFailure(TelegramOutgoingMessage message)
    {
        Interlocked.Increment(ref _sendCalls);
        return _failures.TryDequeue(out var queued) ? queued : SendBehavior?.Invoke(message);
    }

    internal FakeTelegramMessage Record(TelegramOutgoingMessage message, DateTimeOffset now)
    {
        var record = new FakeTelegramMessage(Interlocked.Increment(ref _messageId), message.ChatId, message.Text, message.Buttons, now);
        _sent.Enqueue(record);
        while (_sent.Count > MaxMessages && _sent.TryDequeue(out _))
        {
        }

        return record;
    }

    internal IReadOnlyList<TelegramUpdate> TakeUpdates(long offset)
    {
        var taken = new List<TelegramUpdate>();
        while (_updates.TryDequeue(out var update))
        {
            if (update.UpdateId >= offset)
            {
                taken.Add(update);
            }
        }

        return taken;
    }
}

/// <summary>
/// Development/test Telegram client: never calls Telegram. Sends are recorded in <see cref="FakeTelegramMailbox"/> and,
/// in Development, in <c>notify.dev_messages</c> (shown by <c>GET /api/dev/telegram</c>). Refused in Production.
/// </summary>
internal sealed class FakeTelegramBotClient(FakeTelegramMailbox mailbox, IDevMessageSink sink, TimeProvider clock) : ITelegramBotClient
{
    public async Task<TelegramResult<long>> SendMessageAsync(TelegramOutgoingMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (mailbox.NextFailure(message) is { } error)
        {
            return TelegramResult.Fail<long>(error);
        }

        var record = mailbox.Record(message, clock.GetUtcNow());
        var buttons = message.Buttons is { Count: > 0 } rows ? TelegramJson.Keyboard(rows).ToJsonString() : null;
        await sink.CaptureAsync("Telegram", message.ChatId.ToString(System.Globalization.CultureInfo.InvariantCulture), message.Text, buttons, cancellationToken);
        return TelegramResult.Ok(record.MessageId);
    }

    public async Task<TelegramResult<IReadOnlyList<TelegramUpdate>>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var updates = mailbox.TakeUpdates(offset);
        if (updates.Count == 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 0, 1)), clock, cancellationToken);
        }

        return TelegramResult.Ok(updates);
    }

    public Task<TelegramResult<bool>> SetWebhookAsync(string url, string secretToken, CancellationToken cancellationToken)
    {
        mailbox.WebhookUrl = url;
        mailbox.WebhookSecret = secretToken;
        return Task.FromResult(TelegramResult.Ok(true));
    }

    public Task<TelegramResult<bool>> DeleteWebhookAsync(CancellationToken cancellationToken)
    {
        mailbox.WebhookUrl = null;
        mailbox.WebhookSecret = null;
        return Task.FromResult(TelegramResult.Ok(true));
    }

    public Task<TelegramResult<TelegramBotInfo>> GetMeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(TelegramResult.Ok(new TelegramBotInfo(1, "oqtish_fake_bot")));
}
