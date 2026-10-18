namespace OqTish.Application.Identity.Telegram;

/// <summary>Configuration section <c>Telegram</c>. <see cref="BotToken"/> is a secret (env only).</summary>
public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    public bool Enabled { get; set; }

    public string BotToken { get; set; } = string.Empty;

    /// <summary>Bot username without '@', used for deep links and the Login Widget.</summary>
    public string BotUsername { get; set; } = string.Empty;

    public string MiniAppUrl { get; set; } = string.Empty;

    /// <summary>Maximum age of Mini App <c>initData</c> / widget <c>auth_date</c> (default 24 h).</summary>
    public int AuthMaxAgeSeconds { get; set; } = 86_400;
}
