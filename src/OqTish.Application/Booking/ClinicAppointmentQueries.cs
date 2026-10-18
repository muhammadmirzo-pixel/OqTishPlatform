using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Staff;
using OqTish.Domain.Booking;
using OqTish.Domain.Scheduling;

namespace OqTish.Application.Booking;

/// <summary>
/// Clinic console reads (docs/API.md §4). Every list is filtered in SQL by the caller's scope:
/// <c>appointment.view</c> branches (<see cref="AccessScope.BranchIdsWith"/>), and for the Doctor role only
/// appointments assigned to the caller's doctor record (Pending requests have no doctor, so doctors never
/// see them). FinanceManager has read-only view.
/// <list type="bullet">
/// <item>GET …/appointments?branchId&amp;status&amp;from&amp;to&amp;doctorId&amp;page&amp;pageSize — <c>from</c>/<c>to</c>
/// are UTC instants, half-open [from, to) on <c>startsAt</c>; ordered by start. <c>branchId</c> outside the
/// scope → 403, of another clinic → 404.</item>
/// <item>GET …/appointments/{id} — detail with history (not visible → 404).</item>
/// <item>GET …/appointments/{id}/eligible-doctors — <c>appointment.triage</c>: active doctors linked to the
/// branch who provide the service and have no active block overlapping the interval (the request's own hold
/// excluded).</item>
/// <item>GET …/calendar?branchId&amp;date — <c>appointment.view</c> on the branch: doctors × blocks for one
/// branch-local day (appointments of the branch, holds, time off, and busy time at other branches without
/// details).</item>
/// </list>
/// </summary>
public sealed class ClinicAppointmentQueries(IOqTishDb db, IAccessService access, IDbClock clock, AppointmentReader reader, AvailabilityLoader loader)
{
    public async Task<Result<Page<AppointmentDto>>> ListAsync(Guid clinicId, AppointmentListQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var scope = await access.GetScopeAsync(ct);
        var (filtered, error) = await ScopeFilterAsync(scope, clinicId, query.BranchId, ct);
        if (error is not null)
        {
            return error;
        }

        var paging = PageRequest.Create(query.Page, query.PageSize);
        if (!paging.IsSuccess)
        {
            return paging.Error;
        }

        if (query.From is { } f && query.To is { } t && f >= t)
        {
            return Error.InvalidField("to", "Must be after from.");
        }

        var now = await clock.NowAsync(ct);
        var q = filtered!;
        if (query.DoctorId is { } doctorId)
        {
            q = q.Where(a => a.DoctorId == doctorId);
        }

        if (query.From is { } from)
        {
            var fromUtc = from.ToUniversalTime();
            q = q.Where(a => a.StartsAt >= fromUtc);
        }

        if (query.To is { } to)
        {
            var toUtc = to.ToUniversalTime();
            q = q.Where(a => a.StartsAt < toUtc);
        }

        q = AppointmentReader.WhereStatus(q, query.Status, now);
        return await reader.PageAsync(q, newestFirst: false, paging.Value, AppointmentViewerKind.Staff, scope, now, ct);
    }

    public async Task<Result<AppointmentDto>> GetAsync(Guid clinicId, Guid appointmentId, CancellationToken ct)
    {
        var scope = await access.GetScopeAsync(ct);
        var visible = await VisibleAsync(scope, clinicId, appointmentId, Permissions.AppointmentView, ct);
        if (!visible.IsSuccess)
        {
            return visible.Error;
        }

        var now = await clock.NowAsync(ct);
        return (await reader.GetAsync(appointmentId, AppointmentViewerKind.Staff, scope, now, withHistory: true, ct))!;
    }

