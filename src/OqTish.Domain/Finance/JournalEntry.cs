using OqTish.Domain.Common;

namespace OqTish.Domain.Finance;

/// <summary>
/// One balanced, append-only ledger posting (ADR-007, FINANCIAL_RULES §3). <see cref="SourceEventKey"/> is
/// UNIQUE: posting the same business event twice returns the existing entry. Corrections are new entries with
/// <see cref="ReversesEntryId"/>; nothing is ever updated or deleted (trigger + REVOKE). A deferred constraint
/// trigger checks at commit that the lines of every entry sum to zero per currency and that there are ≥ 2.
/// </summary>
public sealed class JournalEntry
{
    public const int MaxSourceEventKeyLength = 200;
    public const int MaxDescriptionLength = 200;

    private readonly List<LedgerLine> _lines = [];

    private JournalEntry()
    {
        SourceEventKey = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    /// <summary>The branch whose reporting day the entry belongs to; null for clinic-level entries (settlement paid).</summary>
    public Guid? BranchId { get; private set; }

    public JournalEntryType EntryType { get; private set; }

    public string SourceEventKey { get; private set; }

    /// <summary>Branch-local reporting day (after period-lock redirection, OD-F6).</summary>
    public DateOnly PostingDateLocal { get; private set; }

    /// <summary>When the business event happened (the basis event time used for rule selection).</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? ReversesEntryId { get; private set; }

    public Guid? AppointmentId { get; private set; }

    public Guid? PaymentId { get; private set; }

    public Guid? ServiceCompletionId { get; private set; }

    public Guid? SettlementId { get; private set; }

    public Guid? CreatedByUserId { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Database identity (monotonic insert order); used as the summary watermark. Not a business key.</summary>
    public long Sequence { get; private set; }

    public IReadOnlyList<LedgerLine> Lines => _lines;

    public static JournalEntry Create(
        Guid clinicId,
        Guid? branchId,
        JournalEntryType type,
        string sourceEventKey,
        DateOnly postingDateLocal,
        DateTimeOffset occurredAt,
        DateTimeOffset now,
        Guid? createdByUserId,
        Guid? appointmentId = null,
        Guid? paymentId = null,
        Guid? serviceCompletionId = null,
        Guid? settlementId = null,
        Guid? reversesEntryId = null,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceEventKey);
        if (sourceEventKey.Length > MaxSourceEventKeyLength)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceEventKey), "Source event key too long.");
        }

        return new JournalEntry
        {
            Id = IdGenerator.New(),
            ClinicId = clinicId,
            BranchId = branchId,
            EntryType = type,
            SourceEventKey = sourceEventKey,
            PostingDateLocal = postingDateLocal,
            OccurredAt = occurredAt,
            CreatedAt = now,
            CreatedByUserId = createdByUserId,
            AppointmentId = appointmentId,
            PaymentId = paymentId,
            ServiceCompletionId = serviceCompletionId,
            SettlementId = settlementId,
            ReversesEntryId = reversesEntryId,
            Description = description,
        };
    }

    /// <summary>Adds a line; the amount must already be rounded to 2 decimals (rounding happens once per line).</summary>
    public LedgerLine AddLine(
        LedgerAccount account,
        LedgerParty party,
        Money amount,
        Guid? commissionRuleId = null,
        decimal? rateSnapshot = null,
        Money? basisAmountSnapshot = null)
    {
        if (amount.Round() != amount)
        {
            throw new ArgumentException("Ledger amounts are rounded to 2 decimals before posting.", nameof(amount));
        }

        var line = LedgerLine.Create(this, account, party, amount, commissionRuleId, rateSnapshot, basisAmountSnapshot);
        _lines.Add(line);
        return line;
    }

    /// <summary>Σ lines per currency = 0 and ≥ 2 lines (the database checks the same at commit).</summary>
    public bool IsBalanced() => _lines.Count >= 2 && _lines.GroupBy(l => l.Currency).All(g => g.Sum(l => l.Amount) == 0m);
}

/// <summary>One signed line of a <see cref="JournalEntry"/> (debit &gt; 0, credit &lt; 0). Commission lines snapshot the rule.</summary>
public sealed class LedgerLine
{
    private LedgerLine()
    {
    }

    public Guid Id { get; private set; }

    public Guid JournalEntryId { get; private set; }

    public Guid ClinicId { get; private set; }

    public LedgerAccount Account { get; private set; }

    public LedgerParty Party { get; private set; }

    /// <summary>Signed numeric(19,2), never zero.</summary>
    public decimal Amount { get; private set; }

    public Currency Currency { get; private set; }

    public Guid? CommissionRuleId { get; private set; }

    /// <summary>Rate (percentage bases) of the rule when the line was computed, numeric(7,4).</summary>
    public decimal? RateSnapshot { get; private set; }

    /// <summary>The basis amount the commission was computed from (payment, charged amount, refund, fee).</summary>
    public decimal? BasisAmountSnapshot { get; private set; }

    public Money AmountMoney => new(Amount, Currency);

    internal static LedgerLine Create(JournalEntry entry, LedgerAccount account, LedgerParty party, Money amount, Guid? commissionRuleId, decimal? rateSnapshot, Money? basisAmountSnapshot)
    {
        if (amount.IsZero)
        {
            throw new ArgumentException("Ledger lines are never zero.", nameof(amount));
        }

        return new LedgerLine
        {
            Id = IdGenerator.New(),
            JournalEntryId = entry.Id,
            ClinicId = entry.ClinicId,
            Account = account,
            Party = party,
            Amount = amount.Amount,
            Currency = amount.Currency,
            CommissionRuleId = commissionRuleId,
            RateSnapshot = rateSnapshot,
            BasisAmountSnapshot = basisAmountSnapshot?.Amount,
        };
    }
}
