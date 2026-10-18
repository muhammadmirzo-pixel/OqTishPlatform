namespace OqTish.Domain.Finance;

/// <summary>Money flow direction of a payment row: money in from the patient, or a refund back to the patient.</summary>
public enum PaymentDirection
{
    In = 1,
    Refund = 2,
}

/// <summary>How the clinic collected the money (A7: staff record cash / terminal / transfer).</summary>
public enum PaymentMethod
{
    Cash = 1,
    Card = 2,
    Transfer = 3,
}

/// <summary>
/// <see cref="Recorded"/> posts ledger entries. <see cref="Failed"/> never has a ledger effect: either it was
/// recorded as failed (no entries at all, F10) or a recorded payment was marked failed later (its entries are
/// reversed by a reversal entry, F11).
/// </summary>
public enum PaymentStatus
{
    Recorded = 1,
    Failed = 2,
}

/// <summary>Who holds the money. v1: always the clinic (OD-F7 — the platform collects no patient money).</summary>
public enum CollectedBy
{
    Clinic = 1,
    Platform = 2,
}

/// <summary>Commission basis configured per clinic contract (OD-F1). There is no default.</summary>
public enum CommissionBasis
{
    /// <summary>Percentage of net collected payments (refunds reverse proportionally).</summary>
    CollectedPayment = 1,

    /// <summary>Percentage of the charged amount at completion (price concessions reverse proportionally).</summary>
    CompletedService = 2,

    /// <summary>Fixed fee per appointment confirmed by the clinic (never reversed).</summary>
    ConfirmedAppointment = 3,

    /// <summary>Fixed fee for a patient's first completed visit at the clinic (never reversed).</summary>
    NewPatient = 4,
}

/// <summary>Rounding of a computed commission line. v1 supports only 2 decimal places, midpoints away from zero (OD-F5).</summary>
public enum CommissionRounding
{
    AwayFromZero = 1,
}

/// <summary>
/// Ledger accounts (docs/FINANCIAL_RULES.md §3). Sign convention: debit = positive, credit = negative; every
/// journal entry sums to zero per currency.
/// </summary>
public enum LedgerAccount
{
    // Clinic operational book (kept by the platform for the clinic; the platform does not claim this money).
    PatientReceivable = 1,
    ClinicCollectedCash = 2,
    ClinicCollectedCard = 3,
    ClinicCollectedTransfer = 4,
    ClinicServiceRevenue = 5,
    ClinicRefundsPaid = 6,

    // Platform book.
    PlatformCommissionReceivable = 20,
    PlatformCommissionRevenue = 21,
    PlatformCollectedFunds = 22,
    PlatformPayableToClinic = 23,
    SettlementClearing = 24,
}

/// <summary>Whose position a ledger line describes.</summary>
public enum LedgerParty
{
    Clinic = 1,
    Patient = 2,
    Platform = 3,
}

/// <summary>Why a journal entry exists.</summary>
public enum JournalEntryType
{
    /// <summary>Revenue + receivable at service completion (accrual, OD-F4).</summary>
    ServiceCompleted = 1,

    /// <summary>Money collected from the patient.</summary>
    PaymentRecorded = 2,

    /// <summary>Money returned to the patient.</summary>
    PaymentRefunded = 3,

    /// <summary>Reversal of a payment marked failed (bounced card, entry mistake).</summary>
    PaymentReversed = 4,

    /// <summary>Commission accrued for the platform.</summary>
    Commission = 5,

    /// <summary>Commission reversed (refund, failed payment, price concession).</summary>
    CommissionReversal = 6,

    /// <summary>The clinic paid a settlement to the platform.</summary>
    SettlementPaid = 7,
}

/// <summary>Settlement lifecycle: Draft → Finalized → Paid, or Draft → Voided (FINANCIAL_RULES §6).</summary>
public enum SettlementStatus
{
    Draft = 1,
    Finalized = 2,
    Paid = 3,
    Voided = 4,
}

public static class LedgerAccounts
{
    /// <summary>The clinic's "collected" account for a payment method.</summary>
    public static LedgerAccount CollectedFor(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => LedgerAccount.ClinicCollectedCash,
        PaymentMethod.Card => LedgerAccount.ClinicCollectedCard,
        PaymentMethod.Transfer => LedgerAccount.ClinicCollectedTransfer,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown payment method."),
    };

    public static bool IsRateBasis(CommissionBasis basis) => basis is CommissionBasis.CollectedPayment or CommissionBasis.CompletedService;
}
