namespace OqTish.Domain.Identity;

/// <summary>
/// RFC 6238 authenticator secret. The secret is stored encrypted (ASP.NET Data Protection). It becomes
/// effective once <see cref="EnabledAt"/> is set. <see cref="LastUsedStep"/> prevents replaying a code.
/// </summary>
public sealed class TotpCredential
{
    private TotpCredential()
    {
        ProtectedSecret = string.Empty;
    }

    public Guid UserId { get; private set; }

    public string ProtectedSecret { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? EnabledAt { get; private set; }

    public long? LastUsedStep { get; private set; }

    /// <summary>Consecutive wrong codes across all sign-in paths (review R2). Updated atomically in SQL.</summary>
    public int FailedAttempts { get; private set; }

    /// <summary>While in the future, every code is refused without being checked.</summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    public bool IsEnabled => EnabledAt is not null;

    public static TotpCredential CreatePending(Guid userId, string protectedSecret, DateTimeOffset now) => new()
    {
        UserId = userId,
        ProtectedSecret = protectedSecret,
        CreatedAt = now,
    };

    public void ReplacePendingSecret(string protectedSecret, DateTimeOffset now)
    {
        if (IsEnabled)
        {
            throw new InvalidOperationException("An enabled TOTP secret cannot be replaced.");
        }

        ProtectedSecret = protectedSecret;
        CreatedAt = now;
        LastUsedStep = null;
    }

    public void Enable(long usedStep, DateTimeOffset now)
    {
        EnabledAt = now;
        LastUsedStep = usedStep;
    }
}
