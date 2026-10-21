using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Staff;

namespace OqTish.Application.Scheduling;

/// <summary><see cref="AvatarId"/> = the avatar of the linked user (<see cref="UserId"/>), null when unlinked or none.</summary>
public sealed record DoctorDto(Guid Id, string DisplayName, bool IsActive, IReadOnlyList<Guid> BranchIds, IReadOnlyList<Guid> ServiceIds, Guid? UserId, Guid? AvatarId);

public sealed record DoctorCommand(string? DisplayName, bool? IsActive, IReadOnlyList<Guid>? BranchIds, IReadOnlyList<Guid>? ServiceIds, Guid? UserId);

/// <summary>One weekly interval, branch-local <c>HH:mm</c> [start, end).</summary>
public sealed record WorkingHoursEntryDto(Guid BranchId, int Weekday, string Start, string End);

/// <summary>Scheduling error codes (docs/API.md §9).</summary>
public static class ScheduleErrors
{
    public static readonly Error HoursOverlap = Error.Rule("schedule.hours_overlap", "Working-hour intervals of the doctor overlap on the same weekday (across branches).");

    public static readonly Error BranchNotLinked = Error.Rule("schedule.branch_not_linked", "The doctor is not linked to this branch.");

    public static readonly Error DoctorUserInvalid = Error.Rule("schedule.doctor_user_invalid", "The user must be an active member of this clinic.");

    public static readonly Error LinkInUse = Error.Conflict("schedule.link_in_use", "A removed branch/service link is referenced by appointments or time blocks; deactivate the doctor instead.");

    public static readonly Error DoctorUserTaken = Error.Conflict("schedule.doctor_user_taken", "This user is already linked to another doctor of the clinic.");
}

/// <summary>
/// Doctor-own semantics (replaces the BE1a placeholder): the caller's doctor record per clinic is the
/// <c>scheduling.doctors</c> row with <c>user_id</c> = caller (at most one per clinic, unique index).
/// </summary>
internal sealed class DoctorIdentityResolver(IOqTishDb db) : IDoctorIdentityResolver
{
    public async Task<IReadOnlyDictionary<Guid, Guid>> GetDoctorIdsAsync(Guid userId, IReadOnlyCollection<Guid> clinicIds, CancellationToken cancellationToken)
    {
        var ids = clinicIds.ToList();
        return await db.Doctors.AsNoTracking()
            .Where(d => d.UserId == userId && ids.Contains(d.ClinicId))
            .ToDictionaryAsync(d => d.ClinicId, d => d.Id, cancellationToken);
    }
}
