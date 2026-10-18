using Microsoft.EntityFrameworkCore;
using OqTish.Application.Admin;
using OqTish.Application.Common;
using OqTish.Application.Staff;
using OqTish.Domain.Reviews;
using OqTish.Domain.Tenancy;

namespace OqTish.Application.Reviews;

public sealed record ReportCommand(ReportTargetType? TargetType, Guid? TargetId, string? Reason);

public sealed record ReportCreatedDto(Guid Id, ReportStatus Status);

public sealed record ResolveReportCommand(string? Resolution);

public sealed record AbuseReportDto(
    Guid Id,
    ReportTargetType TargetType,
    Guid TargetId,
    string? TargetLabel,
    Guid ClinicId,
    string ClinicName,
    string Reason,
    ReportStatus Status,
    DateTimeOffset CreatedAt,
    string? Resolution,
    DateTimeOffset? ResolvedAt);

/// <summary>
/// Abuse reports (OD-24).
/// <list type="bullet">
/// <item>POST /reports <c>{ targetType: Clinic|Review, targetId, reason 1..500 }</c> — any signed-in user. The target
/// must be publicly visible (an Approved clinic / a Published review), else 404. One open report per
/// (reporter, target): a repeat returns the existing open report (201, idempotent; partial unique index).
/// Audit <c>report.created</c>.</item>
/// <item>GET /admin/reports?status, POST /admin/reports/{id}/resolve <c>{ resolution 1..1000 }</c> —
/// <c>platform.reports.handle</c>; resolving twice → 409 <c>admin.invalid_status_transition</c>. Audit
/// <c>admin.report.resolved</c>.</item>
/// </list>
/// </summary>
public sealed class ReportHandlers(
    IOqTishDb db,
    ITransactionRunner transactions,
    IRowLocks locks,
    IAccessService access,
    ICurrentUser currentUser,
    IAuditLog audit,
    IDbErrorClassifier dbErrors,
    TimeProvider clock)
{
    public async Task<Result<ReportCreatedDto>> CreateAsync(ReportCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var userId = currentUser.RequiredUserId;
        var errors = new FieldErrors();
        if (command.TargetType is null)
        {
            errors.Add("targetType", "Clinic or Review.");
        }

        if (command.TargetId is null)
        {
            errors.Add("targetId", "Required.");
        }

        var reason = errors.Text(command.Reason, "reason", AbuseReport.MaxReasonLength, required: true);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var targetId = command.TargetId!.Value;
        Guid? clinicId = command.TargetType == ReportTargetType.Clinic
            ? await db.Clinics.Where(c => c.Id == targetId && c.VerificationStatus == ClinicVerificationStatus.Approved).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct)
            : await db.Reviews.Where(r => r.Id == targetId && r.ModerationStatus == ReviewModerationStatus.Published).Select(r => (Guid?)r.ClinicId).FirstOrDefaultAsync(ct);
        if (clinicId is null)
        {
            return CommonErrors.NotFound;
        }

        var existing = await OpenReportAsync(userId, command.TargetType!.Value, targetId, ct);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await transactions.RunAsync<ReportCreatedDto>(async token =>
            {
                var report = AbuseReport.Create(command.TargetType.Value, targetId, clinicId.Value, userId, reason!, clock.GetUtcNow());
                db.AbuseReports.Add(report);
                audit.Record(new AuditEvent("report.created", clinicId, TargetType: "report", TargetId: report.Id.ToString(),
                    Details: new { targetType = report.TargetType, targetId }));
                await db.SaveChangesAsync(token);
                return new ReportCreatedDto(report.Id, report.Status);
            }, ct);
        }
        catch (DbUpdateException ex) when (dbErrors.Classify(ex) is { Kind: DbErrorKind.UniqueViolation })
        {
            return (await OpenReportAsync(userId, command.TargetType.Value, targetId, ct))!;
        }
    }

    public async Task<Result<Page<AbuseReportDto>>> AdminListAsync(ReportStatus? status, int? page, int? pageSize, CancellationToken ct)
    {
        var check = (await access.GetScopeAsync(ct)).CheckPlatform(PlatformPermissions.ReportsHandle);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var paging = PageRequest.Create(page, pageSize);
        if (!paging.IsSuccess)
        {
            return paging.Error;
        }

        var q = db.AbuseReports.AsNoTracking().AsQueryable();
        if (status is { } s)
        {
            q = q.Where(r => r.Status == s);
        }

        var rows = await (
                from r in q
                join c in db.Clinics.AsNoTracking() on r.ClinicId equals c.Id
                orderby r.Status == ReportStatus.Open descending, r.CreatedAt descending, r.Id descending
                select new
                {
                    Report = r,
                    ClinicName = c.Name,
                    ReviewText = db.Reviews.Where(v => v.Id == r.TargetId).Select(v => v.Text).FirstOrDefault(),
                })
            .ToPageAsync(paging.Value, ct);
        return new Page<AbuseReportDto>(
            [.. rows.Items.Select(x => new AbuseReportDto(
                x.Report.Id, x.Report.TargetType, x.Report.TargetId,
                x.Report.TargetType == ReportTargetType.Clinic ? x.ClinicName : Excerpt(x.ReviewText),
                x.Report.ClinicId, x.ClinicName, x.Report.Reason, x.Report.Status, x.Report.CreatedAt, x.Report.Resolution, x.Report.ResolvedAt))],
            rows.PageNumber, rows.PageSize, rows.TotalCount);
    }

    public async Task<Result> ResolveAsync(Guid reportId, ResolveReportCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var check = (await access.GetScopeAsync(ct)).CheckPlatform(PlatformPermissions.ReportsHandle);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var resolution = command.Resolution?.Trim();
        if (string.IsNullOrEmpty(resolution) || resolution.Length > AbuseReport.MaxResolutionLength)
        {
            return Error.InvalidField("resolution", $"Required, at most {AbuseReport.MaxResolutionLength} characters.");
        }

        var adminId = currentUser.RequiredUserId;
        return await transactions.RunAsync(async token =>
        {
            var report = await locks.LoadForUpdateAsync<AbuseReport>(reportId, token);
            if (report is null)
            {
                return CommonErrors.NotFound;
            }

            if (!report.Resolve(resolution, adminId, clock.GetUtcNow()))
            {
                return AdminErrors.InvalidStatusTransition;
            }

            audit.Record(new AuditEvent("admin.report.resolved", report.ClinicId, TargetType: "report", TargetId: report.Id.ToString()));
            await db.SaveChangesAsync(token);
            return Result.Success;
        }, ct);
    }

    private static string? Excerpt(string? text) => text is null ? null : text.Length <= 80 ? text : text[..80] + "…";

    private async Task<ReportCreatedDto?> OpenReportAsync(Guid userId, ReportTargetType type, Guid targetId, CancellationToken ct) =>
        await db.AbuseReports.AsNoTracking()
            .Where(r => r.ReporterUserId == userId && r.TargetType == type && r.TargetId == targetId && r.Status == ReportStatus.Open)
            .Select(r => new ReportCreatedDto(r.Id, r.Status))
            .FirstOrDefaultAsync(ct);
}
