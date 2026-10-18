// LINQ string calls below are translated to SQL lower()/strpos(); .NET culture rules never apply.
#pragma warning disable CA1304, CA1311, CA1862
using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Identity;
using OqTish.Application.Staff;
using OqTish.Domain.Identity;
using OqTish.Domain.Tenancy;

namespace OqTish.Application.Admin;

public sealed record AdminDashboardDto(
    IReadOnlyDictionary<string, int> ClinicsByStatus,
    int AppointmentsToday,
    int OpenReports,
    int ActiveUsers,
    int SuspendedUsers);

public sealed record AdminClinicDto(
    Guid Id,
    string Name,
    string LegalName,
    string Phone,
    ClinicVerificationStatus VerificationStatus,
    bool IsSuspended,
    string? StatusReason,
    int BranchCount,
    DateTimeOffset CreatedAt);

public sealed record AdminUserDto(Guid Id, string DisplayName, string? PhoneMasked, string? Email, UserStatus Status, string? StatusReason, DateTimeOffset CreatedAt);

public sealed record AdminReasonCommand(string? Reason);

/// <summary>Mutable admin dashboard counters; BE2 (appointments today, open reports) adds contributors.</summary>
public sealed class AdminDashboardCounts
{
    public int AppointmentsToday { get; set; }

    public int OpenReports { get; set; }
}

/// <summary>Extension point for <c>GET /api/admin/dashboard</c> (register as scoped).</summary>
public interface IAdminDashboardContributor
{
    Task ContributeAsync(AdminDashboardCounts counts, CancellationToken cancellationToken);
}

/// <summary>Platform-admin error codes (docs/API.md §9).</summary>
public static class AdminErrors
{
    public static readonly Error InvalidStatusTransition = Error.Conflict("admin.invalid_status_transition", "The clinic or account is not in a state that allows this action.");

    public static readonly Error CannotSuspendSelf = Error.Rule("admin.cannot_suspend_self", "Platform admins cannot suspend their own account.");
}

