using OqTish.Application.Common;

namespace OqTish.Application.Finance;

/// <summary>Finance error codes (docs/API.md §9).</summary>
public static class FinanceErrors
{
    public const string AmountInvalidCode = "finance.amount_invalid";

    public static readonly Error RefundExceedsPaid = Error.Rule("finance.refund_exceeds_paid", "The refund is larger than the net amount paid with this payment.");

    public static readonly Error SettlementExists = Error.Conflict("finance.settlement_exists", "A settlement already exists for this clinic and month.");

    public static readonly Error RuleOverlap = Error.Conflict("finance.rule_overlap", "The commission rule overlaps an existing rule of the same clinic and basis.");

    /// <summary>A Finalized/Paid settlement locks its month (OD-F6); voiding it is refused.</summary>
    public static readonly Error PeriodLocked = Error.Conflict("finance.period_locked", "The month is locked by a finalized settlement.");

    public static readonly Error AppointmentNotPayable = Error.Rule("finance.appointment_not_payable", "Payments can be recorded only for Confirmed or Completed appointments.");

    public static readonly Error InvalidPaymentState = Error.Conflict("finance.invalid_payment_state", "The payment is not in a state that allows this action.");

    public static readonly Error InvalidSettlementState = Error.Conflict("finance.invalid_settlement_state", "The settlement is not in a state that allows this action.");

    public static Error AmountInvalid(string field, string message) =>
        Error.Validation(AmountInvalidCode, message, new Dictionary<string, string[]> { [field] = [message] });
}
