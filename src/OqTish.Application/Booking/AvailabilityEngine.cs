namespace OqTish.Application.Booking;

/// <summary>A branch-local wall-clock interval [Start, End) on one day (offsets from local midnight).</summary>
public readonly record struct LocalInterval(TimeSpan Start, TimeSpan End)
{
    public bool Covers(TimeSpan start, TimeSpan end) => Start <= start && end <= End;
}

/// <summary>A busy period of a doctor in UTC [Start, End). Overdue holds are never included (they are swept on write).</summary>
public readonly record struct BusyInterval(DateTimeOffset Start, DateTimeOffset End, bool IsTimeOff)
{
    public bool Overlaps(DateTimeOffset start, DateTimeOffset end) => Start < end && start < End;
}

/// <summary>Everything the engine needs about one eligible doctor on one local day.</summary>
public sealed record DoctorDay(Guid DoctorId, string Name, int ServiceCount, IReadOnlyList<LocalInterval> Hours, IReadOnlyList<BusyInterval> Busy)
{
    /// <summary>Working hours cover the local interval and no time off overlaps it ("the doctor works then").</summary>
    public bool Works(TimeSpan localStart, TimeSpan localEnd, DateTimeOffset start, DateTimeOffset end) =>
        Hours.Any(h => h.Covers(localStart, localEnd)) && !Busy.Any(b => b.IsTimeOff && b.Overlaps(start, end));

    /// <summary>No active block (hold, appointment, time off) overlaps the interval.</summary>
    public bool IsFree(DateTimeOffset start, DateTimeOffset end) => !Busy.Any(b => b.Overlaps(start, end));

    /// <summary>Holds/appointments of the doctor that day (the "least booked" tie-breaker).</summary>
    public int Load => Busy.Count(b => !b.IsTimeOff);
}

/// <summary>Input for one branch, one service, one branch-local date.</summary>
public sealed record AvailabilityDay(
    DateOnly Date,
    TimeZoneInfo Zone,
    int StepMinutes,
    int DurationMinutes,
    DateTimeOffset EarliestStart,
    IReadOnlyList<LocalInterval>? OpeningHours,
    IReadOnlyList<DoctorDay> Doctors);

/// <summary>
/// The availability algorithm (pure; docs/SERVICES.md "Availability"). A start time S on the grid is free iff
/// S ≥ now + lead, [S, S + duration) lies inside a branch opening interval of that weekday (when the branch
/// has opening hours configured at all), and at least one eligible doctor (active, linked to the branch,
/// provides the service) has a working-hours interval covering [S, S + duration) in branch time and no active
/// block overlapping it. Slots that would cross local midnight are never offered.
/// </summary>
public static class AvailabilityEngine
{
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    public static IReadOnlyList<DateTimeOffset> FreeStarts(AvailabilityDay day)
    {
        ArgumentNullException.ThrowIfNull(day);
        var result = new List<DateTimeOffset>();
        if (day.Doctors.Count == 0 || (day.OpeningHours is { Count: 0 }))
        {
            return result;
        }

        var duration = TimeSpan.FromMinutes(day.DurationMinutes);
        var step = TimeSpan.FromMinutes(day.StepMinutes);
        for (var local = TimeSpan.Zero; local + duration <= Day; local += step)
        {
            var localEnd = local + duration;
            if (day.OpeningHours is { } opening && !opening.Any(o => o.Covers(local, localEnd)))
            {
                continue;
            }

            if (BookingTime.ToUtc(day.Date, local, day.Zone) is not { } start || start < day.EarliestStart)
            {
                continue;
            }

            var end = start + duration;
            if (day.Doctors.Any(d => d.Works(local, localEnd, start, end) && d.IsFree(start, end)))
            {
                result.Add(start);
            }
        }

        return result;
    }

    /// <summary>
    /// Booking candidates for [start, end), in the order the booking transaction tries them: least flexible
    /// first (fewest services provided), then least booked that day, then doctor id (deterministic).
    /// A greedy heuristic: it reduces, but cannot eliminate, cases where an earlier hold blocks a later
    /// request that only one doctor could serve (TESTING.md B5).
    /// </summary>
    public static IReadOnlyList<DoctorDay> Candidates(IEnumerable<DoctorDay> working, DateTimeOffset start, DateTimeOffset end) =>
        [.. working.Where(d => d.IsFree(start, end)).OrderBy(d => d.ServiceCount).ThenBy(d => d.Load).ThenBy(d => d.DoctorId)];
}
