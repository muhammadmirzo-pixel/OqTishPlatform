namespace OqTish.Domain.Notifications;

/// <summary>The private chat in which the bot may message a linked user (one per user).</summary>
public sealed class TelegramChatLink
{
    private TelegramChatLink()
    {
    }

    public Guid UserId { get; private set; }

    public long TelegramUserId { get; private set; }

    public long ChatId { get; private set; }

    public DateTimeOffset LinkedAt { get; private set; }

    public static TelegramChatLink Create(Guid userId, long telegramUserId, long chatId, DateTimeOffset now) => new()
    {
        UserId = userId,
        TelegramUserId = telegramUserId,
        ChatId = chatId,
        LinkedAt = now,
    };

    public void Relink(long telegramUserId, long chatId, DateTimeOffset now)
    {
        TelegramUserId = telegramUserId;
        ChatId = chatId;
        LinkedAt = now;
    }
}
