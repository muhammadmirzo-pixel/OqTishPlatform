using OqTish.Domain.Common;

namespace OqTish.Domain.Staff;

/// <summary>An explicit platform permission granted to a user (no implicit tenant data access).</summary>
public sealed class PlatformAdminGrant
{
    private PlatformAdminGrant()
    {
        Permission = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Permission { get; private set; }

    public Guid? GrantedByUserId { get; private set; }

    public DateTimeOffset GrantedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? RevokedByUserId { get; private set; }

    public static PlatformAdminGrant Create(Guid userId, string permission, Guid? grantedByUserId, DateTimeOffset now) => new()
    {
        Id = IdGenerator.New(),
        UserId = userId,
        Permission = permission,
        GrantedByUserId = grantedByUserId,
        GrantedAt = now,
    };

    public void Revoke(Guid? byUserId, DateTimeOffset now)
    {
        RevokedAt ??= now;
        RevokedByUserId ??= byUserId;
    }
}
