using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Application.Common.Security;
using OqTish.Domain.Identity;
using OqTish.Domain.Staff;
using OqTish.Domain.Tenancy;

namespace OqTish.Application.Staff;

/// <summary>
/// Reads active memberships of non-suspended clinics, branch grants, platform grants, TOTP state and the
/// doctor link, and applies the TOTP rule (<c>Security:RequireTotpForPrivilegedRoles</c>). A suspended or
/// deleted user gets an empty scope.
/// </summary>
internal sealed class AccessService(
    IOqTishDb db,
    ICurrentUser currentUser,
    IOptions<SecurityOptions> securityOptions,
    IDoctorIdentityResolver doctors) : IAccessService
{
    private AccessScope? _cached;

    public async Task<AccessScope> GetScopeAsync(CancellationToken cancellationToken)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        _cached = currentUser.UserId is { } userId
            ? await GetScopeForUserAsync(userId, cancellationToken)
            : AccessScope.Anonymous;
        return _cached;
    }

    public async Task<AccessScope> GetScopeForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var requireTotp = securityOptions.Value.RequireTotpForPrivilegedRoles;
        var userActive = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.Status == UserStatus.Active, cancellationToken);
        if (!userActive)
        {
            return new AccessScope(userId, false, requireTotp, [], []);
        }

        var memberships = await (
                from m in db.ClinicMemberships.AsNoTracking()
                join c in db.Clinics.AsNoTracking() on m.ClinicId equals c.Id
                where m.UserId == userId
                    && m.Status == MembershipStatus.Active
                    && c.VerificationStatus != ClinicVerificationStatus.Suspended
                select new { m.Id, m.ClinicId, ClinicName = c.Name, c.VerificationStatus, m.Role, m.Scope })
            .ToListAsync(cancellationToken);

        var membershipIds = memberships.Select(m => m.Id).ToList();
        var clinicWideClinicIds = memberships.Where(m => m.Scope == MembershipScope.Clinic).Select(m => m.ClinicId).ToList();

        var grants = membershipIds.Count == 0
            ? []
            : await db.BranchAccess.AsNoTracking()
                .Where(a => membershipIds.Contains(a.MembershipId))
                .Select(a => new { a.MembershipId, a.BranchId })
                .ToListAsync(cancellationToken);

        var clinicBranches = clinicWideClinicIds.Count == 0
            ? []
            : await db.Branches.AsNoTracking()
                .Where(b => clinicWideClinicIds.Contains(b.ClinicId))
                .OrderBy(b => b.Id)
                .Select(b => new { b.ClinicId, b.Id })
                .ToListAsync(cancellationToken);

        var platformGrants = await db.PlatformAdminGrants.AsNoTracking()
            .Where(g => g.UserId == userId && g.RevokedAt == null)
            .Select(g => g.Permission)
            .ToListAsync(cancellationToken);

        var totpEnabled = await db.TotpCredentials.AsNoTracking()
            .AnyAsync(t => t.UserId == userId && t.EnabledAt != null, cancellationToken);

        var doctorIds = memberships.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await doctors.GetDoctorIdsAsync(userId, [.. memberships.Select(m => m.ClinicId)], cancellationToken);

        var access = memberships
            .OrderBy(m => m.ClinicName, StringComparer.Ordinal)
            .Select(m =>
            {
                IReadOnlyList<Guid> granted = [.. grants.Where(g => g.MembershipId == m.Id).Select(g => g.BranchId).Order()];
                IReadOnlyList<Guid> accessible = m.Scope == MembershipScope.Clinic
                    ? [.. clinicBranches.Where(b => b.ClinicId == m.ClinicId).Select(b => b.Id)]
                    : granted;
                var totpBlocked = requireTotp && !totpEnabled && Permissions.TotpRequiredRoles.Contains(m.Role);
                return new MembershipAccess(
                    m.Id, m.ClinicId, m.ClinicName, m.VerificationStatus, m.Role, m.Scope, granted, accessible, totpBlocked,
                    doctorIds.TryGetValue(m.ClinicId, out var doctorId) ? doctorId : null);
            });

        return new AccessScope(userId, totpEnabled, requireTotp, access, platformGrants.Where(PlatformPermissions.IsKnown));
    }
}
