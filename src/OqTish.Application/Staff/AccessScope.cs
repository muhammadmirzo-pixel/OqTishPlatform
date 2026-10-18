using System.Collections.Frozen;
using OqTish.Application.Common;
using OqTish.Application.Identity;
using OqTish.Domain.Staff;
using OqTish.Domain.Tenancy;

namespace OqTish.Application.Staff;

/// <summary>One active membership of a non-suspended clinic, as seen by the access rules.</summary>
public sealed record MembershipAccess(
    Guid MembershipId,
    Guid ClinicId,
    string ClinicName,
    ClinicVerificationStatus ClinicStatus,
    StaffRole Role,
    MembershipScope Scope,
    IReadOnlyList<Guid> GrantedBranchIds,
    IReadOnlyList<Guid> AccessibleBranchIds,
    bool TotpBlocked,
    Guid? DoctorId)
{
    /// <summary>Permissions in effect: empty while TOTP setup is required.</summary>
    public IReadOnlyDictionary<string, PermissionReach> EffectivePermissions =>
        TotpBlocked ? FrozenDictionary<string, PermissionReach>.Empty : Permissions.For(Role);
}

/// <summary>
/// The caller's effective rights, computed once per request from the database by
/// <see cref="IAccessService"/> (CONVENTIONS §3). Results:
/// <list type="bullet">
/// <item>no active membership in the clinic (or the clinic is suspended) → 404 <c>common.not_found</c>;</item>
/// <item>membership blocked by the TOTP rule → 403 <c>auth.totp_setup_required</c>;</item>
/// <item>role lacks the permission, or the branch is outside a Branches-scoped membership → 403 <c>common.forbidden</c>.</item>
/// </list>
/// </summary>
public sealed class AccessScope
{
    private readonly FrozenDictionary<Guid, MembershipAccess> _byClinic;
    private readonly FrozenSet<string> _platformGranted;

    public AccessScope(Guid? userId, bool totpEnabled, bool totpRequiredForPrivileged, IEnumerable<MembershipAccess> memberships, IEnumerable<string> platformGrants)
    {
        UserId = userId;
        TotpEnabled = totpEnabled;
        TotpRequiredForPrivileged = totpRequiredForPrivileged;
        Memberships = [.. memberships];
        _byClinic = Memberships.ToFrozenDictionary(m => m.ClinicId);
        _platformGranted = platformGrants.ToFrozenSet(StringComparer.Ordinal);
    }

    public static AccessScope Anonymous { get; } = new(null, false, true, [], []);

    public Guid? UserId { get; }

    public bool TotpEnabled { get; }

    public bool TotpRequiredForPrivileged { get; }

    public IReadOnlyList<MembershipAccess> Memberships { get; }

    /// <summary>Granted platform permissions (whether or not blocked by the TOTP rule).</summary>
    public IReadOnlyCollection<string> PlatformGrants => _platformGranted;

    public bool PlatformTotpBlocked => _platformGranted.Count > 0 && TotpRequiredForPrivileged && !TotpEnabled;

    /// <summary>Platform permissions in effect (empty while TOTP setup is required).</summary>
    public IReadOnlyList<string> EffectivePlatformPermissions =>
        PlatformTotpBlocked ? [] : [.. PlatformPermissions.All.Where(_platformGranted.Contains)];

    /// <summary>True when some membership or platform grant waits for TOTP setup.</summary>
    public bool TotpSetupRequired => PlatformTotpBlocked || Memberships.Any(m => m.TotpBlocked);

    public MembershipAccess? MembershipIn(Guid clinicId) => _byClinic.GetValueOrDefault(clinicId);

    /// <summary>
    /// Checks a clinic permission on a resource loaded from the database (pass its clinic and, when the
    /// resource belongs to a branch, its branch). With <paramref name="branchId"/> null, a Branches-scoped
    /// membership passes for permissions it holds on its branches, except <see cref="Permissions.ClinicWideOnly"/>.
    /// </summary>
    public Result Check(string permission, Guid clinicId, Guid? branchId = null)
    {
        if (_byClinic.GetValueOrDefault(clinicId) is not { } membership)
        {
            return CommonErrors.NotFound;
        }

        if (membership.TotpBlocked)
        {
            return AuthErrors.TotpSetupRequired;
        }

        if (!Permissions.For(membership.Role).ContainsKey(permission))
        {
            return CommonErrors.Forbidden;
        }

        if (membership.Scope == MembershipScope.Clinic)
        {
            return Result.Success;
        }

        if (branchId is { } b)
        {
            return membership.GrantedBranchIds.Contains(b) ? Result.Success : CommonErrors.Forbidden;
        }

        return Permissions.ClinicWideOnly.Contains(permission) ? CommonErrors.Forbidden : Result.Success;
    }

    /// <summary>Like <see cref="Check"/> but additionally requires a clinic-wide (Clinic-scoped) membership.</summary>
    public Result CheckClinicWide(string permission, Guid clinicId)
    {
        var result = Check(permission, clinicId, null);
        if (!result.IsSuccess)
        {
            return result;
        }

        return _byClinic[clinicId].Scope == MembershipScope.Clinic ? Result.Success : CommonErrors.Forbidden;
    }

    /// <summary>Any active membership in the clinic (for "read: membership" endpoints).</summary>
    public Result CheckMember(Guid clinicId)
    {
        if (_byClinic.GetValueOrDefault(clinicId) is not { } membership)
        {
            return CommonErrors.NotFound;
        }

        return membership.TotpBlocked ? AuthErrors.TotpSetupRequired : Result.Success;
    }

    /// <summary>Platform permission check: no grant → 403 <c>common.forbidden</c>; TOTP rule → 403 <c>auth.totp_setup_required</c>.</summary>
    public Result CheckPlatform(string platformPermission)
    {
        if (!_platformGranted.Contains(platformPermission))
        {
            return CommonErrors.Forbidden;
        }

        return PlatformTotpBlocked ? AuthErrors.TotpSetupRequired : Result.Success;
    }

    /// <summary>True when the permission only covers the caller's own doctor record (Doctor role).</summary>
    public bool IsOwnOnly(string permission, Guid clinicId) =>
        _byClinic.GetValueOrDefault(clinicId) is { TotpBlocked: false } m
        && Permissions.For(m.Role).TryGetValue(permission, out var reach)
        && reach == PermissionReach.OwnOnly;

    /// <summary>
    /// Branch ids on which the caller holds <paramref name="permission"/> in the clinic: all clinic branches
    /// for Clinic scope, the granted ones for Branches scope, empty otherwise. Use it in SQL filters
    /// (<c>ids.Contains(x.BranchId)</c>), never to filter after loading.
    /// </summary>
    public IReadOnlyList<Guid> BranchIdsWith(string permission, Guid clinicId)
    {
        if (_byClinic.GetValueOrDefault(clinicId) is not { TotpBlocked: false } m || !Permissions.For(m.Role).ContainsKey(permission))
        {
            return [];
        }

        return m.Scope == MembershipScope.Clinic ? m.AccessibleBranchIds : m.GrantedBranchIds;
    }

    /// <summary>The caller's doctor record in the clinic (Doctor-own semantics), if one is linked to the user.</summary>
    public Guid? DoctorIdFor(Guid clinicId) => _byClinic.GetValueOrDefault(clinicId)?.DoctorId;
}
