using OqTish.Domain.Common;

namespace OqTish.Domain.Identity;

public enum UserStatus
{
    Active = 1,
    Suspended = 2,
    Deleted = 3,
}

/// <summary>
/// An account. Credentials and identifiers live in <see cref="AuthIdentity"/>; the account itself only
/// carries status and a security stamp that rotates when credentials change.
/// </summary>
public sealed class User
{
    private User()
    {
        SecurityStamp = string.Empty;
    }

    public Guid Id { get; private set; }

    public string? DisplayName { get; private set; }

    public UserStatus Status { get; private set; }

    /// <summary>Random value copied into each session; rotating it invalidates every older session.</summary>
    public string SecurityStamp { get; private set; }

    public string? StatusReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsActive => Status == UserStatus.Active;

    public static User Create(string? displayName, DateTimeOffset now) => new()
    {
        Id = IdGenerator.New(),
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
        Status = UserStatus.Active,
        SecurityStamp = NewStamp(),
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void Rename(string? displayName, DateTimeOffset now)
    {
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        UpdatedAt = now;
    }

    public void RotateSecurityStamp(DateTimeOffset now)
    {
        SecurityStamp = NewStamp();
        UpdatedAt = now;
    }

    public void Suspend(string reason, DateTimeOffset now)
    {
        Status = UserStatus.Suspended;
        StatusReason = reason;
        RotateSecurityStamp(now);
    }

    public void Reinstate(DateTimeOffset now)
    {
        Status = UserStatus.Active;
        StatusReason = null;
        UpdatedAt = now;
    }

    /// <summary>Self-deletion = anonymisation (OD-18): the row stays for referential integrity.</summary>
    public void MarkDeleted(DateTimeOffset now)
    {
        Status = UserStatus.Deleted;
        DisplayName = null;
        StatusReason = null;
        DeletedAt = now;
        RotateSecurityStamp(now);
    }

    private static string NewStamp() => Guid.NewGuid().ToString("N");
}
