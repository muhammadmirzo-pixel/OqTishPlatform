using OqTish.Domain.Common;

namespace OqTish.Domain.Staff;

/// <summary>A user's role in one clinic. <c>UNIQUE(clinic_id, user_id)</c>; <c>UNIQUE(clinic_id, id)</c> for composite FKs.</summary>
public sealed class ClinicMembership
{
    private ClinicMembership()
    {
    }

    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    public Guid UserId { get; private set; }

    public StaffRole Role { get; private set; }

    public MembershipScope Scope { get; private set; }

    public MembershipStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static ClinicMembership Create(Guid clinicId, Guid userId, StaffRole role, MembershipScope scope, DateTimeOffset now) => new()
    {
        Id = IdGenerator.New(),
        ClinicId = clinicId,
        UserId = userId,
        Role = role,
        Scope = scope,
        Status = MembershipStatus.Active,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void Change(StaffRole role, MembershipScope scope, DateTimeOffset now)
    {
        Role = role;
        Scope = scope;
        UpdatedAt = now;
    }

    public void SetStatus(MembershipStatus status, DateTimeOffset now)
    {
        Status = status;
        UpdatedAt = now;
    }
}
