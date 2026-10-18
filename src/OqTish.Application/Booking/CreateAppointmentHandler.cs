using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Domain.Booking;
using OqTish.Domain.Scheduling;
using OqTish.Domain.Tenancy;

namespace OqTish.Application.Booking;

/// <summary>
/// POST /api/appointments (patient, optional <c>Idempotency-Key</c>) — docs/ARCHITECTURE.md §5.
/// Validates the clinic (Approved), branch, service and offer (active), the grid, lead time and horizon,
/// then in ONE READ COMMITTED transaction:
/// <list type="number">
/// <item>idempotency key row (a concurrent duplicate waits on its primary key, then replays);</item>
/// <item>lock the branch row (<c>tenancy.branch_booking_locks FOR UPDATE</c>) — serialises booking decisions of the branch;</item>
/// <item>advisory lock per patient; count the patient's Pending requests (max <c>Booking:MaxPendingPerPatient</c>);</item>
/// <item>sweep-on-write: expire overdue Pending of the branch and overdue holds of the eligible doctors;</item>
/// <item>candidates = eligible doctors whose hours cover the interval and who are not on time off, free,
/// least flexible first; insert the appointment, then for each candidate <c>SAVEPOINT; INSERT</c> a Hold
/// block — 23P01 → roll back to the savepoint and try the next; none → 409 <c>booking.slot_unavailable</c>
/// (the whole transaction rolls back);</item>
/// <item>status history, outbox (staff: new request; patient: request received), audit, idempotency record.</item>
/// </list>
/// The provisional doctor is never returned to the patient. Expiry = database time + <c>Booking:PendingTtlMinutes</c>.
/// </summary>
public sealed class CreateAppointmentHandler(
    IOqTishDb db,
    ITransactionRunner transactions,
    IIdempotencyStore idempotency,
    IRowLocks locks,
    IBookingStore store,
    IDbClock clock,
    ICurrentUser currentUser,
    IAuditLog audit,
    BookingRules rules,
    BookingSweep sweep,
    AvailabilityLoader loader,
    BookingNotifier notifier,
    AppointmentReader reader,
    IOptions<BookingOptions> options)
{
    private readonly BookingOptions _options = options.Value;

    public async Task<Result<AppointmentDto>> HandleAsync(CreateAppointmentCommand command, string? idempotencyKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var userId = currentUser.RequiredUserId;
        var errors = new FieldErrors();
        if (command.BranchId is null)
        {
            errors.Add("branchId", "Required.");
        }

        if (command.ServiceId is null)
        {
            errors.Add("serviceId", "Required.");
        }

        if (command.StartsAt is null)
        {
            errors.Add("startsAt", "Required.");
        }

        var contact = BookingRules.Contact(errors, command.ContactName, command.ContactPhone);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var normalized = command with { StartsAt = command.StartsAt!.Value.ToUniversalTime(), ContactName = contact!.Value.Name, ContactPhone = contact.Value.Phone };
        return await transactions.RunAsync<AppointmentDto>(
            token => string.IsNullOrEmpty(idempotencyKey)
                ? CreateAsync(userId, normalized, token)
                : idempotency.RunAsync(userId, idempotencyKey, new { operation = "appointment.create", request = normalized }, t => CreateAsync(userId, normalized, t), token),
            ct);
    }

    private async Task<Result<AppointmentDto>> CreateAsync(Guid userId, CreateAppointmentCommand c, CancellationToken ct)
    {
        var branchId = c.BranchId!.Value;
        var serviceId = c.ServiceId!.Value;
        var startsAt = c.StartsAt!.Value;
        var offer = await rules.OfferAsync(branchId, serviceId, ct);
        if (offer is null)
        {
            return await db.Branches.AnyAsync(b => b.Id == branchId, ct) ? BookingErrors.ClinicUnavailable : CommonErrors.NotFound;
        }

        if (offer.ClinicStatus != ClinicVerificationStatus.Approved || !offer.IsBookable)
        {
            return BookingErrors.ClinicUnavailable;
        }

        var zone = BookingTime.Zone(offer.TimeZone);
        if (!BookingTime.IsOnGrid(startsAt, zone, _options.SlotStepMinutes))
        {
            return Error.InvalidField("startsAt", $"Start times must be on the {_options.SlotStepMinutes}-minute grid of the branch time zone.");
        }

        await locks.LockBranchAsync(branchId, ct);
        var now = await clock.NowAsync(ct);
        if (startsAt < now.AddMinutes(_options.MinLeadMinutes) || startsAt >= BookingRules.HorizonEnd(now, zone, _options.HorizonDays))
        {
            return BookingErrors.LeadTime;
        }

        var endsAt = startsAt.AddMinutes(offer.DurationMinutes);
        await locks.AdvisoryLockAsync("booking-patient:" + userId, ct);
        var pending = await db.Appointments.CountAsync(a => a.PatientUserId == userId && a.Status == AppointmentStatus.Pending && a.ExpiresAt > now, ct);
        if (pending >= _options.MaxPendingPerPatient)
        {
            return BookingErrors.TooManyPending;
        }

        var eligible = await rules.EligibleDoctorIdsAsync(branchId, serviceId, ct);
        await sweep.ExpireOverdueAsync(branchId, eligible, skipLocked: false, int.MaxValue, ct);

        var date = BookingTime.LocalDate(startsAt, zone);
        var local = BookingTime.ToLocal(startsAt, zone).TimeOfDay;
        var localEnd = local + TimeSpan.FromMinutes(offer.DurationMinutes);
        var (dayStart, dayEnd) = BookingTime.DayRange(date, zone);
        var snapshot = await loader.LoadAsync([branchId], [serviceId], dayStart, dayEnd, now, ct);
        var opening = snapshot.OpeningHours(branchId, BookingTime.IsoWeekday(date));
        if (opening is not null && !opening.Any(o => o.Covers(local, localEnd)))
        {
            return BookingErrors.OutsideHours;
        }

        var working = snapshot.Doctors(branchId, serviceId, date).Where(d => d.Works(local, localEnd, startsAt, endsAt)).ToList();
        if (working.Count == 0)
        {
            return BookingErrors.OutsideHours;
        }

        var candidates = AvailabilityEngine.Candidates(working, startsAt, endsAt);
        if (candidates.Count == 0)
        {
            return BookingErrors.SlotUnavailable;
        }

        var appointment = Appointment.CreatePending(
            offer.ClinicId, branchId, serviceId, userId, startsAt, endsAt, offer.Price, offer.PriceKind, c.ContactName!, c.ContactPhone!,
            now, now.AddMinutes(_options.PendingTtlMinutes));
        db.Appointments.Add(appointment);
        db.AppointmentStatusHistory.Add(AppointmentStatusHistory.Create(offer.ClinicId, appointment.Id, null, AppointmentStatus.Pending, HistoryActorKind.Patient, userId, null, now));
        await db.SaveChangesAsync(ct);

        Guid? held = null;
        foreach (var doctor in candidates)
        {
            var block = DoctorTimeBlock.ForAppointment(offer.ClinicId, branchId, doctor.DoctorId, appointment.Id, startsAt, endsAt, TimeBlockKind.Hold, now);
            if (await store.TryInsertBlockAsync(block, ct))
            {
                held = doctor.DoctorId;
                break;
            }
        }

        if (held is null)
        {
            return BookingErrors.SlotUnavailable;
        }

        await notifier.NotifyAsync([appointment.Id], BookingEvent.Requested, now, ct);
        audit.Record(new AuditEvent("booking.appointment.requested", offer.ClinicId, branchId, "appointment", appointment.Id.ToString(),
            new { serviceId, startsAt, endsAt }));
        await db.SaveChangesAsync(ct);
        return (await reader.GetAsync(appointment.Id, AppointmentViewerKind.Patient, null, now, withHistory: false, ct))!;
    }
}
