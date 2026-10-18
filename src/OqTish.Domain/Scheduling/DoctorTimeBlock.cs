using OqTish.Domain.Common;

namespace OqTish.Domain.Scheduling;

/// <summary>What a busy period of a doctor is.</summary>
public enum TimeBlockKind
{
    /// <summary>Provisional hold for a Pending request (OD-01).</summary>
    Hold = 1,

    /// <summary>A confirmed appointment.</summary>
    Appointment = 2,

    /// <summary>Doctor time off (no appointment, no branch: the doctor is away everywhere).</summary>
    TimeOff = 3,
}

/// <summary>
/// A busy period of a doctor over [<see cref="StartsAt"/>, <see cref="EndsAt"/>). The database derives
/// <c>period tstzrange '[)'</c> from the two columns and enforces
/// <c>EXCLUDE USING gist (doctor_id WITH =, period WITH &amp;&amp;) WHERE (is_active)</c> — the guarantee that a
/// doctor is never double-booked (ADR-004). Rows are deactivated, never deleted, when released.
/// Hold/Appointment rows reference their appointment (<c>appointment_id</c> UNIQUE) and branch
/// (FK → doctor_branches); TimeOff rows have neither.
/// </summary>
public sealed class DoctorTimeBlock
{
    public const int MaxReasonLength = 200;

    private DoctorTimeBlock()
    {
    }

    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    /// <summary>Null for <see cref="TimeBlockKind.TimeOff"/>.</summary>
    public Guid? BranchId { get; private set; }

    public Guid DoctorId { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public TimeBlockKind Kind { get; private set; }

    public Guid? AppointmentId { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>Optional staff note for time off (never shown publicly).</summary>
    public string? Reason { get; private set; }

    public Guid? CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static DoctorTimeBlock ForAppointment(Guid clinicId, Guid branchId, Guid doctorId, Guid appointmentId, DateTimeOffset startsAt, DateTimeOffset endsAt, TimeBlockKind kind, DateTimeOffset now)
    {
        if (kind == TimeBlockKind.TimeOff)
        {
            throw new ArgumentException("Use TimeOff() for time off.", nameof(kind));
        }

        return Create(clinicId, branchId, doctorId, appointmentId, startsAt, endsAt, kind, null, null, now);
    }

    public static DoctorTimeBlock TimeOff(Guid clinicId, Guid doctorId, DateTimeOffset startsAt, DateTimeOffset endsAt, string? reason, Guid? createdByUserId, DateTimeOffset now) =>
        Create(clinicId, null, doctorId, null, startsAt, endsAt, TimeBlockKind.TimeOff, reason, createdByUserId, now);

    private static DoctorTimeBlock Create(Guid clinicId, Guid? branchId, Guid doctorId, Guid? appointmentId, DateTimeOffset startsAt, DateTimeOffset endsAt, TimeBlockKind kind, string? reason, Guid? createdBy, DateTimeOffset now)
    {
        if (endsAt <= startsAt)
        {
            throw new ArgumentException("End must be after start.", nameof(endsAt));
        }

        return new DoctorTimeBlock
        {
            Id = IdGenerator.New(),
            ClinicId = clinicId,
            BranchId = branchId,
            DoctorId = doctorId,
            AppointmentId = appointmentId,
            StartsAt = startsAt.ToUniversalTime(),
            EndsAt = endsAt.ToUniversalTime(),
            Kind = kind,
            IsActive = true,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            CreatedByUserId = createdBy,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
