using Microsoft.EntityFrameworkCore;
using OqTish.Application.Booking;
using OqTish.Application.Common;
using OqTish.Application.Staff;

namespace OqTish.Application.Admin;

/// <summary>
/// Break-glass read of tenant patient data (SECURITY.md §2.5, CONVENTIONS §3.6):
/// GET /api/admin/clinics/{clinicId}/appointments?branchId&amp;status&amp;from&amp;to&amp;doctorId&amp;page&amp;pageSize with the header
/// <c>X-Access-Reason</c>. Requires <c>platform.tenant-data.read</c> (403 otherwise, checked first). The reason
/// arrives percent-encoded UTF-8 (FRONTEND.md F6) and is decoded here; after trimming it must be
/// <see cref="MinReasonLength"/>..<see cref="MaxReasonLength"/> characters (400 <c>common.validation</c> on
/// <c>X-Access-Reason</c>). Unknown clinic → 404. Every successful read writes the audit row
/// <c>platform.tenant_data.read</c> with the decoded reason, the filters and the number of rows returned.
/// </summary>
public sealed class TenantDataQueries(IOqTishDb db, IAccessService access, IDbClock clock, IAuditLog audit, AppointmentReader reader)
{
    public const string ReasonHeader = "X-Access-Reason";
    public const int MinReasonLength = 15;
    public const int MaxReasonLength = 300;

    /// <summary>Percent-decodes (UTF-8) and validates the header value; null when missing or invalid.</summary>
    public static string? DecodeReason(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(raw.Replace('+', ' ')).Trim();
        }
        catch (UriFormatException)
        {
            return null;
        }

        return decoded.Length is >= MinReasonLength and <= MaxReasonLength && !decoded.Any(char.IsControl) ? decoded : null;
    }

    public async Task<Result<Page<AppointmentDto>>> AppointmentsAsync(Guid clinicId, AppointmentListQuery query, string? rawReason, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var scope = await access.GetScopeAsync(ct);
        var check = scope.CheckPlatform(PlatformPermissions.TenantDataRead);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        if (DecodeReason(rawReason) is not { } reason)
        {
            return Error.InvalidField(ReasonHeader, $"A reason of {MinReasonLength}–{MaxReasonLength} characters is required (percent-encoded UTF-8).");
        }

        if (!await db.Clinics.AnyAsync(c => c.Id == clinicId, ct))
        {
            return CommonErrors.NotFound;
        }

        var paging = PageRequest.Create(query.Page, query.PageSize);
        if (!paging.IsSuccess)
        {
            return paging.Error;
        }

        var now = await clock.NowAsync(ct);
        var q = db.Appointments.AsNoTracking().Where(a => a.ClinicId == clinicId);
        if (query.BranchId is { } b)
        {
            q = q.Where(a => a.BranchId == b);
        }

        if (query.DoctorId is { } d)
        {
            q = q.Where(a => a.DoctorId == d);
        }

        if (query.From is { } from)
        {
            var f = from.ToUniversalTime();
            q = q.Where(a => a.StartsAt >= f);
        }

        if (query.To is { } to)
        {
            var t = to.ToUniversalTime();
            q = q.Where(a => a.StartsAt < t);
        }

        q = AppointmentReader.WhereStatus(q, query.Status, now);
        var page = await reader.PageAsync(q, newestFirst: false, paging.Value, AppointmentViewerKind.PlatformAdmin, null, now, ct);
        audit.Record(new AuditEvent("platform.tenant_data.read", clinicId, query.BranchId, "clinic", clinicId.ToString(), new
        {
            reason,
            resource = "appointments",
            filters = new { query.BranchId, query.Status, query.From, query.To, query.DoctorId, page = paging.Value.Number, pageSize = paging.Value.Size },
            returned = page.Items.Count,
        }));
        await db.SaveChangesAsync(ct);
        return page;
    }
}
