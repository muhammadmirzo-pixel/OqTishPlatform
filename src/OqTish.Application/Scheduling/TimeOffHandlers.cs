using Microsoft.EntityFrameworkCore;
using OqTish.Application.Booking;
using OqTish.Application.Common;
using OqTish.Application.Staff;
using OqTish.Domain.Scheduling;
using OqTish.Domain.Staff;

namespace OqTish.Application.Scheduling;

/// <summary>
/// Doctor time off (docs/API.md §4, OD-19) = an active <c>TimeOff</c> block in <c>scheduling.doctor_time_blocks</c>
/// (no branch: the doctor is away everywhere).
/// <list type="bullet">
/// <item>Write (POST/DELETE): <c>schedule.manage</c> on <b>every</b> branch the doctor is linked to (like doctor
/// editing), or the Doctor role for its own record (<c>schedule.own_timeoff</c>).</item>
/// <item>Read (GET ?from&amp;to — UTC instants, overlap with [from, to); default: everything ending after now):
/// the writers above plus any member who can see the doctor (shares a branch) — the calendar shows time off anyway.</item>
/// <item>POST <c>{ startsAt, endsAt, reason? }</c>: end after start and in the future, ≤ 90 days long, start within a
/// year. Overdue holds of the doctor are expired first (sweep-on-write); an overlap with an active
/// Hold/Appointment/TimeOff is rejected by the exclusion constraint → 409 <c>schedule.timeoff_conflict</c>.</item>
/// <item>DELETE: deactivates the block (204); unknown/already removed → 404.</item>
/// </list>
/// Side effects: audit <c>schedule.timeoff.created</c> / <c>schedule.timeoff.deleted</c>.
/// </summary>
public sealed class TimeOffHandlers(
    IOqTishDb db,
    IAccessService access,
    ITransactionRunner transactions,
    IBookingStore store,
    IDbClock clock,
    ICurrentUser currentUser,
    IAuditLog audit,
    BookingSweep sweep)
{
    public const int MaxDays = 90;

    public async Task<Result<IReadOnlyList<TimeOffDto>>> ListAsync(Guid clinicId, Guid doctorId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var doctor = await db.Doctors.AsNoTracking().FirstOrDefaultAsync(d => d.Id == doctorId && d.ClinicId == clinicId, ct);
        if (doctor is null)
        {
            return CommonErrors.NotFound;
        }

        var scope = await access.GetScopeAsync(ct);
        var member = scope.CheckMember(clinicId);
        if (!member.IsSuccess)
        {
            return member.Error;
        }

        var m = scope.MembershipIn(clinicId)!;
        var own = scope.DoctorIdFor(clinicId) == doctorId;
        var shares = m.Scope == MembershipScope.Clinic || await db.DoctorBranches.AnyAsync(x => x.DoctorId == doctorId && m.GrantedBranchIds.Contains(x.BranchId), ct);
        if (!own && !shares)
        {
            return CommonErrors.Forbidden;
        }

        if (from is { } f && to is { } t && f >= t)
        {
            return Error.InvalidField("to", "Must be after from.");
        }

        var now = await clock.NowAsync(ct);
        var q = db.DoctorTimeBlocks.AsNoTracking().Where(b => b.DoctorId == doctorId && b.Kind == TimeBlockKind.TimeOff && b.IsActive);
        if (from is null && to is null)
        {
            q = q.Where(b => b.EndsAt > now);
        }

        if (from is { } fromUtc)
        {
            var fu = fromUtc.ToUniversalTime();
            q = q.Where(b => b.EndsAt > fu);
        }

        if (to is { } toUtc)
        {
            var tu = toUtc.ToUniversalTime();
            q = q.Where(b => b.StartsAt < tu);
        }

        return await q.OrderBy(b => b.StartsAt).ThenBy(b => b.Id).Take(500)
            .Select(b => new TimeOffDto(b.Id, b.StartsAt, b.EndsAt, b.Reason))
            .ToListAsync(ct);
    }

    public async Task<Result<TimeOffDto>> CreateAsync(Guid clinicId, Guid doctorId, TimeOffCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var auth = await AuthorizeWriteAsync(clinicId, doctorId, ct);
        if (!auth.IsSuccess)
        {
            return auth.Error;
        }

        var errors = new FieldErrors();
        if (command.StartsAt is null)
        {
            errors.Add("startsAt", "Required.");
        }

        if (command.EndsAt is null)
        {
            errors.Add("endsAt", "Required.");
        }

        var reason = errors.Text(command.Reason, "reason", DoctorTimeBlock.MaxReasonLength, required: false);
        if (command.StartsAt is { } s0 && command.EndsAt is { } e0)
        {
            if (e0 <= s0)
            {
                errors.Add("endsAt", "Must be after startsAt.");
            }
            else if (e0 - s0 > TimeSpan.FromDays(MaxDays))
            {
                errors.Add("endsAt", $"Time off may last at most {MaxDays} days.");
            }
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var startsAt = command.StartsAt!.Value.ToUniversalTime();
        var endsAt = command.EndsAt!.Value.ToUniversalTime();
        return await transactions.RunAsync<TimeOffDto>(async token =>
        {
            var now = await clock.NowAsync(token);
            if (endsAt <= now || startsAt > now.AddDays(365))
            {
                return Error.InvalidField("startsAt", "Time off must end in the future and start within a year.");
            }

            await sweep.ExpireOverdueAsync(null, [doctorId], skipLocked: false, int.MaxValue, token);
            var block = DoctorTimeBlock.TimeOff(clinicId, doctorId, startsAt, endsAt, reason, currentUser.RequiredUserId, now);
            if (!await store.TryInsertBlockAsync(block, token))
            {
                return BookingErrors.TimeOffConflict;
            }

            audit.Record(new AuditEvent("schedule.timeoff.created", clinicId, TargetType: "doctor", TargetId: doctorId.ToString(),
                Details: new { timeOffId = block.Id, startsAt, endsAt }));
            await db.SaveChangesAsync(token);
            return new TimeOffDto(block.Id, block.StartsAt, block.EndsAt, block.Reason);
        }, ct);
    }

    public async Task<Result> DeleteAsync(Guid clinicId, Guid doctorId, Guid timeOffId, CancellationToken ct)
    {
        var auth = await AuthorizeWriteAsync(clinicId, doctorId, ct);
        if (!auth.IsSuccess)
        {
            return auth.Error;
        }

        return await transactions.RunAsync(async token =>
        {
            var now = await clock.NowAsync(token);
            var rows = await db.DoctorTimeBlocks
                .Where(b => b.Id == timeOffId && b.DoctorId == doctorId && b.ClinicId == clinicId && b.Kind == TimeBlockKind.TimeOff && b.IsActive)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsActive, false).SetProperty(b => b.UpdatedAt, now), token);
            if (rows == 0)
            {
                return CommonErrors.NotFound;
            }

            audit.Record(new AuditEvent("schedule.timeoff.deleted", clinicId, TargetType: "doctor", TargetId: doctorId.ToString(), Details: new { timeOffId }));
            await db.SaveChangesAsync(token);
            return Result.Success;
        }, ct);
    }

    private async Task<Result> AuthorizeWriteAsync(Guid clinicId, Guid doctorId, CancellationToken ct)
    {
        var doctor = await db.Doctors.AsNoTracking().FirstOrDefaultAsync(d => d.Id == doctorId && d.ClinicId == clinicId, ct);
        if (doctor is null)
        {
            return CommonErrors.NotFound;
        }

        var scope = await access.GetScopeAsync(ct);
        if (scope.Check(Permissions.ScheduleOwnTimeOff, clinicId).IsSuccess && scope.DoctorIdFor(clinicId) == doctorId)
        {
            return Result.Success;
        }

        var branches = await db.DoctorBranches.AsNoTracking().Where(x => x.DoctorId == doctorId).Select(x => x.BranchId).ToListAsync(ct);
        return branches.Count == 0
            ? scope.Check(Permissions.ScheduleManage, clinicId)
            : branches.Select(b => scope.Check(Permissions.ScheduleManage, clinicId, b)).FirstOrDefault(r => !r.IsSuccess, Result.Success);
    }
}
