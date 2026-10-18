namespace OqTish.Domain.Identity;

/// <summary>Password hash (ASP.NET Core Identity PasswordHasher format) plus lockout counters.</summary>
public sealed class PasswordCredential
{
    private PasswordCredential()
    {
        PasswordHash = string.Empty;
    }

    public Guid UserId { get; private set; }

    public string PasswordHash { get; private set; }

    public int FailedAttempts { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static PasswordCredential Create(Guid userId, string passwordHash, DateTimeOffset now) => new()
    {
        UserId = userId,
        PasswordHash = passwordHash,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public bool IsLockedOut(DateTimeOffset now) => LockedUntil is { } until && until > now;

    public void SetHash(string passwordHash, DateTimeOffset now)
    {
        PasswordHash = passwordHash;
        FailedAttempts = 0;
        LockedUntil = null;
        UpdatedAt = now;
    }

    public void RegisterFailure(DateTimeOffset now, int maxFailures, TimeSpan lockout)
    {
        FailedAttempts++;
        if (FailedAttempts >= maxFailures)
        {
            LockedUntil = now + lockout;
            FailedAttempts = 0;
        }

        UpdatedAt = now;
    }

    public void RegisterSuccess(DateTimeOffset now)
    {
        if (FailedAttempts == 0 && LockedUntil is null)
        {
            return;
        }

        FailedAttempts = 0;
        LockedUntil = null;
        UpdatedAt = now;
    }
}
