using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Identity;
using OqTish.Domain.Identity;
using OqTish.Domain.Staff;

namespace OqTish.Application.Staff;

/// <summary>
/// Anti-escalation rules for <c>staff.manage</c> (SECURITY.md §8, DECISIONS.md BE1b-3):
/// <list type="number">
/// <item>Roles: <see cref="Permissions.RolesManageableBy"/> — owners manage every role, clinic admins all
/// except ClinicOwner/ClinicAdmin. The rule applies both to the role a membership has and the one it gets.</item>
/// <item>Scope: a Branches-scoped manager only sees and manages Branches-scoped memberships/invitations
/// whose branches are all within its own, and can only grant Branches scope within its own branches.</item>
/// <item>Nobody changes their own membership (no self-escalation); leaving (DELETE self) is allowed.</item>
/// </list>
/// </summary>
internal static class StaffRules
{
    public static bool CanManageRole(MembershipAccess actor, StaffRole role) =>
        Permissions.RolesManageableBy(actor.Role).Contains(role);

    /// <summary>Whether the actor's branch scope covers a membership/invitation with this scope and branches.</summary>
    public static bool Covers(MembershipAccess actor, MembershipScope scope, IEnumerable<Guid> branchIds) =>
        actor.Scope == MembershipScope.Clinic
        || (scope == MembershipScope.Branches && branchIds.All(actor.GrantedBranchIds.Contains));

    /// <summary>Validates scope + branches of a grant: Branches needs ≥ 1 branch of this clinic; Clinic takes none.</summary>
    public static async Task<Result<(MembershipScope Scope, IReadOnlyList<Guid> BranchIds)>> ValidateScopeAsync(
        IOqTishDb db, Guid clinicId, MembershipScope? scope, IReadOnlyList<Guid>? branchIds, CancellationToken ct)
    {
        if (scope is not { } s || !Enum.IsDefined(s))
        {
            return Error.InvalidField("scope", "Required (Clinic or Branches).");
        }

        if (s == MembershipScope.Clinic)
        {
            return Ok(s, []);
        }

        var ids = (branchIds ?? []).Distinct().ToList();
        if (ids.Count == 0)
        {
            return Error.InvalidField("branchIds", "At least one branch is required for Branches scope.");
        }

        var found = await db.Branches.AsNoTracking().CountAsync(b => b.ClinicId == clinicId && ids.Contains(b.Id), ct);
        return found == ids.Count ? Ok(s, ids) : Error.InvalidField("branchIds", "Unknown branch.");
    }

    private static Result<(MembershipScope Scope, IReadOnlyList<Guid> BranchIds)> Ok(MembershipScope scope, IReadOnlyList<Guid> ids) => (scope, ids);

    public static Error? ValidateRole(StaffRole? role) =>
        role is { } r && Enum.IsDefined(r) ? null : Error.InvalidField("role", "Required (ClinicOwner, ClinicAdmin, Receptionist, Doctor or FinanceManager).");

    /// <summary>Display name for staff lists: account name → patient name → masked phone → e-mail.</summary>
    public static async Task<Dictionary<Guid, (string Name, string? PhoneMasked)>> PeopleAsync(IOqTishDb db, IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.DisplayName }).ToListAsync(ct);
        var profiles = await db.PatientProfiles.AsNoTracking().Where(p => userIds.Contains(p.UserId) && !p.IsAnonymised).Select(p => new { p.UserId, p.FullName }).ToListAsync(ct);
        var identities = await db.AuthIdentities.AsNoTracking()
            .Where(i => userIds.Contains(i.UserId) && (i.Kind == AuthIdentityKind.Phone || i.Kind == AuthIdentityKind.EmailPassword))
            .Select(i => new { i.UserId, i.Kind, i.NormalizedIdentifier })
            .ToListAsync(ct);
        return users.ToDictionary(u => u.Id, u =>
        {
            var phone = identities.FirstOrDefault(i => i.UserId == u.Id && i.Kind == AuthIdentityKind.Phone)?.NormalizedIdentifier;
            var masked = phone is null ? null : PhoneNumber.Mask(phone);
            var email = identities.FirstOrDefault(i => i.UserId == u.Id && i.Kind == AuthIdentityKind.EmailPassword)?.NormalizedIdentifier;
            var name = u.DisplayName ?? profiles.FirstOrDefault(p => p.UserId == u.Id)?.FullName ?? masked ?? email ?? "—";
            return (name, masked);
        });
    }
}
