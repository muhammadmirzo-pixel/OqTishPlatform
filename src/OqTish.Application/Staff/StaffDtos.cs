using OqTish.Application.Common;
using OqTish.Domain.Staff;

namespace OqTish.Application.Staff;

public sealed record StaffMemberDto(
    Guid MembershipId,
    Guid UserId,
    string DisplayName,
    string? PhoneMasked,
    StaffRole Role,
    MembershipScope Scope,
    IReadOnlyList<Guid> BranchIds,
    MembershipStatus Status);

public sealed record StaffChangeCommand(StaffRole? Role, MembershipScope? Scope, IReadOnlyList<Guid>? BranchIds);

public sealed record InvitationCommand(string? Phone, StaffRole? Role, MembershipScope? Scope, IReadOnlyList<Guid>? BranchIds);

public sealed record InvitationDto(
    Guid Id,
    string PhoneMasked,
    StaffRole Role,
    MembershipScope Scope,
    IReadOnlyList<Guid> BranchIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

/// <summary>GET /api/me/invitations item.</summary>
public sealed record MyInvitationDto(
    Guid Id,
    Guid ClinicId,
    string ClinicName,
    StaffRole Role,
    MembershipScope Scope,
    IReadOnlyList<string> BranchNames,
    DateTimeOffset ExpiresAt);

/// <summary>Staff error codes (docs/API.md §9).</summary>
public static class StaffErrors
{
    public static readonly Error CannotGrantRole = Error.Forbidden("staff.cannot_grant_role", "The caller may not grant or change this role, scope or membership.");

    public static readonly Error LastOwner = Error.Rule("staff.last_owner", "The clinic must keep at least one active owner.");

    public static readonly Error AlreadyMember = Error.Conflict("staff.already_member", "This person is already a member of the clinic.");

    public static readonly Error InvitationExpired = Error.Rule("staff.invitation_expired", "The invitation has expired. Ask the clinic for a new one.");
}

/// <summary>Configuration section <c>Staff</c>.</summary>
public sealed class StaffOptions
{
    public const string SectionName = "Staff";

    public int InvitationTtlDays { get; set; } = 7;
}
