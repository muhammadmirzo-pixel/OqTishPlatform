namespace OqTish.Domain.Notifications;

/// <summary>
/// The private chat in which the bot may message a linked user (one per user). Inactive when the user blocked
/// the bot (Bot API 403) or sent <c>/stop</c>; <c>/start</c> or a new deep link activates it again.
/// <see cref="LanguageCode"/> (uz/ru/en, from Telegram's <c>language_code</c>) selects the message language.
/// </summary>
public sealed class TelegramChatLink
{
    public const string DefaultLanguage = "uz";

    private TelegramChatLink()
    {
        LanguageCode = DefaultLanguage;
    }

    public Guid UserId { get; private set; }

    public long TelegramUserId { get; private set; }

    public long ChatId { get; private set; }

    public DateTimeOffset LinkedAt { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary><c>uz</c>, <c>ru</c> or <c>en</c>.</summary>
    public string LanguageCode { get; private set; }

    public DateTimeOffset? DeactivatedAt { get; private set; }

    /// <summary><c>blocked</c> (Bot API 403 / chat not found) or <c>stopped</c> (/stop).</summary>
    public string? DeactivationReason { get; private set; }

    public static TelegramChatLink Create(Guid userId, long telegramUserId, long chatId, DateTimeOffset now, string? languageCode = null) => new()
    {
        UserId = userId,
        TelegramUserId = telegramUserId,
        ChatId = chatId,
        LinkedAt = now,
        IsActive = true,
        LanguageCode = NormalizeLanguage(languageCode),
    };

    public void Relink(long telegramUserId, long chatId, DateTimeOffset now)
    {
        TelegramUserId = telegramUserId;
        ChatId = chatId;
        LinkedAt = now;
        IsActive = true;
        DeactivatedAt = null;
        DeactivationReason = null;
    }

    public void SetLanguage(string? languageCode) => LanguageCode = NormalizeLanguage(languageCode);

    public void Deactivate(string reason, DateTimeOffset now)
    {
        IsActive = false;
        DeactivatedAt = now;
        DeactivationReason = reason;
    }

    /// <summary>Telegram <c>language_code</c> (IETF tag such as <c>ru</c>, <c>en-US</c>) → uz/ru/en; anything else → uz.</summary>
    public static string NormalizeLanguage(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return DefaultLanguage;
        }

        var primary = languageCode.Split('-', '_')[0].Trim().ToUpperInvariant();
        return primary switch
        {
            "RU" => "ru",
            "EN" => "en",
            _ => DefaultLanguage,
        };
    }
}
