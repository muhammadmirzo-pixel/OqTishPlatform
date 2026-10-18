using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Notifications;
using OqTish.Domain.Notifications;

namespace OqTish.Application.Booking;

public enum BookingEvent
{
    Requested = 1,
    Confirmed = 2,
    Rejected = 3,
    CancelledByPatient = 4,
    CancelledByClinic = 5,
    Rescheduled = 6,
    Expired = 7,
    Completed = 8,
    NoShow = 9,
}

/// <summary>
/// Writes the notification intents of a booking change into the outbox, in the caller's transaction
/// (docs/SERVICES.md "Bildirishnoma kontrakti"). Reminders are enqueued on confirm/reschedule and
/// cancelled (status Cancelled) whenever the appointment leaves Confirmed or moves. Walk-ins (no patient account)
/// get only the SMS fallback types (BE4: cancelled by clinic, rescheduled).
/// </summary>
public sealed class BookingNotifier(IOqTishDb db, IOutbox outbox)
{
    private static readonly (string Kind, TimeSpan Before)[] Reminders = [("24h", TimeSpan.FromHours(24)), ("2h", TimeSpan.FromHours(2))];

    public async Task NotifyAsync(IReadOnlyCollection<Guid> appointmentIds, BookingEvent evt, DateTimeOffset now, CancellationToken ct, DateTimeOffset? previousStartsAt = null)
    {
        if (appointmentIds.Count == 0)
        {
            return;
        }

        var ids = appointmentIds.ToList();
        var rows = await (
                from a in db.Appointments.AsNoTracking()
                join c in db.Clinics.AsNoTracking() on a.ClinicId equals c.Id
                join b in db.Branches.AsNoTracking() on a.BranchId equals b.Id
                join s in db.Services.AsNoTracking() on a.ServiceId equals s.Id
                where ids.Contains(a.Id)
                select new
                {
                    a.Id, a.ClinicId, a.BranchId, a.PatientUserId, a.StartsAt, a.EndsAt, a.Status, a.ReasonCode, a.DoctorId, a.ExpiresAt,
                    ClinicName = c.Name, BranchName = b.Name, b.TimeZone, ServiceName = s.Name,
                    DoctorName = db.Doctors.Where(d => d.Id == a.DoctorId).Select(d => d.DisplayName).FirstOrDefault(),
                })
            .ToListAsync(ct);

        foreach (var r in rows)
        {
            var name = EventName(evt);
            var notice = new AppointmentNotice(
                name, r.Id, RequestNumber(r.Id), r.ClinicId, r.BranchId, r.PatientUserId, r.ClinicName, r.BranchName, r.ServiceName, r.TimeZone,
                r.StartsAt, r.EndsAt, r.Status, r.ReasonCode, r.DoctorId, r.DoctorName, r.ExpiresAt, previousStartsAt);

            async Task PatientAsync(string type, string? dedupSuffix = null)
            {
                // Walk-ins (no account) only get the OD-11 SMS types; the dispatcher reads the contact phone at send time.
                if (r.PatientUserId is not null || BookingNotificationTypes.SmsFallback.Contains(type))
                {
                    await outbox.EnqueueAsync(type, BookingNotificationTypes.DedupKey(r.Id, dedupSuffix ?? name, "patient"), notice, null, ct);
                }
            }

            Task StaffAsync(string type) => outbox.EnqueueAsync(type, BookingNotificationTypes.DedupKey(r.Id, name, "staff"), notice, null, ct);

            switch (evt)
            {
                case BookingEvent.Requested:
                    await StaffAsync(BookingNotificationTypes.RequestedStaff);
                    await PatientAsync(BookingNotificationTypes.RequestedPatient);
                    break;
                case BookingEvent.Confirmed:
                    await PatientAsync(BookingNotificationTypes.ConfirmedPatient);
                    break;
                case BookingEvent.Rejected:
                    await PatientAsync(BookingNotificationTypes.RejectedPatient);
                    break;
                case BookingEvent.CancelledByPatient:
                    await StaffAsync(BookingNotificationTypes.CancelledByPatientStaff);
                    break;
                case BookingEvent.CancelledByClinic:
                    await PatientAsync(BookingNotificationTypes.CancelledByClinicPatient);
                    break;
                case BookingEvent.Rescheduled:
                    await PatientAsync(BookingNotificationTypes.RescheduledPatient, $"{name}:{r.StartsAt.ToUnixTimeSeconds()}");
                    break;
                case BookingEvent.Expired:
                    await PatientAsync(BookingNotificationTypes.ExpiredPatient);
                    await StaffAsync(BookingNotificationTypes.ExpiredStaff);
                    break;
                case BookingEvent.Completed:
                    await PatientAsync(BookingNotificationTypes.CompletedPatient);
                    break;
                case BookingEvent.NoShow:
                    break;
            }

            var moved = evt != BookingEvent.Rescheduled || previousStartsAt != r.StartsAt;
            if (evt is not (BookingEvent.Requested or BookingEvent.Confirmed) && moved)
            {
                await CancelRemindersAsync(r.Id, ct);
            }

            if (evt is BookingEvent.Confirmed or BookingEvent.Rescheduled && r.PatientUserId is { } patient)
            {
                var reminder = new AppointmentReminder(r.Id, patient, string.Empty, r.ClinicId, r.BranchId, r.ClinicName, r.BranchName, r.ServiceName, r.TimeZone, r.StartsAt, r.DoctorName);
                foreach (var (kind, before) in Reminders)
                {
                    var due = r.StartsAt - before;
                    if (due > now)
                    {
                        await outbox.EnqueueAsync(BookingNotificationTypes.ReminderPatient, BookingNotificationTypes.ReminderDedupKey(r.Id, kind, r.StartsAt), reminder with { Kind = kind }, due, ct);
                    }
                }
            }
        }
    }

    public static string EventName(BookingEvent evt) => evt switch
    {
        BookingEvent.Requested => "requested",
        BookingEvent.Confirmed => "confirmed",
        BookingEvent.Rejected => "rejected",
        BookingEvent.CancelledByPatient => "cancelled_by_patient",
        BookingEvent.CancelledByClinic => "cancelled_by_clinic",
        BookingEvent.Rescheduled => "rescheduled",
        BookingEvent.Expired => "expired",
        BookingEvent.Completed => "completed",
        BookingEvent.NoShow => "no_show",
        _ => throw new ArgumentOutOfRangeException(nameof(evt)),
    };

    public static string RequestNumber(Guid id) => id.ToString("N")[^8..].ToUpperInvariant();

    private async Task CancelRemindersAsync(Guid appointmentId, CancellationToken ct)
    {
        var prefix = BookingNotificationTypes.ReminderPrefix(appointmentId);
        await db.OutboxMessages
            .Where(m => m.DedupKey.StartsWith(prefix) && m.Status == OutboxStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, OutboxStatus.Cancelled), ct);
    }
}
