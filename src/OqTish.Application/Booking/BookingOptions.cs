namespace OqTish.Application.Booking;

/// <summary>Booking rules (configuration section <c>Booking</c>; DECISIONS.md OD-03/04/07, BE2).</summary>
public sealed class BookingOptions
{
    public const string SectionName = "Booking";

    /// <summary>A Pending request expires this long after creation (database time, 24/7, OD-03).</summary>
    public int PendingTtlMinutes { get; set; } = 30;

    /// <summary>Patients may book no sooner than this before the start (OD-03).</summary>
    public int MinLeadMinutes { get; set; } = 60;

    /// <summary>Latest bookable branch-local date = today + HorizonDays (OD-03).</summary>
    public int HorizonDays { get; set; } = 30;

    /// <summary>Start times sit on this branch-local grid (minutes from local midnight, OD-03 / A4).</summary>
    public int SlotStepMinutes { get; set; } = 15;

    /// <summary>A patient may cancel a Confirmed appointment until this long before the start (OD-04).</summary>
    public int PatientCancelCutoffMinutes { get; set; } = 120;

    /// <summary>Active (Pending) requests per patient (OD-07).</summary>
    public int MaxPendingPerPatient { get; set; } = 3;

    /// <summary>No-show may be recorded from start + this grace period.</summary>
    public int NoShowGraceMinutes { get; set; } = 15;

    /// <summary>A review is possible for this many days after completion (A9).</summary>
    public int ReviewWindowDays { get; set; } = 30;

    /// <summary>Walk-ins may start at most this long in the past (a patient who is already in the chair).</summary>
    public int WalkInPastToleranceMinutes { get; set; } = 60;

    /// <summary>Worker expiry sweeper period.</summary>
    public int SweeperIntervalSeconds { get; set; } = 30;

    /// <summary>Rows claimed per sweeper transaction (<c>FOR UPDATE SKIP LOCKED</c>).</summary>
    public int SweeperBatchSize { get; set; } = 100;

    /// <summary>Horizon for <c>nextFreeSlot</c> in public search when <c>availableWithinDays</c> is not given (OD-17).</summary>
    public int SearchDefaultAvailabilityDays { get; set; } = 7;
}
