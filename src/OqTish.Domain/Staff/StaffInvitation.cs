using OqTish.Domain.Common;

namespace OqTish.Domain.Staff;

/// <summary>
/// Invitation addressed to a phone number (OD-21). Stores a keyed hash of the E.164 phone for matching
/// plus a masked form for display. Accepted by the invitee after signing in with that verified phone.
/// </summary>
public sealed class StaffInvitation
{
    private StaffInvitation()
    {
        PhoneHash = string.Empty;
        PhoneMasked = string.Empty;
        BranchIds = [];
    }

    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    public string PhoneHash { get; private set; }

    public string PhoneMasked { get; private set; }

    public StaffRole Role { get; private set; }

    public MembershipScope Scope { get; private set; }

    /// <summary>Branches granted on acceptance when <see cref="Scope"/> is Branches.</summary>
    public IReadOnlyList<Guid> BranchIds { get; private set; }

    public Guid InvitedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public Guid? AcceptedByUserId { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public static StaffInvitation Create(
        Guid clinicId,
        string phoneHash,
        string phoneMasked,
        StaffRole role,
        MembershipScope scope,
        IReadOnlyList<Guid> branchIds,
        Guid invitedByUserId,
        DateTimeOffset now,
        TimeSpan ttl) => new()
        {
            Id = IdGenerator.New(),
            ClinicId = clinicId,
            PhoneHash = phoneHash,
            PhoneMasked = phoneMasked,
            Role = role,
            Scope = scope,
            BranchIds = scope == MembershipScope.Branches ? [.. branchIds] : [],
            InvitedByUserId = invitedByUserId,
            CreatedAt = now,
            ExpiresAt = now + ttl,
        };

    public void MarkAccepted(Guid userId, DateTimeOffset now)
    {
        AcceptedAt = now;
        AcceptedByUserId = userId;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
