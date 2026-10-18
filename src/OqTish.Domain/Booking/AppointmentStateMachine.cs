using System.Collections.Frozen;

namespace OqTish.Domain.Booking;

/// <summary>
/// The appointment state machine. The table below is the single source of truth; every write path
/// asks it first and then performs a conditional UPDATE on the expected status (so a concurrent change
/// makes the write affect 0 rows instead of silently overwriting).
/// <code>
/// Pending   --Confirm--------&gt; Confirmed
/// Pending   --Reject---------&gt; Rejected
/// Pending   --CancelByPatient-&gt; CancelledByPatient
/// Pending   --Expire---------&gt; Expired
/// Confirmed --CancelByPatient-&gt; CancelledByPatient   (until start − cutoff, checked by the handler)
/// Confirmed --CancelByClinic--&gt; CancelledByClinic
/// Confirmed --Reschedule-----&gt; Confirmed            (time/doctor change)
/// Confirmed --Complete-------&gt; Completed            (from start time)
/// Confirmed --NoShow---------&gt; NoShow               (from start + grace)
/// Rejected, CancelledByPatient, CancelledByClinic, Expired, Completed, NoShow: terminal.
/// </code>
/// Time rules (deadline, cutoffs) are not part of the table; handlers check them against database time.
/// </summary>
public static class AppointmentStateMachine
{
    private static readonly FrozenDictionary<(AppointmentStatus From, AppointmentAction Action), AppointmentStatus> Transitions =
        new Dictionary<(AppointmentStatus, AppointmentAction), AppointmentStatus>
        {
            [(AppointmentStatus.Pending, AppointmentAction.Confirm)] = AppointmentStatus.Confirmed,
            [(AppointmentStatus.Pending, AppointmentAction.Reject)] = AppointmentStatus.Rejected,
            [(AppointmentStatus.Pending, AppointmentAction.CancelByPatient)] = AppointmentStatus.CancelledByPatient,
            [(AppointmentStatus.Pending, AppointmentAction.Expire)] = AppointmentStatus.Expired,
            [(AppointmentStatus.Confirmed, AppointmentAction.CancelByPatient)] = AppointmentStatus.CancelledByPatient,
            [(AppointmentStatus.Confirmed, AppointmentAction.CancelByClinic)] = AppointmentStatus.CancelledByClinic,
            [(AppointmentStatus.Confirmed, AppointmentAction.Reschedule)] = AppointmentStatus.Confirmed,
            [(AppointmentStatus.Confirmed, AppointmentAction.Complete)] = AppointmentStatus.Completed,
            [(AppointmentStatus.Confirmed, AppointmentAction.NoShow)] = AppointmentStatus.NoShow,
        }.ToFrozenDictionary();

    /// <summary>Statuses whose doctor time is reserved (an active time block exists).</summary>
    public static readonly FrozenSet<AppointmentStatus> HoldsTime = new[] { AppointmentStatus.Pending, AppointmentStatus.Confirmed }.ToFrozenSet();

    /// <summary>Statuses that require an assigned doctor (mirrors the CHECK constraint).</summary>
    public static readonly FrozenSet<AppointmentStatus> RequiresDoctor =
        new[] { AppointmentStatus.Confirmed, AppointmentStatus.Completed, AppointmentStatus.NoShow }.ToFrozenSet();

    /// <summary>Every (status, action) pair that is allowed, for tests and documentation.</summary>
    public static IReadOnlyCollection<(AppointmentStatus From, AppointmentAction Action)> AllowedPairs => Transitions.Keys;

    public static bool IsTerminal(AppointmentStatus status) => !Transitions.Keys.Any(k => k.From == status);

    /// <summary>The status after <paramref name="action"/>, or false when the action is not allowed from <paramref name="from"/>.</summary>
    public static bool TryTransition(AppointmentStatus from, AppointmentAction action, out AppointmentStatus to) =>
        Transitions.TryGetValue((from, action), out to);

    public static bool CanApply(AppointmentStatus from, AppointmentAction action) => Transitions.ContainsKey((from, action));
}
