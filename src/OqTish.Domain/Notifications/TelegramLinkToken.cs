using OqTish.Domain.Common;

namespace OqTish.Domain.Notifications;

/// <summary>
/// One-time token for the bot deep link <c>https://t.me/&lt;bot&gt;?start=&lt;token&gt;</c>. Only the
/// SHA-256 of the token is stored. Consumed by the bot (BE4) through
/// <c>TelegramLinkService.ConsumeTelegramLinkTokenAsync</c>.
/// </summary>
public sealed class TelegramLinkToken
{
    private TelegramLinkToken()
    {
        TokenHash = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public static TelegramLinkToken Create(Guid userId, string tokenHash, DateTimeOffset now, TimeSpan ttl) => new()
    {
        Id = IdGenerator.New(),
        UserId = userId,
        TokenHash = tokenHash,
        CreatedAt = now,
        ExpiresAt = now + ttl,
    };
}
