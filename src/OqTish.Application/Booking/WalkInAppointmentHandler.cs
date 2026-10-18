using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Application.Staff;
using OqTish.Domain.Booking;
using OqTish.Domain.Scheduling;

namespace OqTish.Application.Booking;

/// <summary>
/// POST /api/clinics/{clinicId}/appointments (walk-in / phone booking, OD-08). Auth:
/// <c>appointment.create_walkin</c> on the branch (branch of another clinic → 404). Optional
/// <c>Idempotency-Key</c> (scoped to the staff user). Creates a <b>Confirmed</b> appointment directly with the
/// chosen doctor, no patient account (<c>patient_user_id</c> NULL), contact name + phone. Same transaction
/// shape as patient booking: branch lock → sweep-on-write for the doctor → appointment → Appointment block
/// (23P01 → 409 <c>booking.doctor_busy</c>) → history, audit. Working hours are not required (staff decide);
/// the start must be on the grid, not more than <c>Booking:WalkInPastToleranceMinutes</c> in the past and
/// within the horizon. No patient notifications (no account).
/// </summary>
public sealed class WalkInAppointmentHandler(
    IOqTishDb db,
    ITransactionRunner transactions,
    IIdempotencyStore idempotency,
    IRowLocks locks,
    IBookingStore store,
    IDbClock clock,
    IAccessService access,
    ICurrentUser currentUser,
    IAuditLog audit,
    IDbErrorClassifier dbErrors,
    BookingRules rules,
    BookingSweep sweep,
    AppointmentReader reader,
    IOptions<BookingOptions> options)
{
    private readonly BookingOptions _options = options.Value;

    public async Task<Result<AppointmentDto>> HandleAsync(Guid clinicId, WalkInCommand command, string? idempotencyKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
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

        if (command.DoctorId is null)
        {
            errors.Add("doctorId", "Required.");
        }

        var contact = BookingRules.Contact(errors, command.ContactName, command.ContactPhone);
        var branch = command.BranchId is { } bid
            ? await db.Branches.AsNoTracking().Where(b => b.Id == bid && b.ClinicId == clinicId).Select(b => new { b.Id, b.ClinicId }).FirstOrDefaultAsync(ct)
            : null;
        var scope = await access.GetScopeAsync(ct);
        if (command.BranchId is not null && branch is null)
        {
            return CommonErrors.NotFound;
        }

        var check = scope.Check(Permissions.AppointmentCreateWalkIn, clinicId, branch?.Id);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var userId = currentUser.RequiredUserId;
        var normalized = command with { StartsAt = command.StartsAt!.Value.ToUniversalTime(), ContactName = contact!.Value.Name, ContactPhone = contact.Value.Phone };
        try
        {
            return await transactions.RunAsync<AppointmentDto>(
                token => string.IsNullOrEmpty(idempotencyKey)
                    ? CreateAsync(clinicId, userId, normalized, scope, token)
                    : idempotency.RunAsync(userId, idempotencyKey, new { operation = "appointment.walkin", clinicId, request = normalized },
                        t => CreateAsync(clinicId, userId, normalized, scope, t), token),
                ct);
        }
        catch (Exception ex) when (dbErrors.Classify(ex) is { Kind: DbErrorKind.ForeignKeyViolation })
        {
            return BookingErrors.DoctorNotEligible;
        }
    }

    private async Task<Result<AppointmentDto>> CreateAsync(Guid clinicId, Guid userId, WalkInCommand c, AccessScope scope, CancellationToken ct)
    {
        var branchId = c.BranchId!.Value;
        var serviceId = c.ServiceId!.Value;
        var doctorId = c.DoctorId!.Value;
        var startsAt = c.StartsAt!.Value;
        var offer = await rules.OfferAsync(branchId, serviceId, ct);
        if (offer is null || offer.ClinicId != clinicId || !offer.IsBookable)
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
        if (startsAt < now.AddMinutes(-_options.WalkInPastToleranceMinutes) || startsAt >= BookingRules.HorizonEnd(now, zone, _options.HorizonDays))
        {
            return BookingErrors.LeadTime;
        }

        if (await rules.EligibleDoctorAsync(clinicId, branchId, serviceId, doctorId, ct) is null)
        {
            return BookingErrors.DoctorNotEligible;
        }

        await sweep.ExpireOverdueAsync(branchId, [doctorId], skipLocked: false, int.MaxValue, ct);
        var endsAt = startsAt.AddMinutes(offer.DurationMinutes);
        var appointment = Appointment.CreateWalkIn(clinicId, branchId, serviceId, doctorId, startsAt, endsAt, offer.Price, offer.PriceKind,
            c.ContactName!, c.ContactPhone!, userId, now);
        db.Appointments.Add(appointment);
        db.AppointmentStatusHistory.Add(AppointmentStatusHistory.Create(clinicId, appointment.Id, null, AppointmentStatus.Confirmed, HistoryActorKind.Staff, userId, null, now));
        await db.SaveChangesAsync(ct);
        if (!await store.TryInsertBlockAsync(DoctorTimeBlock.ForAppointment(clinicId, branchId, doctorId, appointment.Id, startsAt, endsAt, TimeBlockKind.Appointment, now), ct))
        {
            return BookingErrors.DoctorBusy;
        }

        audit.Record(new AuditEvent("booking.appointment.walkin_created", clinicId, branchId, "appointment", appointment.Id.ToString(),
            new { serviceId, doctorId, startsAt, endsAt }));
        await db.SaveChangesAsync(ct);
        return (await reader.GetAsync(appointment.Id, AppointmentViewerKind.Staff, scope, now, withHistory: true, ct))!;
    }
}
