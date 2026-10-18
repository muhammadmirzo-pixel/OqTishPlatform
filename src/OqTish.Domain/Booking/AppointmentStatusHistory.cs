using OqTish.Domain.Common;

namespace OqTish.Domain.Booking;

/// <summary>
/// One status transition of an appointment (append-only: <c>REVOKE UPDATE, DELETE</c> plus a trigger).
/// <see cref="From"/> is null for the creation row. A reschedule is recorded as Confirmed → Confirmed with
/// reason code <c>Rescheduled</c>.
/// </summary>
public sealed class AppointmentStatusHistory
{
    public const string RescheduledReason = "Rescheduled";

    private AppointmentStatusHistory()
    {
    }

    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    public Guid AppointmentId { get; private set; }

    public AppointmentStatus? From { get; private set; }

    public AppointmentStatus To { get; private set; }

    public HistoryActorKind ActorKind { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public string? ReasonCode { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public static AppointmentStatusHistory Create(
        Guid clinicId,
        Guid appointmentId,
        AppointmentStatus? from,
        AppointmentStatus to,
        HistoryActorKind actorKind,
        Guid? actorUserId,
        string? reasonCode,
        DateTimeOffset occurredAt) => new()
        {
            Id = IdGenerator.New(),
            ClinicId = clinicId,
            AppointmentId = appointmentId,
            From = from,
            To = to,
            ActorKind = actorKind,
            ActorUserId = actorUserId,
            ReasonCode = reasonCode,
            OccurredAt = occurredAt,
        };
}
