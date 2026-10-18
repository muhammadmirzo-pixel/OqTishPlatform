using OqTish.Domain.Common;

namespace OqTish.Domain.Scheduling;

/// <summary>
/// One weekly interval of a doctor at a branch, on an ISO weekday (1 = Monday), in branch-local wall-clock
/// time [start, end). FK (clinic_id, doctor_id, branch_id) → doctor_branches. Intervals of one doctor on the
/// same weekday never overlap, across all branches (handler check + exclusion constraint).
/// </summary>
public sealed class WorkingHours
{
    private WorkingHours()
    {
    }

    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    public Guid DoctorId { get; private set; }

    public Guid BranchId { get; private set; }

    public short Weekday { get; private set; }

    public TimeOnly StartLocal { get; private set; }

    public TimeOnly EndLocal { get; private set; }

    public static WorkingHours Create(Guid clinicId, Guid doctorId, Guid branchId, short weekday, TimeOnly start, TimeOnly end)
    {
        if (weekday is < 1 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(weekday), "ISO weekday must be 1..7.");
        }

        if (start >= end)
        {
            throw new ArgumentException("Start must be before end.", nameof(start));
        }

        return new WorkingHours
        {
            Id = IdGenerator.New(),
            ClinicId = clinicId,
            DoctorId = doctorId,
            BranchId = branchId,
            Weekday = weekday,
            StartLocal = start,
            EndLocal = end,
        };
    }
}
