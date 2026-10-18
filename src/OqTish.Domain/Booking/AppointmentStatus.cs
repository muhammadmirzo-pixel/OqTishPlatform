namespace OqTish.Domain.Booking;

/// <summary>Appointment lifecycle states (docs/API.md §0). Stored as text with a CHECK constraint.</summary>
public enum AppointmentStatus
{
    Pending = 1,
    Confirmed = 2,
    Rejected = 3,
    CancelledByPatient = 4,
    CancelledByClinic = 5,
    Expired = 6,
    Completed = 7,
    NoShow = 8,
}

/// <summary>How the appointment was created: a patient request, or a staff walk-in/phone booking (OD-08).</summary>
public enum AppointmentSource
{
    Patient = 1,
    WalkIn = 2,
}

/// <summary>Who caused a status transition (status history).</summary>
public enum HistoryActorKind
{
    Patient = 1,
    Staff = 2,
    System = 3,
}

/// <summary>Every action that changes an appointment. <see cref="AppointmentStateMachine"/> decides which are allowed.</summary>
public enum AppointmentAction
{
    Confirm = 1,
    Reject = 2,
    CancelByPatient = 3,
    CancelByClinic = 4,
    Reschedule = 5,
    Complete = 6,
    NoShow = 7,
    Expire = 8,
}
