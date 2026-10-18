namespace OqTish.Application.Notifications;

/// <summary>
/// Bounded exponential backoff with jitter for outbox delivery. Attempt <c>n</c> (1-based) failed → wait
/// <c>schedule[n − 1]</c> (the last entry repeats) × (1 ± jitter); attempt <see cref="NotificationOptions.MaxAttempts"/>
/// failed → Failed. A 429 <c>retry_after</c> longer than the backoff wins.
/// </summary>
public static class RetryPolicy
{
    public static bool IsFinal(int attempt, int maxAttempts) => attempt >= Math.Max(1, maxAttempts);

    public static TimeSpan NextDelay(int attempt, IReadOnlyList<TimeSpan> schedule, decimal jitterRatio, decimal random01, TimeSpan? retryAfter = null)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (schedule.Count == 0)
        {
            throw new ArgumentException("The backoff schedule is empty.", nameof(schedule));
        }

        var baseDelay = schedule[Math.Clamp(attempt - 1, 0, schedule.Count - 1)];
        var jitter = Math.Clamp(jitterRatio, 0m, 0.9m);
        var factor = 1m + (((Math.Clamp(random01, 0m, 1m) * 2m) - 1m) * jitter);
        var delay = TimeSpan.FromTicks((long)(baseDelay.Ticks * factor));
        return retryAfter is { } ra && ra > delay ? ra : delay;
    }
}
