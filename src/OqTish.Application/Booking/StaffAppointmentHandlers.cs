using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Application.Staff;
using OqTish.Domain.Booking;
using OqTish.Domain.Common;
using OqTish.Domain.Scheduling;

namespace OqTish.Application.Booking;

/// <summary>
/// Clinic-side transitions (docs/API.md §4, ARCHITECTURE.md §6). Every action:
/// <list type="number">
/// <item>loads the appointment by id <b>within the route clinic</b> (other clinic → 404) and checks the
/// permission on its branch (no membership → 404, in-tenant missing permission / other branch → 403;
/// Doctor-own permissions: not the caller's appointment → 404);</item>
/// <item>in one transaction locks branch → appointment row → (blocks), re-reads the status, asks
/// <see cref="AppointmentStateMachine"/>, compares time rules with database time;</item>
/// <item>writes with a conditional UPDATE on the expected status (and <c>expires_at &gt; statement_timestamp()</c>
/// for Pending), so exactly one of two racing transitions wins;</item>
/// <item>moves/releases the time block (exclusion constraint re-checks), writes history, outbox and audit.</item>
/// </list>
/// confirm/reject/reschedule need <c>appointment.triage</c>, cancel <c>appointment.cancel</c>, complete/no-show
/// <c>appointment.outcome</c> (Doctor: own). FinanceManager (view only) gets 403 on every transition.
/// </summary>
public sealed class StaffAppointmentHandlers(
    IOqTishDb db,
    ITransactionRunner transactions,
    IRowLocks locks,
    IBookingStore store,
    IDbClock clock,
    IAccessService access,
    ICurrentUser currentUser,
    IAuditLog audit,
    IDbErrorClassifier dbErrors,
    BookingRules rules,
    BookingSweep sweep,
    BookingNotifier notifier,
    AppointmentReader reader,
    IAppointmentCompletedHandler completed,
    IAppointmentConfirmedHandler confirmed,
    IOptions<BookingOptions> options)
{
    private readonly BookingOptions _options = options.Value;

    public Task<Result<AppointmentDto>> ConfirmAsync(Guid clinicId, Guid appointmentId, ConfirmCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.DoctorId is not { } doctorId)
        {
            return Task.FromResult<Result<AppointmentDto>>(Error.InvalidField("doctorId", "Required."));
        }

        return RunAsync(clinicId, appointmentId, Permissions.AppointmentTriage, async (a, now, scope, token) =>
        {
            if (a.Status != AppointmentStatus.Pending)
            {
                return BookingErrors.NotPending;
            }

            if (a.ExpiresAt <= now)
            {
                return BookingErrors.Expired;
            }

            var doctor = await rules.EligibleDoctorAsync(a.ClinicId, a.BranchId, a.ServiceId, doctorId, token);
            if (doctor is null)
            {
                return BookingErrors.DoctorNotEligible;
            }

            // Sweep other overdue holds of the doctor, never the request being confirmed: if its own deadline
            // passes meanwhile, the conditional UPDATE below affects 0 rows and the whole confirm rolls back.
            await sweep.ExpireOverdueAsync(a.BranchId, [doctorId], skipLocked: false, int.MaxValue, token, exceptAppointmentId: a.Id);
            if (!await MoveOrInsertBlockAsync(a, doctorId, a.StartsAt, a.EndsAt, now, token))
            {
                return BookingErrors.DoctorBusy;
            }

            var rows = await db.Appointments
                .Where(x => x.Id == a.Id && x.Status == AppointmentStatus.Pending && x.ExpiresAt > DbTime.StatementTimestamp())
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, AppointmentStatus.Confirmed)
                    .SetProperty(x => x.DoctorId, doctorId)
                    .SetProperty(x => x.UpdatedAt, now)
                    .SetProperty(x => x.StatusChangedAt, now), token);
            if (rows != 1)
            {
                return BookingErrors.Expired;
            }

            var hook = await confirmed.OnConfirmedAsync(
                new AppointmentConfirmedContext(a.Id, a.ClinicId, a.BranchId, a.ServiceId, doctorId, a.PatientUserId, now, currentUser.RequiredUserId), token);
            if (!hook.IsSuccess)
            {
                return hook.Error;
            }

            return await FinishAsync(a, AppointmentStatus.Pending, AppointmentStatus.Confirmed, null, BookingEvent.Confirmed, "booking.appointment.confirmed", now, scope,
                new { doctorId }, token);
        }, ct);
    }

    public Task<Result<AppointmentDto>> RejectAsync(Guid clinicId, Guid appointmentId, ReasonCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!ReasonCodes.IsValid(ReasonCodes.Reject, command.ReasonCode))
        {
            return Task.FromResult<Result<AppointmentDto>>(Error.InvalidField("reasonCode", "Use one of: " + string.Join(", ", ReasonCodes.Reject) + "."));
        }

        return RunAsync(clinicId, appointmentId, Permissions.AppointmentTriage, async (a, now, scope, token) =>
        {
            if (a.Status != AppointmentStatus.Pending)
            {
                return BookingErrors.NotPending;
            }

            if (a.ExpiresAt <= now)
            {
                return BookingErrors.Expired;
            }

            var rows = await db.Appointments
                .Where(x => x.Id == a.Id && x.Status == AppointmentStatus.Pending && x.ExpiresAt > DbTime.StatementTimestamp())
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, AppointmentStatus.Rejected)
                    .SetProperty(x => x.ReasonCode, command.ReasonCode)
                    .SetProperty(x => x.UpdatedAt, now)
                    .SetProperty(x => x.StatusChangedAt, now), token);
            if (rows != 1)
            {
                return BookingErrors.Expired;
            }

            await store.ReleaseBlocksAsync([a.Id], now, token);
            return await FinishAsync(a, AppointmentStatus.Pending, AppointmentStatus.Rejected, command.ReasonCode, BookingEvent.Rejected, "booking.appointment.rejected", now, scope,
                new { reasonCode = command.ReasonCode }, token);
        }, ct);
    }

    public Task<Result<AppointmentDto>> CancelAsync(Guid clinicId, Guid appointmentId, ReasonCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!ReasonCodes.IsValid(ReasonCodes.ClinicCancel, command.ReasonCode))
        {
            return Task.FromResult<Result<AppointmentDto>>(Error.InvalidField("reasonCode", "Use one of: " + string.Join(", ", ReasonCodes.ClinicCancel) + "."));
        }

        return RunAsync(clinicId, appointmentId, Permissions.AppointmentCancel, async (a, now, scope, token) =>
        {
            if (!AppointmentStateMachine.CanApply(a.Status, AppointmentAction.CancelByClinic))
            {
                return BookingErrors.InvalidTransition;
            }

            if (!await ConditionalAsync(a.Id, AppointmentStatus.Confirmed, AppointmentStatus.CancelledByClinic, command.ReasonCode, now, token))
            {
                return BookingErrors.InvalidTransition;
            }

            await store.ReleaseBlocksAsync([a.Id], now, token);
            return await FinishAsync(a, AppointmentStatus.Confirmed, AppointmentStatus.CancelledByClinic, command.ReasonCode, BookingEvent.CancelledByClinic,
                "booking.appointment.cancelled_by_clinic", now, scope, new { reasonCode = command.ReasonCode }, token);
        }, ct);
    }

    public Task<Result<AppointmentDto>> RescheduleAsync(Guid clinicId, Guid appointmentId, RescheduleCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = new FieldErrors();
        if (command.StartsAt is null)
        {
            errors.Add("startsAt", "Required.");
        }

        if (command.DoctorId is null)
        {
            errors.Add("doctorId", "Required.");
        }

        if (errors.HasErrors)
        {
            return Task.FromResult<Result<AppointmentDto>>(errors.ToError());
        }

        var newStart = command.StartsAt!.Value.ToUniversalTime();
        var doctorId = command.DoctorId!.Value;
        return RunAsync(clinicId, appointmentId, Permissions.AppointmentTriage, async (a, now, scope, token) =>
        {
            if (!AppointmentStateMachine.CanApply(a.Status, AppointmentAction.Reschedule))
            {
                return BookingErrors.InvalidTransition;
            }

            var zone = BookingTime.Zone(await db.Branches.Where(b => b.Id == a.BranchId).Select(b => b.TimeZone).FirstAsync(token));
            if (!BookingTime.IsOnGrid(newStart, zone, _options.SlotStepMinutes))
            {
                return Error.InvalidField("startsAt", $"Start times must be on the {_options.SlotStepMinutes}-minute grid of the branch time zone.");
            }

            if (newStart < now || newStart >= BookingRules.HorizonEnd(now, zone, _options.HorizonDays))
            {
                return BookingErrors.LeadTime;
            }

            var doctor = await rules.EligibleDoctorAsync(a.ClinicId, a.BranchId, a.ServiceId, doctorId, token);
            if (doctor is null)
            {
                return BookingErrors.DoctorNotEligible;
            }

            var newEnd = newStart + (a.EndsAt - a.StartsAt);
            await sweep.ExpireOverdueAsync(a.BranchId, [doctorId], skipLocked: false, int.MaxValue, token);
            if (!await MoveOrInsertBlockAsync(a, doctorId, newStart, newEnd, now, token))
            {
                return BookingErrors.DoctorBusy;
            }

            var rows = await db.Appointments
                .Where(x => x.Id == a.Id && x.Status == AppointmentStatus.Confirmed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.StartsAt, newStart)
                    .SetProperty(x => x.EndsAt, newEnd)
                    .SetProperty(x => x.DoctorId, doctorId)
                    .SetProperty(x => x.ReasonCode, AppointmentStatusHistory.RescheduledReason)
                    .SetProperty(x => x.UpdatedAt, now), token);
            if (rows != 1)
            {
                return BookingErrors.InvalidTransition;
            }

            return await FinishAsync(a, AppointmentStatus.Confirmed, AppointmentStatus.Confirmed, AppointmentStatusHistory.RescheduledReason, BookingEvent.Rescheduled,
                "booking.appointment.rescheduled", now, scope, new { previousStartsAt = a.StartsAt, previousDoctorId = a.DoctorId, startsAt = newStart, doctorId }, token,
                previousStartsAt: a.StartsAt);
        }, ct);
    }

    public Task<Result<AppointmentDto>> CompleteAsync(Guid clinicId, Guid appointmentId, CompleteCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.ChargedAmount is not { } charged)
        {
            return Task.FromResult<Result<AppointmentDto>>(Error.InvalidField("chargedAmount", "Required."));
        }

        if (charged.IsNegative)
        {
            return Task.FromResult<Result<AppointmentDto>>(Error.Validation("finance.amount_invalid", "The charged amount must be zero or more.",
                new Dictionary<string, string[]> { ["chargedAmount"] = ["Must be zero or more."] }));
        }

        return RunAsync(clinicId, appointmentId, Permissions.AppointmentOutcome, async (a, now, scope, token) =>
        {
            if (!AppointmentStateMachine.CanApply(a.Status, AppointmentAction.Complete))
            {
                return BookingErrors.InvalidTransition;
            }

            if (charged.Currency != a.Currency)
            {
                return Error.Validation("finance.amount_invalid", "Currency does not match the appointment.",
                    new Dictionary<string, string[]> { ["chargedAmount"] = ["Currency mismatch."] });
            }

            if (now < a.StartsAt)
            {
                return BookingErrors.TooEarlyForOutcome;
            }

            var amount = charged.Round().Amount;
            var rows = await db.Appointments
                .Where(x => x.Id == a.Id && x.Status == AppointmentStatus.Confirmed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, AppointmentStatus.Completed)
                    .SetProperty(x => x.ChargedAmount, amount)
                    .SetProperty(x => x.UpdatedAt, now)
                    .SetProperty(x => x.StatusChangedAt, now), token);
            if (rows != 1)
            {
                return BookingErrors.InvalidTransition;
            }

            await store.ReleaseBlocksAsync([a.Id], now, token);
            var hook = await completed.OnCompletedAsync(
                new AppointmentCompletedContext(a.Id, a.ClinicId, a.BranchId, a.ServiceId, a.DoctorId!.Value, a.PatientUserId, new Money(amount, a.Currency),
                    a.QuotedPriceMoney, a.StartsAt, now, currentUser.RequiredUserId),
                token);
            if (!hook.IsSuccess)
            {
                return hook.Error;
            }

            return await FinishAsync(a, AppointmentStatus.Confirmed, AppointmentStatus.Completed, null, BookingEvent.Completed, "booking.appointment.completed", now, scope,
                new { chargedAmount = amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) }, token);
        }, ct);
    }

    public Task<Result<AppointmentDto>> NoShowAsync(Guid clinicId, Guid appointmentId, CancellationToken ct) =>
        RunAsync(clinicId, appointmentId, Permissions.AppointmentOutcome, async (a, now, scope, token) =>
        {
            if (!AppointmentStateMachine.CanApply(a.Status, AppointmentAction.NoShow))
            {
                return BookingErrors.InvalidTransition;
            }

            if (now < a.StartsAt.AddMinutes(_options.NoShowGraceMinutes))
            {
                return BookingErrors.TooEarlyForOutcome;
            }

            if (!await ConditionalAsync(a.Id, AppointmentStatus.Confirmed, AppointmentStatus.NoShow, null, now, token))
            {
                return BookingErrors.InvalidTransition;
            }

            await store.ReleaseBlocksAsync([a.Id], now, token);
            return await FinishAsync(a, AppointmentStatus.Confirmed, AppointmentStatus.NoShow, null, BookingEvent.NoShow, "booking.appointment.no_show", now, scope, null, token);
        }, ct);

    /// <summary>
    /// Shared prologue: load (route clinic), authorize, then transaction with branch → appointment locks and
    /// a fresh read of the row. A concurrent unlink of the doctor (23503 on the composite FKs) maps to 422.
    /// </summary>
    private async Task<Result<AppointmentDto>> RunAsync(
        Guid clinicId,
        Guid appointmentId,
        string permission,
        Func<Appointment, DateTimeOffset, AccessScope, CancellationToken, Task<Result<AppointmentDto>>> action,
        CancellationToken ct)
    {
        var target = await db.Appointments.AsNoTracking().Where(a => a.Id == appointmentId && a.ClinicId == clinicId)
            .Select(a => new { a.Id, a.ClinicId, a.BranchId, a.DoctorId }).FirstOrDefaultAsync(ct);
        if (target is null)
        {
            return CommonErrors.NotFound;
        }

        var scope = await access.GetScopeAsync(ct);
        var check = scope.Check(permission, target.ClinicId, target.BranchId);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        if (scope.IsOwnOnly(permission, target.ClinicId) && (target.DoctorId is null || target.DoctorId != scope.DoctorIdFor(target.ClinicId)))
        {
            return CommonErrors.NotFound;
        }

        try
        {
            return await transactions.RunAsync<AppointmentDto>(async token =>
            {
                await locks.LockBranchAsync(target.BranchId, token);
                await locks.LockAsync<Appointment>(target.Id, token);
                var current = await db.Appointments.AsNoTracking().FirstAsync(a => a.Id == target.Id, token);

                // Re-check doctor-own under the lock (review R4): a reschedule committed between the
                // pre-check and the lock may have reassigned the appointment to another doctor.
                if (scope.IsOwnOnly(permission, current.ClinicId) && (current.DoctorId is null || current.DoctorId != scope.DoctorIdFor(current.ClinicId)))
                {
                    return CommonErrors.NotFound;
                }

                var now = await clock.NowAsync(token);
                return await action(current, now, scope, token);
            }, ct);
        }
        catch (Exception ex) when (dbErrors.Classify(ex) is { Kind: DbErrorKind.ForeignKeyViolation })
        {
            return BookingErrors.DoctorNotEligible;
        }
    }

    private async Task<bool> ConditionalAsync(Guid id, AppointmentStatus expected, AppointmentStatus next, string? reasonCode, DateTimeOffset now, CancellationToken ct) =>
        await db.Appointments
            .Where(x => x.Id == id && x.Status == expected)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, next)
                .SetProperty(x => x.ReasonCode, reasonCode)
                .SetProperty(x => x.UpdatedAt, now)
                .SetProperty(x => x.StatusChangedAt, now), ct) == 1;

    /// <summary>Moves the appointment's block row (exclusion constraint re-checks); inserts one only if the appointment has none.</summary>
    private async Task<bool> MoveOrInsertBlockAsync(Appointment a, Guid doctorId, DateTimeOffset start, DateTimeOffset end, DateTimeOffset now, CancellationToken ct)
    {
        var moved = await store.TryMoveBlockAsync(a.Id, doctorId, start, end, TimeBlockKind.Appointment, now, ct);
        return moved switch
        {
            BlockMoveResult.Moved => true,
            BlockMoveResult.Conflict => false,
            _ => await store.TryInsertBlockAsync(DoctorTimeBlock.ForAppointment(a.ClinicId, a.BranchId, doctorId, a.Id, start, end, TimeBlockKind.Appointment, now), ct),
        };
    }

    private async Task<Result<AppointmentDto>> FinishAsync(
        Appointment a,
        AppointmentStatus from,
        AppointmentStatus to,
        string? reasonCode,
        BookingEvent evt,
        string auditAction,
        DateTimeOffset now,
        AccessScope scope,
        object? details,
        CancellationToken ct,
        DateTimeOffset? previousStartsAt = null)
    {
        db.AppointmentStatusHistory.Add(AppointmentStatusHistory.Create(a.ClinicId, a.Id, from, to, HistoryActorKind.Staff, currentUser.RequiredUserId, reasonCode, now));
        await notifier.NotifyAsync([a.Id], evt, now, ct, previousStartsAt);
        audit.Record(new AuditEvent(auditAction, a.ClinicId, a.BranchId, "appointment", a.Id.ToString(), details));
        await db.SaveChangesAsync(ct);
        return (await reader.GetAsync(a.Id, AppointmentViewerKind.Staff, scope, now, withHistory: true, ct))!;
    }
}
