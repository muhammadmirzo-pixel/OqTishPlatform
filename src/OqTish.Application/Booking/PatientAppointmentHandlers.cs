using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Domain.Booking;

namespace OqTish.Application.Booking;

/// <summary>
/// The patient's own appointments (docs/API.md §3). Auth: signed-in user; another patient's appointment → 404.
/// <list type="bullet">
/// <item>GET /me/appointments?status&amp;page&amp;pageSize — newest first (status filter uses the
/// "overdue Pending = Expired" rule).</item>
/// <item>GET /me/appointments/{id} — with status history.</item>
/// <item>POST /me/appointments/{id}/cancel — Pending any time (until it expires), Confirmed until
/// start − <c>Booking:PatientCancelCutoffMinutes</c> (422 <c>booking.cancel_too_late</c>), by database time.
/// Lock order branch → appointment → block; conditional UPDATE; block released; staff notified.</item>
/// </list>
/// </summary>
public sealed class PatientAppointmentHandlers(
    IOqTishDb db,
    ITransactionRunner transactions,
    IRowLocks locks,
    IBookingStore store,
    IDbClock clock,
    ICurrentUser currentUser,
    IAuditLog audit,
    BookingNotifier notifier,
    AppointmentReader reader,
    IOptions<BookingOptions> options)
{
    public async Task<Result<Page<AppointmentDto>>> ListAsync(AppointmentStatus? status, int? page, int? pageSize, CancellationToken ct)
    {
        var paging = PageRequest.Create(page, pageSize);
        if (!paging.IsSuccess)
        {
            return paging.Error;
        }

        var userId = currentUser.RequiredUserId;
        var now = await clock.NowAsync(ct);
        var q = AppointmentReader.WhereStatus(db.Appointments.AsNoTracking().Where(a => a.PatientUserId == userId), status, now);
        return await reader.PageAsync(q, newestFirst: true, paging.Value, AppointmentViewerKind.Patient, null, now, ct);
    }

    public async Task<Result<AppointmentDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var userId = currentUser.RequiredUserId;
        if (!await db.Appointments.AnyAsync(a => a.Id == id && a.PatientUserId == userId, ct))
        {
            return CommonErrors.NotFound;
        }

        var now = await clock.NowAsync(ct);
        return (await reader.GetAsync(id, AppointmentViewerKind.Patient, null, now, withHistory: true, ct))!;
    }

    public async Task<Result<AppointmentDto>> CancelAsync(Guid id, CancellationToken ct)
    {
        var userId = currentUser.RequiredUserId;
        var target = await db.Appointments.AsNoTracking().Where(a => a.Id == id && a.PatientUserId == userId).Select(a => new { a.Id, a.BranchId }).FirstOrDefaultAsync(ct);
        if (target is null)
        {
            return CommonErrors.NotFound;
        }

        return await transactions.RunAsync<AppointmentDto>(async token =>
        {
            await locks.LockBranchAsync(target.BranchId, token);
            await locks.LockAsync<Appointment>(target.Id, token);
            var a = await db.Appointments.AsNoTracking().FirstAsync(x => x.Id == target.Id, token);
            var now = await clock.NowAsync(token);
            int rows;
            switch (a.Status)
            {
                case AppointmentStatus.Pending:
                    if (a.ExpiresAt <= now)
                    {
                        return BookingErrors.Expired;
                    }

                    rows = await db.Appointments
                        .Where(x => x.Id == a.Id && x.Status == AppointmentStatus.Pending && x.ExpiresAt > DbTime.StatementTimestamp())
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(x => x.Status, AppointmentStatus.CancelledByPatient)
                            .SetProperty(x => x.ReasonCode, ReasonCodes.PatientCancelled)
                            .SetProperty(x => x.UpdatedAt, now)
                            .SetProperty(x => x.StatusChangedAt, now), token);
                    break;
                case AppointmentStatus.Confirmed:
                    if (now >= a.StartsAt.AddMinutes(-options.Value.PatientCancelCutoffMinutes))
                    {
                        return BookingErrors.CancelTooLate;
                    }

                    rows = await db.Appointments
                        .Where(x => x.Id == a.Id && x.Status == AppointmentStatus.Confirmed)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(x => x.Status, AppointmentStatus.CancelledByPatient)
                            .SetProperty(x => x.ReasonCode, ReasonCodes.PatientCancelled)
                            .SetProperty(x => x.UpdatedAt, now)
                            .SetProperty(x => x.StatusChangedAt, now), token);
                    break;
                default:
                    return BookingErrors.InvalidTransition;
            }

            if (rows != 1)
            {
                return BookingErrors.InvalidTransition;
            }

            await store.ReleaseBlocksAsync([a.Id], now, token);
            db.AppointmentStatusHistory.Add(AppointmentStatusHistory.Create(a.ClinicId, a.Id, a.Status, AppointmentStatus.CancelledByPatient, HistoryActorKind.Patient, userId,
                ReasonCodes.PatientCancelled, now));
            await notifier.NotifyAsync([a.Id], BookingEvent.CancelledByPatient, now, token);
            audit.Record(new AuditEvent("booking.appointment.cancelled_by_patient", a.ClinicId, a.BranchId, "appointment", a.Id.ToString(), new { from = a.Status }));
            await db.SaveChangesAsync(token);
            return (await reader.GetAsync(a.Id, AppointmentViewerKind.Patient, null, now, withHistory: true, token))!;
        }, ct);
    }
}