    public async Task<Result<IReadOnlyList<EligibleDoctorDto>>> EligibleDoctorsAsync(Guid clinicId, Guid appointmentId, CancellationToken ct)
    {
        var scope = await access.GetScopeAsync(ct);
        var visible = await VisibleAsync(scope, clinicId, appointmentId, Permissions.AppointmentTriage, ct);
        if (!visible.IsSuccess)
        {
            return visible.Error;
        }

        var a = visible.Value;
        var now = await clock.NowAsync(ct);
        var zone = BookingTime.Zone(await db.Branches.Where(b => b.Id == a.BranchId).Select(b => b.TimeZone).FirstAsync(ct));
        var date = BookingTime.LocalDate(a.StartsAt, zone);
        var local = BookingTime.ToLocal(a.StartsAt, zone).TimeOfDay;
        var localEnd = local + (a.EndsAt - a.StartsAt);
        var weekday = BookingTime.IsoWeekday(date);
        var doctors = await db.Doctors.AsNoTracking()
            .Where(d => d.ClinicId == a.ClinicId && d.IsActive
                && db.DoctorBranches.Any(x => x.DoctorId == d.Id && x.BranchId == a.BranchId)
                && db.DoctorServices.Any(x => x.DoctorId == d.Id && x.ServiceId == a.ServiceId))
            .Select(d => new { d.Id, d.DisplayName })
            .ToListAsync(ct);
        var ids = doctors.Select(d => d.Id).ToList();
        var busy = (await loader.BusyQuery(ids, a.StartsAt, a.EndsAt, now, excludeAppointmentId: a.Id).ToListAsync(ct)).Select(b => b.DoctorId).Distinct().ToList();
        var hours = await db.WorkingHours.AsNoTracking().Where(w => ids.Contains(w.DoctorId) && w.BranchId == a.BranchId && w.Weekday == weekday)
            .Select(w => new { w.DoctorId, w.StartLocal, w.EndLocal }).ToListAsync(ct);
        var holder = await db.DoctorTimeBlocks.AsNoTracking().Where(b => b.AppointmentId == a.Id && b.IsActive).Select(b => (Guid?)b.DoctorId).FirstOrDefaultAsync(ct);
        return doctors.Where(d => !busy.Contains(d.Id))
            .Select(d => new EligibleDoctorDto(d.Id, d.DisplayName,
                hours.Any(h => h.DoctorId == d.Id && h.StartLocal.ToTimeSpan() <= local && localEnd <= h.EndLocal.ToTimeSpan()),
                holder == d.Id))
            .OrderByDescending(d => d.Provisional).ThenByDescending(d => d.WithinWorkingHours).ThenBy(d => d.Name, StringComparer.Ordinal).ThenBy(d => d.Id)
            .ToList();
    }

    public async Task<Result<CalendarDto>> CalendarAsync(Guid clinicId, Guid? branchId, string? date, CancellationToken ct)
    {
        if (branchId is not { } bid)
        {
            return Error.InvalidField("branchId", "Required.");
        }

        var branch = await db.Branches.AsNoTracking().Where(b => b.Id == bid && b.ClinicId == clinicId).Select(b => new { b.Id, b.ClinicId, b.TimeZone }).FirstOrDefaultAsync(ct);
        if (branch is null)
        {
            return CommonErrors.NotFound;
        }

        var scope = await access.GetScopeAsync(ct);
        var check = scope.Check(Permissions.AppointmentView, branch.ClinicId, branch.Id);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return Error.InvalidField("date", "Use the local date format YYYY-MM-DD.");
        }

        var ownOnly = scope.IsOwnOnly(Permissions.AppointmentView, clinicId);
        var ownDoctor = scope.DoctorIdFor(clinicId);
        var zone = BookingTime.Zone(branch.TimeZone);
        var (start, end) = BookingTime.DayRange(day, zone);
        var now = await clock.NowAsync(ct);

        var appointments = await (
                from a in db.Appointments.AsNoTracking()
                join s in db.Services.AsNoTracking() on a.ServiceId equals s.Id
                where a.BranchId == branch.Id && a.StartsAt < end && a.EndsAt > start
                    && (a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.Completed || a.Status == AppointmentStatus.NoShow
                        || (a.Status == AppointmentStatus.Pending && a.ExpiresAt > now))
                select new
                {
                    a.Id, a.StartsAt, a.EndsAt, a.Status, a.ContactName, ServiceName = s.Name,
                    DoctorId = a.DoctorId ?? db.DoctorTimeBlocks.Where(b => b.AppointmentId == a.Id && b.IsActive).Select(b => (Guid?)b.DoctorId).FirstOrDefault(),
                })
            .ToListAsync(ct);

        var linked = await (
                from l in db.DoctorBranches.AsNoTracking()
                join d in db.Doctors.AsNoTracking() on l.DoctorId equals d.Id
                where l.BranchId == branch.Id && d.IsActive
                select new { d.Id, d.DisplayName })
            .ToListAsync(ct);
        var extraIds = appointments.Where(a => a.DoctorId is not null).Select(a => a.DoctorId!.Value).Except(linked.Select(l => l.Id)).ToList();
        var extra = await db.Doctors.AsNoTracking().Where(d => extraIds.Contains(d.Id)).Select(d => new { d.Id, d.DisplayName }).ToListAsync(ct);
        var doctors = linked.Concat(extra).Where(d => !ownOnly || d.Id == ownDoctor).OrderBy(d => d.DisplayName, StringComparer.Ordinal).ThenBy(d => d.Id).ToList();
        var doctorIds = doctors.Select(d => d.Id).ToList();

