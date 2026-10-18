using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using OqTish.Application.Identity.Telegram;

namespace OqTish.Application.Notifications.Telegram;

/// <summary>
/// Per-chat flood protection for incoming bot updates (singleton, in-process): a fixed one-minute window of
/// <see cref="TelegramOptions.ChatRateLimitPerMinute"/> updates per chat; the rest are ignored without a reply.
/// </summary>
public sealed class TelegramChatRateLimiter(IOptions<TelegramOptions> options, TimeProvider clock)
{
    private const int CleanupThreshold = 10_000;
    private readonly ConcurrentDictionary<long, Window> _windows = new();

    public bool TryAcquire(long chatId)
    {
        var limit = Math.Max(1, options.Value.ChatRateLimitPerMinute);
        var now = clock.GetUtcNow();
        var minute = now.ToUnixTimeSeconds() / 60;
        if (_windows.Count > CleanupThreshold)
        {
            foreach (var (key, w) in _windows)
            {
                if (w.Minute < minute)
                {
                    _windows.TryRemove(key, out _);
                }
            }
        }

        var window = _windows.AddOrUpdate(chatId, _ => new Window(minute, 1), (_, w) => w.Minute == minute ? w with { Count = w.Count + 1 } : new Window(minute, 1));
        return window.Count <= limit;
    }

    private sealed record Window(long Minute, int Count);
}
