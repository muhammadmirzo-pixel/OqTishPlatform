using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Staff;
using OqTish.Domain.Scheduling;
using OqTish.Domain.Staff;

namespace OqTish.Application.Scheduling;

/// <summary>
/// Weekly working-hour templates of a doctor (branch-local wall-clock times, docs/API.md §4).
/// <list type="bullet">
/// <item>GET …/doctors/{doctorId}/working-hours — any membership; entries of branches outside a
/// Branches-scoped member's scope are not returned.</item>
/// <item>PUT … <c>[{ branchId, weekday, start, end }]</c> — replaces the entries of every branch on which the
/// caller holds <c>schedule.manage</c>; entries of other branches are kept untouched, and sending one for
/// such a branch → 403. Each branch must be linked to the doctor (422 <c>schedule.branch_not_linked</c>).
/// Intervals of the doctor on one weekday must not overlap across all branches (422
/// <c>schedule.hours_overlap</c>; the exclusion constraint <c>ex_working_hours_no_overlap</c> is the backstop).</item>
/// </list>
/// Side effects: audit <c>schedule.working_hours.updated</c>.
/// </summary>
public sealed class WorkingHoursHandlers(IOqTishDb db, IAccessService access, ITransactionRunner transactions, IRowLocks locks, IAuditLog audit, IDbErrorClassifier errors)
{
    public const int MaxEntries = 100;

    public async Task<Result<IReadOnlyList<WorkingHoursEntryDto>>> GetAsync(Guid clinicId, Guid doctorId, CancellationToken ct)
    {
        var doctor = await db.Doctors.AsNoTracking().FirstOrDefaultAsync(d => d.Id == doctorId && d.ClinicId == clinicId, ct);
        if (doctor is null)
        {
            return CommonErrors.NotFound;
        }

        var scope = await access.GetScopeAsync(ct);
        var check = scope.CheckMember(doctor.ClinicId);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var visible = scope.MembershipIn(doctor.ClinicId)!.AccessibleBranchIds;
        var rows = await db.WorkingHours.AsNoTracking()
            .Where(w => w.DoctorId == doctor.Id && visible.Contains(w.BranchId))
            .OrderBy(w => w.Weekday).ThenBy(w => w.StartLocal).ThenBy(w => w.BranchId)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<Result> SetAsync(Guid clinicId, Guid doctorId, IReadOnlyList<WorkingHoursEntryDto>? entries, CancellationToken ct)
    {
        var doctor = await db.Doctors.AsNoTracking().FirstOrDefaultAsync(d => d.Id == doctorId && d.ClinicId == clinicId, ct);
        if (doctor is null)
        {
            return CommonErrors.NotFound;
        }

        var scope = await access.GetScopeAsync(ct);
        var any = scope.Check(Permissions.ScheduleManage, doctor.ClinicId);
        if (!any.IsSuccess)
        {
            return any;
        }

        var validation = new FieldErrors();
        var parsed = Parse(entries, validation);
        if (parsed is null)
        {
            return validation.ToError();
        }

        if (parsed.Select(p => p.BranchId).Distinct().Select(b => scope.Check(Permissions.ScheduleManage, doctor.ClinicId, b)).FirstOrDefault(r => !r.IsSuccess, Result.Success) is { IsSuccess: false } denied)
        {
            return denied;
        }

        var manageable = scope.BranchIdsWith(Permissions.ScheduleManage, doctor.ClinicId);
        try
        {
            return await transactions.RunAsync(async token =>
            {
                await locks.AdvisoryLockAsync("working-hours:" + doctor.Id, token);
                var linked = await db.DoctorBranches.Where(x => x.DoctorId == doctor.Id).Select(x => x.BranchId).ToListAsync(token);
                if (parsed.Any(p => !linked.Contains(p.BranchId)))
                {
                    return ScheduleErrors.BranchNotLinked;
                }

                var kept = await db.WorkingHours.AsNoTracking().Where(w => w.DoctorId == doctor.Id && !manageable.Contains(w.BranchId)).ToListAsync(token);
                var all = kept.Select(k => (k.Weekday, k.StartLocal, k.EndLocal)).Concat(parsed.Select(p => (p.Weekday, p.Start, p.End)));
                if (Overlaps(all))
                {
                    return ScheduleErrors.HoursOverlap;
                }

                await db.WorkingHours.Where(w => w.DoctorId == doctor.Id && manageable.Contains(w.BranchId)).ExecuteDeleteAsync(token);
                foreach (var p in parsed)
                {
                    db.WorkingHours.Add(Domain.Scheduling.WorkingHours.Create(doctor.ClinicId, doctor.Id, p.BranchId, p.Weekday, p.Start, p.End));
                }

                audit.Record(new AuditEvent("schedule.working_hours.updated", doctor.ClinicId, TargetType: "doctor", TargetId: doctor.Id.ToString(),
                    Details: new { entries = parsed.Count, branchIds = parsed.Select(p => p.BranchId).Distinct() }));
                await db.SaveChangesAsync(token);
                return Result.Success;
            }, ct);
        }
        catch (DbUpdateException ex) when (errors.Classify(ex) is { Kind: DbErrorKind.ExclusionViolation })
        {
            return ScheduleErrors.HoursOverlap;
        }
    }

    private static bool Overlaps(IEnumerable<(short Weekday, TimeOnly Start, TimeOnly End)> intervals)
    {
        foreach (var day in intervals.GroupBy(i => i.Weekday))
        {
            var ordered = day.OrderBy(i => i.Start).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].Start < ordered[i - 1].End)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static List<(Guid BranchId, short Weekday, TimeOnly Start, TimeOnly End)>? Parse(IReadOnlyList<WorkingHoursEntryDto>? entries, FieldErrors validation)
    {
        if (entries is null || entries.Count > MaxEntries)
        {
            validation.Add("entries", $"A JSON array of at most {MaxEntries} entries is required.");
            return null;
        }

        var result = new List<(Guid, short, TimeOnly, TimeOnly)>();
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e is null)
            {
                validation.Add($"[{i}]", "Required.");
                continue;
            }

            var start = validation.Time(e.Start, $"[{i}].start");
            var end = validation.Time(e.End, $"[{i}].end");
            if (e.Weekday is < 1 or > 7)
            {
                validation.Add($"[{i}].weekday", "ISO weekday 1..7 (1 = Monday).");
            }
            else if (e.BranchId == Guid.Empty)
            {
                validation.Add($"[{i}].branchId", "Required.");
            }
            else if (start is { } s && end is { } en)
            {
                if (s >= en)
                {
                    validation.Add($"[{i}]", "Start must be before end.");
                }
                else
                {
                    result.Add((e.BranchId, (short)e.Weekday, s, en));
                }
            }
        }

        return validation.HasErrors ? null : result;
    }

    private static WorkingHoursEntryDto ToDto(WorkingHours w) =>
        new(w.BranchId, w.Weekday, LocalFormats.Time(w.StartLocal), LocalFormats.Time(w.EndLocal));
}