        var otherBlocks = await (
                from b in db.DoctorTimeBlocks.AsNoTracking()
                where b.IsActive && doctorIds.Contains(b.DoctorId) && b.StartsAt < end && b.EndsAt > start
                where b.Kind == TimeBlockKind.TimeOff
                    || !db.Appointments.Any(a => a.Id == b.AppointmentId && (a.BranchId == branch.Id || (a.Status == AppointmentStatus.Pending && a.ExpiresAt <= now)))
                select new { b.Id, b.DoctorId, b.StartsAt, b.EndsAt, b.Kind })
            .ToListAsync(ct);
        var weekday = BookingTime.IsoWeekday(day);
        var hours = await db.WorkingHours.AsNoTracking().Where(w => doctorIds.Contains(w.DoctorId) && w.BranchId == branch.Id && w.Weekday == weekday)
            .OrderBy(w => w.StartLocal).Select(w => new { w.DoctorId, w.StartLocal, w.EndLocal }).ToListAsync(ct);

        var columns = doctors.Select(d =>
        {
            var blocks = appointments.Where(a => a.DoctorId == d.Id)
                .Select(a =>
                {
                    var hideDetails = ownOnly && a.Status == AppointmentStatus.Pending;
                    return new CalendarBlockDto(
                        a.Status == AppointmentStatus.Pending ? TimeBlockKind.Hold : TimeBlockKind.Appointment,
                        a.StartsAt, a.EndsAt, hideDetails ? null : a.Id, hideDetails ? null : a.Status, hideDetails ? null : a.ServiceName,
                        hideDetails ? null : a.ContactName, false, null);
                })
                .Concat(otherBlocks.Where(b => b.DoctorId == d.Id).Select(b => b.Kind == TimeBlockKind.TimeOff
                    ? new CalendarBlockDto(TimeBlockKind.TimeOff, b.StartsAt, b.EndsAt, null, null, null, null, false, b.Id)
                    : new CalendarBlockDto(b.Kind, b.StartsAt, b.EndsAt, null, null, null, null, true, null)))
                .OrderBy(b => b.StartsAt)
                .ToList();
            return new CalendarDoctorDto(d.Id, d.DisplayName,
                [.. hours.Where(h => h.DoctorId == d.Id).Select(h => new CalendarHoursDto(LocalFormats.Time(h.StartLocal), LocalFormats.Time(h.EndLocal)))],
                blocks);
        }).ToList();
        return new CalendarDto(LocalFormats.Date(day), branch.TimeZone, branch.Id, columns);
    }

    /// <summary>The caller's visible appointments of the clinic (SQL filter), or the error for the branch filter.</summary>
    internal async Task<(IQueryable<Appointment>? Query, Error? Error)> ScopeFilterAsync(AccessScope scope, Guid clinicId, Guid? branchId, CancellationToken ct)
    {
        IReadOnlyList<Guid> branches;
        if (branchId is { } b)
        {
            if (!await db.Branches.AnyAsync(x => x.Id == b && x.ClinicId == clinicId, ct))
            {
                return (null, CommonErrors.NotFound);
            }

            var check = scope.Check(Permissions.AppointmentView, clinicId, b);
            if (!check.IsSuccess)
            {
                return (null, check.Error);
            }

            branches = [b];
        }
        else
        {
            var check = scope.Check(Permissions.AppointmentView, clinicId);
            if (!check.IsSuccess)
            {
                return (null, check.Error);
            }

            branches = scope.BranchIdsWith(Permissions.AppointmentView, clinicId);
        }

        var q = db.Appointments.AsNoTracking().Where(a => a.ClinicId == clinicId && branches.Contains(a.BranchId));
        if (scope.IsOwnOnly(Permissions.AppointmentView, clinicId))
        {
            var own = scope.DoctorIdFor(clinicId);
            q = q.Where(a => own != null && a.DoctorId == own);
        }

        return (q, null);
    }

    private async Task<Result<Appointment>> VisibleAsync(AccessScope scope, Guid clinicId, Guid appointmentId, string permission, CancellationToken ct)
    {
        var a = await db.Appointments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == appointmentId && x.ClinicId == clinicId, ct);
        if (a is null)
        {
            return CommonErrors.NotFound;
        }

        var check = scope.Check(permission, a.ClinicId, a.BranchId);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        if (scope.IsOwnOnly(permission, a.ClinicId) && (a.DoctorId is null || a.DoctorId != scope.DoctorIdFor(a.ClinicId)))
        {
            return CommonErrors.NotFound;
        }

        return a;
    }
}
