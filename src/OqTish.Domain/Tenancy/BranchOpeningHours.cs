using OqTish.Domain.Common;

namespace OqTish.Domain.Tenancy;

/// <summary>One opening interval of a branch on an ISO weekday (1 = Monday), in branch-local time.</summary>
public sealed class BranchOpeningHours
{
    private BranchOpeningHours()
    {
    }

    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    public Guid BranchId { get; private set; }

    public short Weekday { get; private set; }

    public TimeOnly Opens { get; private set; }

    public TimeOnly Closes { get; private set; }

    public static BranchOpeningHours Create(Guid clinicId, Guid branchId, short weekday, TimeOnly opens, TimeOnly closes)
    {
        if (weekday is < 1 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(weekday), "ISO weekday must be 1..7.");
        }

        if (opens >= closes)
        {
            throw new ArgumentException("Opening time must be before closing time.", nameof(opens));
        }

        return new BranchOpeningHours
        {
            Id = IdGenerator.New(),
            ClinicId = clinicId,
            BranchId = branchId,
            Weekday = weekday,
            Opens = opens,
            Closes = closes,
        };
    }
}
