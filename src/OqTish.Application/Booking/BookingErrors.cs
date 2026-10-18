using OqTish.Application.Common;

namespace OqTish.Application.Booking;

/// <summary>Booking error codes (docs/API.md §9).</summary>
public static class BookingErrors
{
    public static readonly Error SlotUnavailable = Error.Conflict("booking.slot_unavailable", "No eligible doctor is free for that interval.");

    public static readonly Error OutsideHours = Error.Rule("booking.outside_hours", "The interval is not within working hours.");

    public static readonly Error LeadTime = Error.Rule("booking.lead_time", "The start time is too soon or beyond the booking horizon.");

    public static readonly Error TooManyPending = Error.Rule("booking.too_many_pending", "The patient already has the maximum number of pending requests.");

    public static readonly Error ClinicUnavailable = Error.Rule("booking.clinic_unavailable", "The clinic, branch or service is not available for booking.");

    public static readonly Error NotPending = Error.Conflict("booking.not_pending", "The request is no longer pending.");

    public static readonly Error Expired = Error.Conflict("booking.expired", "The confirmation deadline has passed.");

    public static readonly Error DoctorNotEligible = Error.Rule("booking.doctor_not_eligible", "The doctor does not provide this service at this branch.");

    public static readonly Error DoctorBusy = Error.Conflict("booking.doctor_busy", "The doctor is busy during that interval.");

    public static readonly Error InvalidTransition = Error.Conflict("booking.invalid_transition", "The appointment's status does not allow this action.");

    public static readonly Error CancelTooLate = Error.Rule("booking.cancel_too_late", "It is too late to cancel this appointment; contact the clinic.");

    public static readonly Error TooEarlyForOutcome = Error.Rule("booking.too_early_for_outcome", "The outcome cannot be recorded before the allowed time.");

    public static readonly Error TimeOffConflict = Error.Conflict("schedule.timeoff_conflict", "The time off overlaps appointments, holds or other time off of the doctor.");
}

/// <summary>
/// Reason codes for reject / clinic cancel (FRONTEND.md F5). Both the backend list of the brief and the
/// codes the UI sends are accepted (union), see DECISIONS.md BE2.
/// </summary>
public static class ReasonCodes
{
    public static readonly IReadOnlyList<string> Reject = ["NoDoctorAvailable", "TimeUnavailable", "ServiceUnavailable", "PatientUnreachable", "Other"];

    public static readonly IReadOnlyList<string> ClinicCancel = ["DoctorUnavailable", "ClinicClosed", "PatientRequest", "ClinicEmergency", "Other"];

    /// <summary>Used in history when the patient cancels.</summary>
    public const string PatientCancelled = "PatientCancelled";

    /// <summary>Used in history when the account was deleted (OD-18).</summary>
    public const string AccountDeleted = "AccountDeleted";

    public static bool IsValid(IReadOnlyList<string> allowed, string? code) => code is not null && allowed.Contains(code, StringComparer.Ordinal);
}
