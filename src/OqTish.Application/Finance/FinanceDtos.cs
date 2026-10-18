using OqTish.Domain.Common;
using OqTish.Domain.Finance;

namespace OqTish.Application.Finance;

/// <summary>
/// docs/API.md <c>PaymentDto</c>. Additive: <c>refunded</c> (Σ refunds of an incoming payment), <c>refundable</c>
/// (what can still be refunded), <c>reason</c> (refunds), <c>failureReason</c>, <c>failedAt</c>.
/// </summary>
public sealed record PaymentDto(
    Guid Id,
    Guid AppointmentId,
    Guid BranchId,
    PaymentDirection Direction,
    PaymentMethod Method,
    Money Amount,
    PaymentStatus Status,
    string? ExternalRef,
    Guid? RefundOfPaymentId,
    DateTimeOffset RecordedAt,
    string? ContactName,
    string? ServiceName,
    Money Refunded,
    Money Refundable,
    string? Reason,
    string? FailureReason,
    DateTimeOffset? FailedAt);

/// <summary>
/// <c>GET …/appointments/{id}/balance</c>. <c>charged</c> = amount entered at completion (0 before);
/// <c>paid</c> = recorded incoming payments; <c>refunded</c> = refunds; <c>outstanding</c> = the patient
/// receivable in the ledger (negative = patient credit, e.g. a prepayment before completion).
/// </summary>
public sealed record BalanceDto(Money Charged, Money Paid, Money Refunded, Money Outstanding);

/// <summary><c>status</c> (additive, optional): <c>Failed</c> records a declined attempt without any ledger effect (F10).</summary>
public sealed record RecordPaymentCommand(Money? Amount, PaymentMethod? Method, string? ExternalRef, PaymentStatus? Status);

public sealed record FailPaymentCommand(string? Reason);

public sealed record RefundCommand(Money? Amount, string? Reason);

/// <summary>Payments list filters; <c>from</c>/<c>to</c> are UTC instants, half-open [from, to) on <c>recordedAt</c>.</summary>
public sealed record PaymentListQuery(Guid? BranchId, Guid? AppointmentId, DateTimeOffset? From, DateTimeOffset? To, int? Page, int? PageSize);

/// <summary>One branch-local day; <c>branchId</c> null = all branches in the caller's scope. Additive: <c>priorPeriodRefunded</c>.</summary>
public sealed record DailyReportRowDto(string Date, Guid? BranchId, int Visits, Money Charged, Money Collected, Money Refunded, Money Commission, Money PriorPeriodRefunded);

/// <summary>
/// Monthly totals. <c>reconciled</c>: the stored daily summaries equal the ledger for every (branch, day) of the
/// month; <c>commissionRuleConfigured</c>: the clinic has a commission rule overlapping the month (F14 flag).
/// Additive: <c>branchId</c>, <c>priorPeriodRefunded</c>, <c>locked</c> (month locked by a finalized settlement).
/// </summary>
public sealed record MonthlyReportDto(
    int Year,
    int Month,
    Guid? BranchId,
    int Visits,
    Money Charged,
    Money Collected,
    Money Refunded,
    Money Commission,
    Money PriorPeriodRefunded,
    bool Reconciled,
    bool CommissionRuleConfigured,
    bool Locked);

/// <summary>Settlement as the UI expects (API.md §4/§5). Additive: <c>periodStart</c>, <c>periodEnd</c>, <c>itemCount</c>, <c>commissionRuleConfigured</c>, <c>paymentTerms</c>.</summary>
public sealed record SettlementDto(
    Guid Id,
    Guid ClinicId,
    string ClinicName,
    int Year,
    int Month,
    SettlementStatus Status,
    Money AmountDue,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinalizedAt,
    DateTimeOffset? PaidAt,
    string? PaidReference,
    string? VoidReason,
    string PeriodStart,
    string PeriodEnd,
    int ItemCount,
    bool CommissionRuleConfigured,
    string? PaymentTerms);

public sealed record CreateSettlementCommand(Guid? ClinicId, int? Year, int? Month, string? PaymentTerms);

public sealed record MarkSettlementPaidCommand(DateTimeOffset? PaidAt, string? Reference);

public sealed record SettlementReasonCommand(string? Reason);

/// <summary><c>rate</c> is a fraction string with 4 decimals ("0.0750" = 7.5 %), null for fixed-fee bases.</summary>
public sealed record CommissionRuleDto(
    Guid Id,
    Guid ClinicId,
    CommissionBasis Basis,
    string? Rate,
    Money? FixedFee,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string ContractReference,
    CommissionRounding Rounding,
    Guid ApprovedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EndedAt);

public sealed record CreateCommissionRuleCommand(CommissionBasis? Basis, string? Rate, Money? FixedFee, DateTimeOffset? EffectiveFrom, DateTimeOffset? EffectiveTo, string? ContractReference);

public sealed record EndCommissionRuleCommand(DateTimeOffset? EffectiveTo);