/// <summary>
/// Platform admin (docs/API.md §5). Explicit platform grants only (TOTP rule applies, AccessScope.CheckPlatform).
/// <list type="bullet">
/// <item>GET /admin/dashboard — any effective platform permission.</item>
/// <item>GET /admin/clinics, POST …/approve, …/reject {reason} — <c>platform.clinic.verify</c>.
/// Approve: Pending/Rejected → Approved; reject: Pending → Rejected.</item>
/// <item>POST …/suspend {reason}, …/reinstate — <c>platform.account.suspend</c>. Suspend: Approved →
/// Suspended (every membership stops working on the next request; hidden from the public); reinstate:
/// Suspended → Approved.</item>
/// <item>GET /admin/users, POST /admin/users/{id}/suspend {reason}, …/reinstate — <c>platform.account.suspend</c>.
/// Suspension rotates the security stamp and revokes every session; self-suspension is refused.</item>
/// </list>
/// Side effects: audit <c>admin.clinic.approved|rejected|suspended|reinstated</c>, <c>admin.user.suspended|reinstated</c>.
/// </summary>
public sealed class AdminHandlers(
    IOqTishDb db,
    IAccessService access,
    ITransactionRunner transactions,
    SessionService sessions,
    IAuditLog audit,
    ICurrentUser currentUser,
    IEnumerable<IAdminDashboardContributor> contributors,
    TimeProvider clock)
{
    public async Task<Result<AdminDashboardDto>> DashboardAsync(CancellationToken ct)
    {
        var scope = await access.GetScopeAsync(ct);
        if (scope.PlatformGrants.Count == 0)
        {
            return CommonErrors.Forbidden;
        }

        if (scope.PlatformTotpBlocked)
        {
            return AuthErrors.TotpSetupRequired;
        }

        var byStatus = await db.Clinics.AsNoTracking().GroupBy(c => c.VerificationStatus).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var users = await db.Users.AsNoTracking().GroupBy(u => u.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var counts = new AdminDashboardCounts();
        foreach (var contributor in contributors)
        {
            await contributor.ContributeAsync(counts, ct);
        }

        return new AdminDashboardDto(
            Enum.GetValues<ClinicVerificationStatus>().ToDictionary(s => s.ToString(), s => byStatus.FirstOrDefault(x => x.Key == s)?.Count ?? 0),
            counts.AppointmentsToday,
            counts.OpenReports,
            users.FirstOrDefault(x => x.Key == UserStatus.Active)?.Count ?? 0,
            users.FirstOrDefault(x => x.Key == UserStatus.Suspended)?.Count ?? 0);
    }

    public async Task<Result<Page<AdminClinicDto>>> ClinicsAsync(ClinicVerificationStatus? status, string? q, int? page, int? pageSize, CancellationToken ct)
    {
        var check = (await access.GetScopeAsync(ct)).CheckPlatform(PlatformPermissions.ClinicVerify);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var paging = PageRequest.Create(page, pageSize);
        if (!paging.IsSuccess)
        {
            return paging.Error;
        }

        var query = db.Clinics.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(c => c.VerificationStatus == s);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(term) || (c.LegalName != null && c.LegalName.ToLower().Contains(term)));
        }

        return await query
            .OrderBy(c => c.VerificationStatus == ClinicVerificationStatus.Pending ? 0 : 1).ThenByDescending(c => c.CreatedAt).ThenBy(c => c.Id)
            .Select(c => new AdminClinicDto(c.Id, c.Name, c.LegalName ?? "", c.Phone ?? "", c.VerificationStatus,
                c.VerificationStatus == ClinicVerificationStatus.Suspended, c.StatusReason, db.Branches.Count(b => b.ClinicId == c.Id), c.CreatedAt))
            .ToPageAsync(paging.Value, ct);
    }

    public Task<Result<AdminClinicDto>> ApproveAsync(Guid clinicId, CancellationToken ct) =>
        ChangeClinicAsync(clinicId, PlatformPermissions.ClinicVerify, null, requireReason: false, "admin.clinic.approved",
            c => c.VerificationStatus is ClinicVerificationStatus.Pending or ClinicVerificationStatus.Rejected, (c, _, now) => c.Approve(now), ct);

    public Task<Result<AdminClinicDto>> RejectAsync(Guid clinicId, AdminReasonCommand command, CancellationToken ct) =>
        ChangeClinicAsync(clinicId, PlatformPermissions.ClinicVerify, command?.Reason, requireReason: true, "admin.clinic.rejected",
            c => c.VerificationStatus == ClinicVerificationStatus.Pending, (c, r, now) => c.Reject(r!, now), ct);

    public Task<Result<AdminClinicDto>> SuspendAsync(Guid clinicId, AdminReasonCommand command, CancellationToken ct) =>
        ChangeClinicAsync(clinicId, PlatformPermissions.AccountSuspend, command?.Reason, requireReason: true, "admin.clinic.suspended",
            c => c.VerificationStatus == ClinicVerificationStatus.Approved, (c, r, now) => c.Suspend(r!, now), ct);

    public Task<Result<AdminClinicDto>> ReinstateAsync(Guid clinicId, CancellationToken ct) =>
        ChangeClinicAsync(clinicId, PlatformPermissions.AccountSuspend, null, requireReason: false, "admin.clinic.reinstated",
            c => c.VerificationStatus == ClinicVerificationStatus.Suspended, (c, _, now) => c.Reinstate(now), ct);

    public async Task<Result<Page<AdminUserDto>>> UsersAsync(string? q, int? page, int? pageSize, CancellationToken ct)
    {
        var check = (await access.GetScopeAsync(ct)).CheckPlatform(PlatformPermissions.AccountSuspend);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var paging = PageRequest.Create(page, pageSize);
        if (!paging.IsSuccess)
        {
            return paging.Error;
        }

        var query = db.Users.AsNoTracking().Where(u => u.Status != UserStatus.Deleted);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            var phone = PhoneNumber.TryNormalize(q, out var e164) ? e164 : null;
            query = query.Where(u => (u.DisplayName != null && u.DisplayName.ToLower().Contains(term))
                || db.AuthIdentities.Any(i => i.UserId == u.Id && i.Kind == AuthIdentityKind.EmailPassword && i.NormalizedIdentifier.Contains(term))
                || (phone != null && db.AuthIdentities.Any(i => i.UserId == u.Id && i.Kind == AuthIdentityKind.Phone && i.NormalizedIdentifier == phone)));
        }

        var rows = await query.OrderByDescending(u => u.CreatedAt).ThenBy(u => u.Id)
            .Select(u => new
            {
                u.Id, u.DisplayName, u.Status, u.StatusReason, u.CreatedAt,
                Phone = db.AuthIdentities.Where(i => i.UserId == u.Id && i.Kind == AuthIdentityKind.Phone).Select(i => i.NormalizedIdentifier).FirstOrDefault(),
                Email = db.AuthIdentities.Where(i => i.UserId == u.Id && i.Kind == AuthIdentityKind.EmailPassword).Select(i => i.NormalizedIdentifier).FirstOrDefault(),
                Patient = db.PatientProfiles.Where(p => p.UserId == u.Id && !p.IsAnonymised).Select(p => p.FullName).FirstOrDefault(),
            })
            .ToPageAsync(paging.Value, ct);
        return new Page<AdminUserDto>(
            [.. rows.Items.Select(r =>
            {
                var masked = r.Phone is null ? null : PhoneNumber.Mask(r.Phone);
                return new AdminUserDto(r.Id, r.DisplayName ?? r.Patient ?? masked ?? r.Email ?? "—", masked, r.Email, r.Status, r.StatusReason, r.CreatedAt);
            })],
            rows.PageNumber, rows.PageSize, rows.TotalCount);
    }

    public async Task<Result> SuspendUserAsync(Guid userId, AdminReasonCommand command, CancellationToken ct)
    {
        var check = (await access.GetScopeAsync(ct)).CheckPlatform(PlatformPermissions.AccountSuspend);
        if (!check.IsSuccess)
        {
            return check;
        }

        if (ValidReason(command?.Reason) is not { } reason)
        {
            return Error.InvalidField("reason", "Required, at most 1000 characters.");
        }

        return await transactions.RunAsync(async token =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, token);
            if (user is null)
            {
                return CommonErrors.NotFound;
            }

            if (user.Id == currentUser.RequiredUserId)
            {
                return AdminErrors.CannotSuspendSelf;
            }

            if (user.Status != UserStatus.Active)
            {
                return AdminErrors.InvalidStatusTransition;
            }

            user.Suspend(reason, clock.GetUtcNow());
            var revoked = await sessions.RevokeAllAsync(user.Id, SessionRevocationReasons.Suspended, null, token);
            audit.Record(new AuditEvent("admin.user.suspended", TargetType: "user", TargetId: user.Id.ToString(), Details: new { reason, revokedSessions = revoked }));
            await db.SaveChangesAsync(token);
            return Result.Success;
        }, ct);
    }

    public async Task<Result> ReinstateUserAsync(Guid userId, CancellationToken ct)
    {
        var check = (await access.GetScopeAsync(ct)).CheckPlatform(PlatformPermissions.AccountSuspend);
        if (!check.IsSuccess)
        {
            return check;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return CommonErrors.NotFound;
        }

        if (user.Status != UserStatus.Suspended)
        {
            return AdminErrors.InvalidStatusTransition;
        }

        user.Reinstate(clock.GetUtcNow());
        audit.Record(new AuditEvent("admin.user.reinstated", TargetType: "user", TargetId: user.Id.ToString()));
        await db.SaveChangesAsync(ct);
        return Result.Success;
    }

    private static string? ValidReason(string? reason) =>
        reason?.Trim() is { Length: > 0 and <= 1000 } r ? r : null;

    private async Task<Result<AdminClinicDto>> ChangeClinicAsync(
        Guid clinicId,
        string permission,
        string? rawReason,
        bool requireReason,
        string action,
        Func<Clinic, bool> allowed,
        Action<Clinic, string?, DateTimeOffset> apply,
        CancellationToken ct)
    {
        var check = (await access.GetScopeAsync(ct)).CheckPlatform(permission);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var reason = ValidReason(rawReason);
        if (requireReason && reason is null)
        {
            return Error.InvalidField("reason", "Required, at most 1000 characters.");
        }

        return await transactions.RunAsync<AdminClinicDto>(async token =>
        {
            var clinic = await db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId, token);
            if (clinic is null)
            {
                return CommonErrors.NotFound;
            }

            if (!allowed(clinic))
            {
                return AdminErrors.InvalidStatusTransition;
            }

            var from = clinic.VerificationStatus;
            apply(clinic, reason, clock.GetUtcNow());
            audit.Record(new AuditEvent(action, clinic.Id, TargetType: "clinic", TargetId: clinic.Id.ToString(),
                Details: new { from = from.ToString(), to = clinic.VerificationStatus.ToString(), reason }));
            await db.SaveChangesAsync(token);
            var branches = await db.Branches.CountAsync(b => b.ClinicId == clinic.Id, token);
            return new AdminClinicDto(clinic.Id, clinic.Name, clinic.LegalName ?? "", clinic.Phone ?? "", clinic.VerificationStatus,
                clinic.IsSuspended, clinic.StatusReason, branches, clinic.CreatedAt);
        }, ct);
    }
}
