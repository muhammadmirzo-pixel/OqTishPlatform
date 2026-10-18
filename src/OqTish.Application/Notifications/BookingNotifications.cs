using OqTish.Domain.Booking;

namespace OqTish.Application.Notifications;

/// <summary>
/// Outbox message types written by Booking (BE2). BE4 delivers them (templates per type and language,
/// docs/SERVICES.md "Bildirishnoma kontrakti"). Staff types (<c>*.staff</c>) go to staff of the branch who
/// hold <c>appointment.triage</c> (requested/cancelled/expired); patient types to <c>PatientUserId</c>.
/// Payloads never contain patient phones or names (OD-10).
/// </summary>
public static class BookingNotificationTypes
{
    public const string RequestedStaff = "appointment.requested.staff";
    public const string RequestedPatient = "appointment.requested.patient";
    public const string ConfirmedPatient = "appointment.confirmed.patient";
    public const string RejectedPatient = "appointment.rejected.patient";
    public const string CancelledByPatientStaff = "appointment.cancelled_by_patient.staff";
    public const string CancelledByClinicPatient = "appointment.cancelled_by_clinic.patient";
    public const string RescheduledPatient = "appointment.rescheduled.patient";
    public const string ExpiredPatient = "appointment.expired.patient";
    public const string ExpiredStaff = "appointment.expired.staff";
    public const string CompletedPatient = "appointment.completed.patient";
    public const string ReminderPatient = "appointment.reminder.patient";

    public static readonly IReadOnlyList<string> All =
    [
        RequestedStaff, RequestedPatient, ConfirmedPatient, RejectedPatient, CancelledByPatientStaff, CancelledByClinicPatient,
        RescheduledPatient, ExpiredPatient, ExpiredStaff, CompletedPatient, ReminderPatient,
    ];

    /// <summary>Dedup key <c>appt:{id}:{event}:{audience}</c> (one message per appointment, event and audience).</summary>
    public static string DedupKey(Guid appointmentId, string evt, string audience) => $"appt:{appointmentId:N}:{evt}:{audience}";

    /// <summary>Reminder dedup key <c>appt:{id}:reminder:{kind}:{startsAt unix}</c>: one per (appointment, kind, start time).</summary>
    public static string ReminderDedupKey(Guid appointmentId, string kind, DateTimeOffset startsAt) =>
        $"{ReminderPrefix(appointmentId)}{kind}:{startsAt.ToUnixTimeSeconds()}";

    /// <summary>Prefix of every reminder of an appointment (used to cancel them).</summary>
    public static string ReminderPrefix(Guid appointmentId) => $"appt:{appointmentId:N}:reminder:";
}

/// <summary>
/// Payload of every <c>appointment.*</c> message except reminders. Display data is a snapshot taken in the
/// same transaction as the change. <see cref="RequestNumber"/> = last 8 hex digits of the id (shown to staff
/// instead of patient data).
/// </summary>
public sealed record AppointmentNotice(
    string Event,
    Guid AppointmentId,
    string RequestNumber,
    Guid ClinicId,
    Guid BranchId,
    Guid? PatientUserId,
    string ClinicName,
    string BranchName,
    string ServiceName,
    string TimeZone,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    AppointmentStatus Status,
    string? ReasonCode,
    Guid? DoctorId,
    string? DoctorName,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? PreviousStartsAt);

/// <summary>
/// Payload of <c>appointment.reminder.patient</c> (enqueued with <c>not_before</c> = start − 24 h / − 2 h on
/// confirm and reschedule). Contract for BE4: before sending, the dispatcher MUST re-read the appointment and
/// skip (mark Cancelled) when it is no longer Confirmed or its <c>starts_at</c> differs from <see cref="StartsAt"/>.
/// Booking also cancels pending reminders itself when an appointment leaves Confirmed or is rescheduled.
/// </summary>
public sealed record AppointmentReminder(
    Guid AppointmentId,
    Guid PatientUserId,
    string Kind,
    Guid ClinicId,
    Guid BranchId,
    string ClinicName,
    string BranchName,
    string ServiceName,
    string TimeZone,
    DateTimeOffset StartsAt,
    string? DoctorName);
