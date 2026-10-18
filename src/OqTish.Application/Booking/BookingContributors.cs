using Microsoft.EntityFrameworkCore;
using OqTish.Application.Admin;
using OqTish.Application.Common;
using OqTish.Application.Identity.Handlers;
using OqTish.Application.Staff;
using OqTish.Application.Tenancy;
using OqTish.Domain.Booking;
using OqTish.Domain.Reviews;

namespace OqTish.Application.Booking;

/// <summary>
/// Clinic dashboard counts (BE1b-16) for the context's branches: <c>pending</c> = Pending requests waiting now
/// (not overdue, any date), <c>confirmed</c>/<c>completed</c>/<c>noShow</c> = appointments starting on the
/// branch-local "today". Doctor role: only its own appointments (and no pending requests).
/// </summary>
public sealed class BookingDashboardContributor(IOqTishDb db, IAccessService access, IDbClock clock) : IClinicDashboardContributor
{
    public async Task ContributeAsync(ClinicDashboardContext context, ClinicDashboardCounts counts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(counts);
        var scope = await access.GetScopeAsync(cancellationToken);
        var branches = context.BranchIds.ToList();
        var q = db.Appointments.AsNoTracking().Where(a => a.ClinicId == context.ClinicId && branches.Contains(a.BranchId));
        if (scope.IsOwnOnly(Permissions.AppointmentView, context.ClinicId))
        {
            var own = scope.DoctorIdFor(context.ClinicId);
            q = q.Where(a => own != null && a.DoctorId == own);
        }

        var now = await clock.NowAsync(cancellationToken);
        var (start, end) = BookingTime.DayRange(context.LocalDate, BookingTime.Zone(context.TimeZone));
        counts.Pending += await q.CountAsync(a => a.Status == AppointmentStatus.Pending && a.ExpiresAt > now, cancellationToken);
        var today = await q.Where(a => a.StartsAt >= start && a.StartsAt < end).GroupBy(a => a.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        counts.Confirmed += today.FirstOrDefault(x => x.Key == AppointmentStatus.Confirmed)?.Count ?? 0;
        counts.Completed += today.FirstOrDefault(x => x.Key == AppointmentStatus.Completed)?.Count ?? 0;
        counts.NoShow += today.FirstOrDefault(x => x.Key == AppointmentStatus.NoShow)?.Count ?? 0;
    }
}

/// <summary>
/// Admin dashboard: <c>appointmentsToday</c> = appointments (Pending not overdue, Confirmed, Completed, NoShow)
/// starting today in Asia/Tashkent; <c>openReports</c> = open abuse reports.
/// </summary>
public sealed class BookingAdminDashboardContributor(IOqTishDb db, IDbClock clock) : IAdminDashboardContributor
{
    public async Task ContributeAsync(AdminDashboardCounts counts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(counts);
        var now = await clock.NowAsync(cancellationToken);
        var zone = BookingTime.Zone(Domain.Tenancy.Branch.DefaultTimeZone);
        var (start, end) = BookingTime.DayRange(BookingTime.LocalDate(now, zone), zone);
        counts.AppointmentsToday += await db.Appointments.AsNoTracking().CountAsync(a => a.StartsAt >= start && a.StartsAt < end
            && (a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.Completed || a.Status == AppointmentStatus.NoShow
                || (a.Status == AppointmentStatus.Pending && a.ExpiresAt > now)), cancellationToken);
        counts.OpenReports += await db.AbuseReports.AsNoTracking().CountAsync(r => r.Status == ReportStatus.Open, cancellationToken);
    }
}

/// <summary>
/// Self-deletion (OD-18, BE1a-7): inside the deletion transaction, cancels the user's active
/// appointments (Pending, and Confirmed that have not started yet; → CancelledByPatient, reason <c>AccountDeleted</c>, blocks released, staff
/// notified) under branch → appointment locks (branches in id order), and de-identifies the contact fields of
/// all their appointments. Reviews stay (their author label comes from the anonymised profile).
/// </summary>
public sealed class BookingAccountDeletionContributor(IOqTishDb db, IRowLocks locks, IBookingStore store, IDbClock clock, BookingNotifier notifier) : IAccountDeletionContributor
{
    public async Task AnonymiseAsync(Guid userId, CancellationToken cancellationToken)
    {
        var active = await db.Appointments.AsNoTracking()
            .Where(a => a.PatientUserId == userId && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed))
            .Select(a => new { a.Id, a.BranchId })
            .ToListAsync(cancellationToken);
        foreach (var branchId in active.Select(a => a.BranchId).Distinct().Order())
        {
            await locks.LockBranchAsync(branchId, cancellationToken);
        }

        var now = await clock.NowAsync(cancellationToken);
        var cancelled = new List<Guid>();
        foreach (var id in active.Select(a => a.Id).Order())
        {
            await locks.LockAsync<Appointment>(id, cancellationToken);
            var a = await db.Appointments.AsNoTracking().FirstAsync(x => x.Id == id, cancellationToken);
            if (!AppointmentStateMachine.HoldsTime.Contains(a.Status) || (a.Status == AppointmentStatus.Confirmed && a.StartsAt <= now))
            {
                continue;
            }

            var rows = await db.Appointments.Where(x => x.Id == id && x.Status == a.Status)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, AppointmentStatus.CancelledByPatient)
                    .SetProperty(x => x.ReasonCode, ReasonCodes.AccountDeleted)
                    .SetProperty(x => x.UpdatedAt, now)
                    .SetProperty(x => x.StatusChangedAt, now), cancellationToken);
            if (rows == 1)
            {
                db.AppointmentStatusHistory.Add(AppointmentStatusHistory.Create(a.ClinicId, a.Id, a.Status, AppointmentStatus.CancelledByPatient, HistoryActorKind.System, null,
                    ReasonCodes.AccountDeleted, now));
                cancelled.Add(id);
            }
        }

        await store.ReleaseBlocksAsync(cancelled, now, cancellationToken);
        await notifier.NotifyAsync(cancelled, BookingEvent.CancelledByPatient, now, cancellationToken);
        await db.Appointments.Where(a => a.PatientUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ContactName, Appointment.AnonymisedName).SetProperty(a => a.ContactPhone, (string?)null), cancellationToken);
    }
}
