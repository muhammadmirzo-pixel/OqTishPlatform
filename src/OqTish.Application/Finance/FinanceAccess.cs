using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Staff;

namespace OqTish.Application.Finance;

/// <summary>
/// Finance authorization helpers on top of <see cref="AccessScope"/> (CONVENTIONS §3, FINANCIAL_RULES §9).
/// Resources are always loaded first (route clinic), then checked on <b>their</b> branch.
/// </summary>
public sealed class FinanceAccess(IOqTishDb db, IAccessService access)
{
    /// <summary>Success when any of the permissions passes; otherwise the first error (404 beats 403: no tenant enumeration).</summary>
    public static Result CheckAny(AccessScope scope, Guid clinicId, Guid? branchId, params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(permissions);
        Error? first = null;
        foreach (var permission in permissions)
        {
            var check = scope.Check(permission, clinicId, branchId);
            if (check.IsSuccess)
            {
                return Result.Success;
            }

            first ??= check.Error;
        }

        return first!;
    }

    /// <summary>Union of the branches on which the caller holds any of the permissions (for SQL filters).</summary>
    public static IReadOnlyList<Guid> BranchesWithAny(AccessScope scope, Guid clinicId, params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(permissions);
        return [.. permissions.SelectMany(p => scope.BranchIdsWith(p, clinicId)).Distinct()];
    }

    /// <summary>
    /// Branches a report/settlement read may cover: the caller's branches with <paramref name="permission"/>, or —
    /// for a platform admin with <c>platform.finance.settle</c> (SECURITY.md §2.1) — every branch of the clinic.
    /// With <paramref name="branchId"/>: that branch only (another clinic's branch → 404, outside scope → 403).
    /// </summary>
    public async Task<Result<Guid[]>> ReportBranchesAsync(Guid clinicId, Guid? branchId, string permission, CancellationToken ct)
    {
        var scope = await access.GetScopeAsync(ct);
        Guid[] branches;
        var check = scope.Check(permission, clinicId, null);
        if (check.IsSuccess)
        {
            branches = [.. scope.BranchIdsWith(permission, clinicId)];
        }
        else if (scope.CheckPlatform(PlatformPermissions.FinanceSettle).IsSuccess && await db.Clinics.AnyAsync(c => c.Id == clinicId, ct))
        {
            branches = await db.Branches.AsNoTracking().Where(b => b.ClinicId == clinicId).Select(b => b.Id).ToArrayAsync(ct);
        }
        else
        {
            return check.Error;
        }

        if (branchId is not { } b)
        {
            return branches;
        }

        if (!await db.Branches.AnyAsync(x => x.Id == b && x.ClinicId == clinicId, ct))
        {
            return CommonErrors.NotFound;
        }

        return branches.Contains(b) ? new[] { b } : CommonErrors.Forbidden;
    }

    /// <summary>Platform admin check for <c>platform.finance.settle</c>.</summary>
    public async Task<Result> CheckPlatformAsync(CancellationToken ct) => (await access.GetScopeAsync(ct)).CheckPlatform(PlatformPermissions.FinanceSettle);

    /// <summary>The time zone used for clinic-level views (Asia/Tashkent unless the clinic's first branch says otherwise).</summary>
    public async Task<string> ClinicTimeZoneAsync(Guid clinicId, CancellationToken ct) =>
        await db.Branches.AsNoTracking().Where(b => b.ClinicId == clinicId).OrderBy(b => b.CreatedAt).Select(b => b.TimeZone).FirstOrDefaultAsync(ct)
        ?? Domain.Tenancy.Branch.DefaultTimeZone;
}
