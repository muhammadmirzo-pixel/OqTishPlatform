using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Application.Staff;
using OqTish.Domain.Booking;

namespace OqTish.Application.Booking;

/// <summary>Who reads an appointment; decides <c>canCancel</c>/<c>canReview</c>.</summary>
public enum AppointmentViewerKind
{
    Patient = 1,
    Staff = 2,
    PlatformAdmin = 3,
}

/// <summary>One joined row: the appointment plus display names.</summary>
public sealed class AppointmentRow
{
    public required Appointment Appointment { get; init; }

    public required string ClinicName { get; init; }

    public required string BranchName { get; init; }

    public required string TimeZone { get; init; }

    public required string ServiceName { get; init; }

    public string? DoctorName { get; init; }

    public bool HasReview { get; init; }
}

/// <summary>
/// Builds <see cref="AppointmentDto"/>s from SQL-filtered queries (callers apply the authorization filters in
/// SQL before paging). Overdue Pending is shown as Expired; <c>canCancel</c> is computed for the caller.
/// </summary>
public sealed class AppointmentReader(IOqTishDb db, IOptions<BookingOptions> options)
{
    private readonly BookingOptions _options = options.Value;

    public IQueryable<AppointmentRow> Rows(IQueryable<Appointment> appointments) =>
        from a in appointments
        join c in db.Clinics.AsNoTracking() on a.ClinicId equals c.Id
        join b in db.Branches.AsNoTracking() on a.BranchId equals b.Id
        join s in db.Services.AsNoTracking() on a.ServiceId equals s.Id
        select new AppointmentRow
        {
            Appointment = a,
            ClinicName = c.Name,
            BranchName = b.Name,
            TimeZone = b.TimeZone,
            ServiceName = s.Name,
            DoctorName = db.Doctors.Where(d => d.Id == a.DoctorId).Select(d => d.DisplayName).FirstOrDefault(),
            HasReview = db.Reviews.Any(r => r.AppointmentId == a.Id),
        };

    /// <summary>Status filter with the "overdue Pending = Expired" read rule, evaluated against <paramref name="now"/>.</summary>
    public static IQueryable<Appointment> WhereStatus(IQueryable<Appointment> q, AppointmentStatus? status, DateTimeOffset now) => status switch
    {
        null => q,
        AppointmentStatus.Pending => q.Where(a => a.Status == AppointmentStatus.Pending && a.ExpiresAt > now),
        AppointmentStatus.Expired => q.Where(a => a.Status == AppointmentStatus.Expired || (a.Status == AppointmentStatus.Pending && a.ExpiresAt <= now)),
        { } s => q.Where(a => a.Status == s),
    };

    public async Task<AppointmentDto?> GetAsync(Guid id, AppointmentViewerKind viewer, AccessScope? scope, DateTimeOffset now, bool withHistory, CancellationToken ct)
    {
        var row = await Rows(db.Appointments.AsNoTracking().Where(a => a.Id == id)).FirstOrDefaultAsync(ct);
        if (row is null)
        {
            return null;
        }

        IReadOnlyList<AppointmentHistoryDto>? history = null;
        if (withHistory)
        {
            history = await db.AppointmentStatusHistory.AsNoTracking()
                .Where(h => h.AppointmentId == id)
                .OrderBy(h => h.OccurredAt).ThenBy(h => h.Id)
                .Select(h => new AppointmentHistoryDto(h.From, h.To, h.OccurredAt, h.ActorKind, h.ReasonCode))
                .ToListAsync(ct);
        }

        return ToDto(row, viewer, scope, now, history);
    }

    public async Task<Page<AppointmentDto>> PageAsync(IQueryable<Appointment> filtered, bool newestFirst, PageRequest page, AppointmentViewerKind viewer, AccessScope? scope, DateTimeOffset now, CancellationToken ct)
    {
        var ordered = newestFirst
            ? Rows(filtered).OrderByDescending(r => r.Appointment.CreatedAt).ThenByDescending(r => r.Appointment.Id)
            : Rows(filtered).OrderBy(r => r.Appointment.StartsAt).ThenBy(r => r.Appointment.Id);
        var rows = await ordered.ToPageAsync(page, ct);
        return new Page<AppointmentDto>([.. rows.Items.Select(r => ToDto(r, viewer, scope, now, null))], rows.PageNumber, rows.PageSize, rows.TotalCount);
    }

    public AppointmentDto ToDto(AppointmentRow row, AppointmentViewerKind viewer, AccessScope? scope, DateTimeOffset now, IReadOnlyList<AppointmentHistoryDto>? history)
    {
        ArgumentNullException.ThrowIfNull(row);
        var a = row.Appointment;
        var overdue = a.Status == AppointmentStatus.Pending && a.ExpiresAt <= now;
        var status = overdue ? AppointmentStatus.Expired : a.Status;
        var canCancel = viewer switch
        {
            AppointmentViewerKind.Patient => status == AppointmentStatus.Pending
                || (status == AppointmentStatus.Confirmed && now < a.StartsAt.AddMinutes(-_options.PatientCancelCutoffMinutes)),
            AppointmentViewerKind.Staff => status == AppointmentStatus.Confirmed && scope is not null
                && scope.Check(Permissions.AppointmentCancel, a.ClinicId, a.BranchId).IsSuccess,
            _ => false,
        };
        var canReview = viewer == AppointmentViewerKind.Patient && status == AppointmentStatus.Completed && !row.HasReview
            && now <= a.StatusChangedAt.AddDays(_options.ReviewWindowDays);
        return new AppointmentDto(
            a.Id, a.ClinicId, row.ClinicName, a.BranchId, row.BranchName, row.TimeZone, a.ServiceId, row.ServiceName,
            a.StartsAt, a.EndsAt, status, a.ExpiresAt, a.QuotedPriceMoney, a.PriceKind, a.ChargedMoney,
            a.DoctorId is { } d && status != AppointmentStatus.Pending ? new AppointmentDoctorDto(d, row.DoctorName ?? string.Empty) : null,
            a.ContactName, a.ContactPhone ?? string.Empty, a.Source, canCancel, row.HasReview, canReview, a.CreatedAt, history);
    }
}
