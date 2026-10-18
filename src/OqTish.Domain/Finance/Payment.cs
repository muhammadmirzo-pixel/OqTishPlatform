using OqTish.Domain.Common;

namespace OqTish.Domain.Finance;

/// <summary>
/// Money that moved for one appointment (docs/FINANCIAL_RULES.md §1): collected from the patient
/// (<see cref="PaymentDirection.In"/>) or returned (<see cref="PaymentDirection.Refund"/>, linked to the
/// original by <see cref="RefundOfPaymentId"/>). Amounts are always positive; the direction gives the sign
/// in the ledger. Only the status of an <c>In</c> payment can change (Recorded → Failed); the ledger keeps
/// the history through reversal entries. <see cref="IdempotencyKey"/> is UNIQUE per clinic.
/// </summary>
public sealed class Payment
{
    public const int MaxExternalRefLength = 100;
    public const int MaxReasonLength = 500;

    private Payment()
    {
    }

    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    public Guid BranchId { get; private set; }

    public Guid AppointmentId { get; private set; }

    public PaymentDirection Direction { get; private set; }

    public PaymentMethod Method { get; private set; }

    /// <summary>Always &gt; 0, numeric(19,2).</summary>
    public decimal Amount { get; private set; }

    public Currency Currency { get; private set; }

    public PaymentStatus Status { get; private set; }

    public CollectedBy CollectedBy { get; private set; }

    public Guid? RefundOfPaymentId { get; private set; }

    public string? IdempotencyKey { get; private set; }

    public string? ExternalRef { get; private set; }

    /// <summary>Refund reason (refunds only).</summary>
    public string? Reason { get; private set; }

    public Guid RecordedByUserId { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public string? FailureReason { get; private set; }

    public Guid? FailedByUserId { get; private set; }

    public DateTimeOffset? FailedAt { get; private set; }

    public Money AmountMoney => new(Amount, Currency);

    public static Payment RecordIn(
        Guid clinicId,
        Guid branchId,
        Guid appointmentId,
        Money amount,
        PaymentMethod method,
        PaymentStatus status,
        string? idempotencyKey,
        string? externalRef,
        Guid recordedByUserId,
        DateTimeOffset recordedAt)
    {
        EnsurePositive(amount);
        var p = new Payment
        {
            Id = IdGenerator.New(),
            ClinicId = clinicId,
            BranchId = branchId,
            AppointmentId = appointmentId,
            Direction = PaymentDirection.In,
            Method = method,
            Amount = amount.Amount,
            Currency = amount.Currency,
            Status = status,
            CollectedBy = CollectedBy.Clinic,
            IdempotencyKey = idempotencyKey,
            ExternalRef = externalRef,
            RecordedByUserId = recordedByUserId,
            RecordedAt = recordedAt,
        };
        if (status == PaymentStatus.Failed)
        {
            p.FailedAt = recordedAt;
            p.FailedByUserId = recordedByUserId;
        }

        return p;
    }

    public static Payment RecordRefund(Payment original, Money amount, string reason, string? idempotencyKey, Guid recordedByUserId, DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        EnsurePositive(amount);
        if (original.Direction != PaymentDirection.In || original.Status != PaymentStatus.Recorded)
        {
            throw new InvalidOperationException("Only recorded incoming payments can be refunded.");
        }

        return new Payment
        {
            Id = IdGenerator.New(),
            ClinicId = original.ClinicId,
            BranchId = original.BranchId,
            AppointmentId = original.AppointmentId,
            Direction = PaymentDirection.Refund,
            Method = original.Method,
            Amount = amount.Amount,
            Currency = amount.Currency,
            Status = PaymentStatus.Recorded,
            CollectedBy = original.CollectedBy,
            RefundOfPaymentId = original.Id,
            IdempotencyKey = idempotencyKey,
            Reason = reason,
            RecordedByUserId = recordedByUserId,
            RecordedAt = recordedAt,
        };
    }

    /// <summary>Recorded incoming payment → Failed (the caller posts the reversal entries).</summary>
    public void MarkFailed(string reason, Guid userId, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Direction != PaymentDirection.In || Status != PaymentStatus.Recorded)
        {
            throw new InvalidOperationException("Only recorded incoming payments can be marked failed.");
        }

        Status = PaymentStatus.Failed;
        FailureReason = reason;
        FailedByUserId = userId;
        FailedAt = at;
    }

    private static void EnsurePositive(Money amount)
    {
        if (!amount.IsPositive || amount.Round() != amount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Payment amounts are > 0 with at most 2 decimals.");
        }
    }
}

/// <summary>
/// Allocation of a payment to what it pays for. v1: one allocation per incoming payment, to its appointment
/// and — when the service is already completed — to its <see cref="ServiceCompletionId"/> (prepayments on a
/// Confirmed appointment have none). Refunds are not allocated (they reference the original payment).
/// </summary>
public sealed class PaymentAllocation
{
    private PaymentAllocation()
    {
    }

    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    public Guid PaymentId { get; private set; }

    public Guid AppointmentId { get; private set; }

    public Guid? ServiceCompletionId { get; private set; }

    public decimal Amount { get; private set; }

    public Currency Currency { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static PaymentAllocation For(Payment payment, Guid? serviceCompletionId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(payment);
        return new PaymentAllocation
        {
            Id = IdGenerator.New(),
            ClinicId = payment.ClinicId,
            PaymentId = payment.Id,
            AppointmentId = payment.AppointmentId,
            ServiceCompletionId = serviceCompletionId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            CreatedAt = now,
        };
    }
}

/// <summary>
/// What was delivered and at what charged amount (one per appointment). Created by the completion hook in the
/// completion transaction; never changed afterwards.
/// </summary>
public sealed class ServiceCompletion
{
    private ServiceCompletion()
    {
    }

    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    public Guid BranchId { get; private set; }

    public Guid AppointmentId { get; private set; }

    public Guid ServiceId { get; private set; }

    public Guid DoctorId { get; private set; }

    /// <summary>The patient's account (null for walk-ins without an account).</summary>
    public Guid? PatientUserId { get; private set; }

    public decimal ChargedAmount { get; private set; }

    public Currency Currency { get; private set; }

    public Guid CompletedByUserId { get; private set; }

    public DateTimeOffset CompletedAt { get; private set; }

    /// <summary>Branch-local reporting day of the completion (after period-lock redirection).</summary>
    public DateOnly PostingDateLocal { get; private set; }

    public Money ChargedMoney => new(ChargedAmount, Currency);

    public static ServiceCompletion Create(
        Guid clinicId,
        Guid branchId,
        Guid appointmentId,
        Guid serviceId,
        Guid doctorId,
        Guid? patientUserId,
        Money charged,
        Guid completedByUserId,
        DateTimeOffset completedAt,
        DateOnly postingDateLocal)
    {
        if (charged.IsNegative || charged.Round() != charged)
        {
            throw new ArgumentOutOfRangeException(nameof(charged), "The charged amount is ≥ 0 with at most 2 decimals.");
        }

        return new ServiceCompletion
        {
            Id = IdGenerator.New(),
            ClinicId = clinicId,
            BranchId = branchId,
            AppointmentId = appointmentId,
            ServiceId = serviceId,
            DoctorId = doctorId,
            PatientUserId = patientUserId,
            ChargedAmount = charged.Amount,
            Currency = charged.Currency,
            CompletedByUserId = completedByUserId,
            CompletedAt = completedAt,
            PostingDateLocal = postingDateLocal,
        };
    }
}
