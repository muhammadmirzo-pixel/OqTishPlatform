using System.Globalization;

namespace OqTish.Application.Notifications;

/// <summary>Configuration section <c>Notifications</c> (outbox dispatcher, docs/SERVICES.md §5).</summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>Poll interval of the dispatcher.</summary>
    public int DispatchIntervalSeconds { get; set; } = 2;

    /// <summary>Messages claimed per batch.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>Lease of a claimed (Processing) message; after it a crashed dispatcher's rows are reclaimed.</summary>
    public int LeaseSeconds { get; set; } = 300;

    /// <summary>Delivery attempts per message before it becomes Failed.</summary>
    public int MaxAttempts { get; set; } = 6;

    /// <summary>Delay after failed attempt 1, 2, … (comma-separated seconds; the last value repeats). Default 1 m, 5 m, 30 m, 2 h, 6 h.</summary>
    public string BackoffSeconds { get; set; } = "60,300,1800,7200,21600";

    /// <summary>± fraction of random jitter applied to every backoff delay.</summary>
    public decimal JitterRatio { get; set; } = 0.2m;

    /// <summary>Public base URL of the web app (e.g. <c>https://oqtish.uz</c>) for "open in console" buttons; HTTPS only, otherwise no button.</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    public IReadOnlyList<TimeSpan> BackoffSchedule()
    {
        var values = new List<TimeSpan>();
        foreach (var part in (BackoffSeconds ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
            {
                values.Add(TimeSpan.FromSeconds(seconds));
            }
        }

        return values.Count > 0 ? values : [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(6)];
    }
}

/// <summary>Configuration section <c>Retention</c> (nightly job, OD-18). Audit log and ledger are never touched.</summary>
public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    public bool Enabled { get; set; } = true;

    /// <summary>Local time (Asia/Tashkent) of the nightly run.</summary>
    public string RunAtLocalTime { get; set; } = "03:30";

    public int OtpChallengeHours { get; set; } = 24;

    /// <summary>Revoked / expired / idle-dead sessions are deleted this many days after they ended.</summary>
    public int SessionDays { get; set; } = 30;

    /// <summary>Sent, Skipped and Cancelled outbox rows (and their delivery attempts).</summary>
    public int OutboxDays { get; set; } = 30;

    /// <summary>Failed outbox rows are kept longer for investigation.</summary>
    public int FailedOutboxDays { get; set; } = 90;

    /// <summary>Telegram deep-link tokens are deleted this many hours after they expired (used or not).</summary>
    public int LinkTokenHours { get; set; } = 24;

    /// <summary>Development capture rows (<c>notify.dev_messages</c>).</summary>
    public int DevMessageDays { get; set; } = 7;
}
