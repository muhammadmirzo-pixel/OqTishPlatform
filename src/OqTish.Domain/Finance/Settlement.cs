using OqTish.Domain.Common;

namespace OqTish.Domain.Finance;

/// <summary>
/// The monthly platform ↔ clinic obligation (FINANCIAL_RULES §6, OD-F10): the sum of the clinic's unsettled
/// commission receivable lines posted in [<see cref="PeriodStart"/>, <see cref="PeriodEnd"/>).
/// Draft → Finalized (locks the month, OD-F6) → Paid, or Draft → Voided (releases the items).
/// UNIQUE(clinic_id, period_start) WHERE status &lt;&gt; 'Voided'.
/// </summary>
public sealed class Settlement
{
    public const int MaxReferenceLength = 200;
    public const int MaxReasonLength = 500;
    public const int MaxPaymentTermsLength = 500;

    private Settlement()
    {
    }

    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    /// <summary>First day of the month (Asia/Tashkent calendar).</summary>
    public DateOnly PeriodStart { get; private set; }

    /// <summary>First day of the next month (exclusive).</summary>
    public DateOnly PeriodEnd { get; private set; }

    public SettlementStatus Status { get; private set; }

    /// <summary>Clinic owes the platform (negative = the platform owes the clinic, e.g. reversals exceeding accruals).</summary>
    public decimal AmountDue { get; private set; }

    public Currency Currency { get; private set; }

    /// <summary>Informational payment terms (OD-F10).</summary>
    public string? PaymentTerms { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? FinalizedAt { get; private set; }

    public Guid? FinalizedByUserId { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public string? PaidReference { get; private set; }

    public Guid? PaidRecordedByUserId { get; private set; }

    public DateTimeOffset? VoidedAt { get; private set; }

    public string? VoidReason { get; private set; }

    public Guid? VoidedByUserId { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Money AmountDueMoney => new(AmountDue, Currency);

    public int Year => PeriodStart.Year;

    public int Month => PeriodStart.Month;

    public static Settlement CreateDraft(Guid clinicId, int year, int month, Money amountDue, string? paymentTerms, Guid userId, DateTimeOffset now)
    {
        var start = new DateOnly(year, month, 1);
        return new Settlement
        {
            Id = IdGenerator.New(),
            ClinicId = clinicId,
            PeriodStart = start,
            PeriodEnd = start.AddMonths(1),
            Status = SettlementStatus.Draft,
            AmountDue = amountDue.Amount,
            Currency = amountDue.Currency,
            PaymentTerms = paymentTerms,
            CreatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void AddToAmount(Money amount)
    {
        EnsureStatus(SettlementStatus.Draft);
        AmountDue = (AmountDueMoney + amount).Amount;
    }

    public void Finalize(Guid userId, DateTimeOffset now)
    {
        EnsureStatus(SettlementStatus.Draft);
        Status = SettlementStatus.Finalized;
        FinalizedAt = now;
        FinalizedByUserId = userId;
        UpdatedAt = now;
    }

    public void MarkPaid(DateTimeOffset paidAt, string reference, Guid userId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        EnsureStatus(SettlementStatus.Finalized);
        Status = SettlementStatus.Paid;
        PaidAt = paidAt.ToUniversalTime();
        PaidReference = reference.Trim();
        PaidRecordedByUserId = userId;
        UpdatedAt = now;
    }

    public void Void(string reason, Guid userId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        EnsureStatus(SettlementStatus.Draft);
        Status = SettlementStatus.Voided;
        VoidReason = reason.Trim();
        VoidedAt = now;
        VoidedByUserId = userId;
        UpdatedAt = now;
    }

    /// <summary>Finalized and Paid settlements lock their month for the clinic (OD-F6).</summary>
    public bool LocksPeriod => Status is SettlementStatus.Finalized or SettlementStatus.Paid;

    private void EnsureStatus(SettlementStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"Settlement is {Status}, expected {expected}.");
        }
    }
}

/// <summary>A commission receivable line settled by a settlement. <c>ledger_line_id</c> is UNIQUE: a line is settled at most once.</summary>
public sealed class SettlementItem
{
    private SettlementItem()
    {
    }

    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    public Guid SettlementId { get; private set; }

    public Guid LedgerLineId { get; private set; }

    public decimal Amount { get; private set; }

    public Currency Currency { get; private set; }

    public static SettlementItem Create(Settlement settlement, Guid ledgerLineId, Money amount)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        return new SettlementItem
        {
            Id = IdGenerator.New(),
            ClinicId = settlement.ClinicId,
            SettlementId = settlement.Id,
            LedgerLineId = ledgerLineId,
            Amount = amount.Amount,
            Currency = amount.Currency,
        };
    }
}

/// <summary>
/// Rebuildable cache of one branch-local day (FINANCIAL_RULES §7): sums of the ledger for (branch, day). Never
/// the source of truth — the nightly job recomputes it from the ledger and overwrites it on a mismatch.
/// </summary>
public sealed class DailyFinancialSummary
{
    private DailyFinancialSummary()
    {
    }

    public Guid BranchId { get; private set; }

    public DateOnly LocalDate { get; private set; }

    public Guid ClinicId { get; private set; }

    public Currency Currency { get; private set; }

    public int Visits { get; private set; }

    public decimal Charged { get; private set; }

    public decimal Collected { get; private set; }

    public decimal Refunded { get; private set; }

    public decimal PriorPeriodRefunded { get; private set; }

    public decimal Commission { get; private set; }

    /// <summary>Highest journal entry <see cref="JournalEntry.Sequence"/> included (informational).</summary>
    public long LedgerWatermark { get; private set; }

    public DateTimeOffset ComputedAt { get; private set; }

    public static DailyFinancialSummary Create(Guid clinicId, Guid branchId, DateOnly date, DailyTotals totals, long watermark, DateTimeOffset now)
    {
        var s = new DailyFinancialSummary { ClinicId = clinicId, BranchId = branchId, LocalDate = date, Currency = Currency.UZS };
        s.Overwrite(totals, watermark, now);
        return s;
    }

    public DailyTotals Totals => new(Visits, Charged, Collected, Refunded, PriorPeriodRefunded, Commission);

    public void Overwrite(DailyTotals totals, long watermark, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(totals);
        Visits = totals.Visits;
        Charged = totals.Charged;
        Collected = totals.Collected;
        Refunded = totals.Refunded;
        PriorPeriodRefunded = totals.PriorPeriodRefunded;
        Commission = totals.Commission;
        LedgerWatermark = watermark;
        ComputedAt = now;
    }
}

/// <summary>
/// Per-day figures (UZS): visits = completions; charged = gross revenue at completion; collected = net money in
/// (payments minus failed-payment reversals); refunded = money returned; priorPeriodRefunded = the part of
/// refunded whose original payment was posted in an earlier month (F9); commission = net platform commission.
/// </summary>
public sealed record DailyTotals(int Visits, decimal Charged, decimal Collected, decimal Refunded, decimal PriorPeriodRefunded, decimal Commission)
{
    public static DailyTotals Zero { get; } = new(0, 0m, 0m, 0m, 0m, 0m);

    public DailyTotals Plus(DailyTotals other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new(Visits + other.Visits, Charged + other.Charged, Collected + other.Collected, Refunded + other.Refunded,
            PriorPeriodRefunded + other.PriorPeriodRefunded, Commission + other.Commission);
    }

    public bool IsZero => this == Zero;
}
