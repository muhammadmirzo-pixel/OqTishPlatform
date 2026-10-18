using OqTish.Domain.Catalog;
using OqTish.Domain.Common;

namespace OqTish.Domain.Booking;

/// <summary>
/// A booking at a branch for one service over [<see cref="StartsAt"/>, <see cref="EndsAt"/>) (UTC).
/// Status changes are never made through this object: handlers ask <see cref="AppointmentStateMachine"/>
/// and then run a conditional UPDATE on the expected status (with the deadline compared to database
/// time where it applies). The doctor's time is reserved by a <c>scheduling.doctor_time_blocks</c> row
/// (Hold while Pending — the provisional doctor, never shown to the patient — Appointment once confirmed).
/// Composite tenant FKs: (clinic, branch) → branches, (clinic, branch, service) → branch_services,
/// (clinic, branch, doctor) → doctor_branches, (clinic, doctor, service) → doctor_services.
/// </summary>
public sealed class Appointment
{
    public const int MaxContactNameLength = 100;
    public const int MaxReasonCodeLength = 32;
    public const string AnonymisedName = "—";

    private Appointment()
    {
        ContactName = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    public Guid BranchId { get; private set; }

    public Guid ServiceId { get; private set; }

    /// <summary>The requesting patient's account; null for walk-in/phone bookings (OD-08).</summary>
    public Guid? PatientUserId { get; private set; }

    /// <summary>Assigned doctor; null while Pending (the provisional hold lives in the time block only).</summary>
    public Guid? DoctorId { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public AppointmentStatus Status { get; private set; }

    /// <summary>Confirmation deadline (database time). Required while Pending.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>Price snapshot at booking time (not binding, OD-F8).</summary>
    public decimal QuotedPrice { get; private set; }

    public Currency Currency { get; private set; }

    public PriceKind PriceKind { get; private set; }

    /// <summary>Amount charged, entered at completion (BE3 posts it to the ledger).</summary>
    public decimal? ChargedAmount { get; private set; }

    public string ContactName { get; private set; }

    /// <summary>E.164; null after account anonymisation.</summary>
    public string? ContactPhone { get; private set; }

    public AppointmentSource Source { get; private set; }

    /// <summary>Reason code of the last reject/cancel/reschedule.</summary>
    public string? ReasonCode { get; private set; }

    public Guid? CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>When the current status was entered (for Completed: the completion time, used by the review window).</summary>
    public DateTimeOffset StatusChangedAt { get; private set; }

    /// <summary>PostgreSQL <c>xmin</c> concurrency token.</summary>
    public uint Version { get; private set; }

    public Money QuotedPriceMoney => new(QuotedPrice, Currency);

    public Money? ChargedMoney => ChargedAmount is { } c ? new Money(c, Currency) : null;

    public static Appointment CreatePending(
        Guid clinicId,
        Guid branchId,
        Guid serviceId,
        Guid patientUserId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        Money quotedPrice,
        PriceKind priceKind,
        string contactName,
        string contactPhone,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        var a = Create(clinicId, branchId, serviceId, startsAt, endsAt, quotedPrice, priceKind, contactName, contactPhone, now);
        if (expiresAt <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt), "The deadline must be in the future.");
        }

        a.PatientUserId = patientUserId;
        a.CreatedByUserId = patientUserId;
        a.Status = AppointmentStatus.Pending;
        a.Source = AppointmentSource.Patient;
        a.ExpiresAt = expiresAt;
        return a;
    }

    /// <summary>Walk-in / phone booking by staff: Confirmed immediately with the chosen doctor, no patient account (OD-08).</summary>
    public static Appointment CreateWalkIn(
        Guid clinicId,
        Guid branchId,
        Guid serviceId,
        Guid doctorId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        Money quotedPrice,
        PriceKind priceKind,
        string contactName,
        string contactPhone,
        Guid createdByUserId,
        DateTimeOffset now)
    {
        var a = Create(clinicId, branchId, serviceId, startsAt, endsAt, quotedPrice, priceKind, contactName, contactPhone, now);
        a.DoctorId = doctorId;
        a.CreatedByUserId = createdByUserId;
        a.Status = AppointmentStatus.Confirmed;
        a.Source = AppointmentSource.WalkIn;
        return a;
    }

    private static Appointment Create(
        Guid clinicId,
        Guid branchId,
        Guid serviceId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        Money quotedPrice,
        PriceKind priceKind,
        string contactName,
        string contactPhone,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contactName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contactPhone);
        if (endsAt <= startsAt)
        {
            throw new ArgumentException("End must be after start.", nameof(endsAt));
        }

        if (quotedPrice.IsNegative)
        {
            throw new ArgumentOutOfRangeException(nameof(quotedPrice), "Price must not be negative.");
        }

        return new Appointment
        {
            Id = IdGenerator.New(),
            ClinicId = clinicId,
            BranchId = branchId,
            ServiceId = serviceId,
            StartsAt = startsAt.ToUniversalTime(),
            EndsAt = endsAt.ToUniversalTime(),
            QuotedPrice = quotedPrice.Round().Amount,
            Currency = quotedPrice.Currency,
            PriceKind = priceKind,
            ContactName = contactName.Trim(),
            ContactPhone = contactPhone,
            CreatedAt = now,
            UpdatedAt = now,
            StatusChangedAt = now,
        };
    }
}
