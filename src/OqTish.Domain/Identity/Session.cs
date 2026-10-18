using OqTish.Domain.Common;

namespace OqTish.Domain.Identity;

/// <summary>
/// A server-side sign-in session (ADR-003). The cookie carries a random token; only its SHA-256 is
/// stored. A session is valid while not revoked, not past <see cref="ExpiresAt"/> (30-day sliding),
/// not idle for longer than the idle timeout (14 days), its security stamp still matches the user's
/// and the user is Active. It is validated against the database on every request.
/// </summary>
public sealed class Session
{
    private Session()
    {
        TokenHash = string.Empty;
        SecurityStamp = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; }

    public string SecurityStamp { get; private set; }

    /// <summary>How the session was established (Otp, Password, TelegramWebApp, TelegramWidget).</summary>
    public string AuthMethod { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevokedReason { get; private set; }

    public string? IpHash { get; private set; }

    public string? UserAgent { get; private set; }

    public static Session Create(
        Guid userId,
        string tokenHash,
        string securityStamp,
        string authMethod,
        DateTimeOffset now,
        TimeSpan lifetime,
        string? ipHash,
        string? userAgent) => new()
        {
            Id = IdGenerator.New(),
            UserId = userId,
            TokenHash = tokenHash,
            SecurityStamp = securityStamp,
            AuthMethod = authMethod,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + lifetime,
            IpHash = ipHash,
            UserAgent = userAgent is { Length: > 256 } ? userAgent[..256] : userAgent,
        };

    public bool IsActiveAt(DateTimeOffset now, TimeSpan idleTimeout) =>
        RevokedAt is null && ExpiresAt > now && LastSeenAt + idleTimeout > now;

    public void Revoke(string reason, DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevokedReason = reason;
    }

    public void UpdateSecurityStamp(string stamp) => SecurityStamp = stamp;
}
