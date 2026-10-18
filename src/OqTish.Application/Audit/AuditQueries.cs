using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Staff;
using OqTish.Domain.Audit;

namespace OqTish.Application.Audit;

/// <summary>Audit row as shown in the clinic console and the platform admin (details are not exposed).</summary>
public sealed record AuditEntryDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    AuditActorKind ActorKind,
    Guid? ActorUserId,
    string? ActorName,
    string Action,
    Guid? ClinicId,
    Guid? BranchId,
    string? TargetType,
    string? TargetId);

/// <summary>
/// GET /api/clinics/{clinicId}/audit?page&amp;pageSize — <c>audit.view</c> (owner, clinic-wide) — and
/// GET /api/admin/audit?clinicId&amp;action&amp;page&amp;pageSize — <c>platform.audit.view</c>. Newest first.
/// Side effects: none.
/// </summary>
public sealed class AuditQueries(IOqTishDb db, IAccessService access)
{
    public async Task<Result<Page<AuditEntryDto>>> ClinicAsync(Guid clinicId, int? page, int? pageSize, CancellationToken ct)
    {
        var check = (await access.GetScopeAsync(ct)).CheckClinicWide(Permissions.AuditView, clinicId);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var paging = PageRequest.Create(page, pageSize);
        return paging.IsSuccess ? await QueryAsync(db.AuditLog.Where(a => a.ClinicId == clinicId), paging.Value, ct) : paging.Error;
    }

    public async Task<Result<Page<AuditEntryDto>>> PlatformAsync(Guid? clinicId, string? action, int? page, int? pageSize, CancellationToken ct)
    {
        var check = (await access.GetScopeAsync(ct)).CheckPlatform(PlatformPermissions.AuditView);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var paging = PageRequest.Create(page, pageSize);
        if (!paging.IsSuccess)
        {
            return paging.Error;
        }

        var query = db.AuditLog.AsQueryable();
        if (clinicId is { } c)
        {
            query = query.Where(a => a.ClinicId == c);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            // Exact action or a dotted prefix ("staff." → every staff action).
            var a = action.Trim();
            query = a.EndsWith('.') ? query.Where(x => x.Action.StartsWith(a)) : query.Where(x => x.Action == a);
        }

        return await QueryAsync(query, paging.Value, ct);
    }

    private async Task<Page<AuditEntryDto>> QueryAsync(IQueryable<AuditLogEntry> query, PageRequest page, CancellationToken ct)
    {
        var rows = await query.AsNoTracking()
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Select(a => new AuditEntryDto(a.Id, a.OccurredAt, a.ActorKind, a.ActorUserId,
                db.Users.Where(u => u.Id == a.ActorUserId).Select(u => u.DisplayName).FirstOrDefault(),
                a.Action, a.ClinicId, a.BranchId, a.TargetType, a.TargetId))
            .ToPageAsync(page, ct);
        return rows;
    }
}
