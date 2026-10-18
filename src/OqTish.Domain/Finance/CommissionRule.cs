using OqTish.Domain.Common;

namespace OqTish.Domain.Finance;

/// <summary>
/// One approved commission contract term for a clinic (FINANCIAL_RULES §4, OD-F1). Percentage bases carry a
/// <see cref="Rate"/> (fraction with 4 decimals, 0.0750 = 7.5 %), fixed-fee bases a <see cref="FixedFee"/>.
/// Effective range [<see cref="EffectiveFrom"/>, <see cref="EffectiveTo"/>) never overlaps another rule of the
/// same clinic and basis (GiST exclusion constraint). Rules are never edited: the only change is
/// <see cref="End"/>, which sets/shortens <see cref="EffectiveTo"/> (a trigger enforces this in the database).
/// </summary>
public sealed class CommissionRule
{
    public const int RateScale = 4;
    public const int MaxContractReferenceLength = 200;

    private CommissionRule()
    {
        ContractReference = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    public CommissionBasis Basis { get; private set; }

    /// <summary>numeric(7,4) fraction in (0, 1]; null for fixed-fee bases.</summary>
    public decimal? Rate { get; private set; }

    /// <summary>numeric(19,2) &gt; 0; null for percentage bases.</summary>
    public decimal? FixedFee { get; private set; }

    public Currency Currency { get; private set; }

    public CommissionRounding Rounding { get; private set; }

    public DateTimeOffset EffectiveFrom { get; private set; }

    public DateTimeOffset? EffectiveTo { get; private set; }

    public string ContractReference { get; private set; }

    public Guid ApprovedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public Guid? EndedByUserId { get; private set; }

    public Money? FixedFeeMoney => FixedFee is { } f ? new Money(f, Currency) : null;

    /// <summary>True when the instant lies in [from, to).</summary>
    public bool CoversInstant(DateTimeOffset instant) => EffectiveFrom <= instant && (EffectiveTo is null || instant < EffectiveTo);

    public static CommissionRule Create(
        Guid clinicId,
        CommissionBasis basis,
        decimal? rate,
        Money? fixedFee,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        string contractReference,
        Guid approvedByUserId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractReference);
        if (LedgerAccounts.IsRateBasis(basis))
        {
            if (rate is not { } r || r <= 0m || r > 1m || decimal.Round(r, RateScale) != r || fixedFee is not null)
            {
                throw new ArgumentException("Percentage bases need a rate in (0, 1] with at most 4 decimals and no fixed fee.", nameof(rate));
            }
        }
        else if (fixedFee is not { IsPositive: true } fee || fee.Round() != fee || rate is not null)
        {
            throw new ArgumentException("Fixed-fee bases need a positive fee with at most 2 decimals and no rate.", nameof(fixedFee));
        }

        if (effectiveTo is { } to && to <= effectiveFrom)
        {
            throw new ArgumentException("The end must be after the start.", nameof(effectiveTo));
        }

        return new CommissionRule
        {
            Id = IdGenerator.New(),
            ClinicId = clinicId,
            Basis = basis,
            Rate = rate,
            FixedFee = fixedFee?.Amount,
            Currency = fixedFee?.Currency ?? Currency.UZS,
            Rounding = CommissionRounding.AwayFromZero,
            EffectiveFrom = effectiveFrom.ToUniversalTime(),
            EffectiveTo = effectiveTo?.ToUniversalTime(),
            ContractReference = contractReference.Trim(),
            ApprovedByUserId = approvedByUserId,
            CreatedAt = now,
        };
    }

    /// <summary>Ends the rule at <paramref name="effectiveTo"/> (only shortens, never extends or reopens).</summary>
    public void End(DateTimeOffset effectiveTo, Guid userId, DateTimeOffset now)
    {
        effectiveTo = effectiveTo.ToUniversalTime();
        if (effectiveTo <= EffectiveFrom || (EffectiveTo is { } current && effectiveTo > current))
        {
            throw new InvalidOperationException("A rule can only be shortened to an end after its start.");
        }

        EffectiveTo = effectiveTo;
        EndedAt = now;
        EndedByUserId = userId;
    }
}

/// <summary>
/// Commission arithmetic (FINANCIAL_RULES §2, §4, OD-F3/F5). Rounding happens once per computed line, 2 decimals,
/// midpoints away from zero; totals are sums of rounded lines (F6).
/// </summary>
public static class CommissionMath
{
    /// <summary>round(basis × rate) — e.g. 7.5 % of 333 333.33 = 24 999.99975 → 25 000.00 (F5).</summary>
    public static Money PercentageLine(Money basis, decimal rate) => basis.Multiply(rate).Round();

    /// <summary>
    /// Proportional reversal on a cumulative basis, so that reversing 100 % returns exactly the original line:
    /// <c>round(original × reducedSoFar / basis) − alreadyReversed</c>, where <paramref name="reducedSoFar"/>
    /// includes the current reduction and is capped at <paramref name="basis"/>. Returns a non-negative amount
    /// to reverse (the caller posts it with a negative sign).
    /// </summary>
    public static Money ProportionalReversal(Money originalCommission, Money basis, Money reducedSoFar, Money alreadyReversed)
    {
        if (!basis.IsPositive || !originalCommission.IsPositive)
        {
            return Money.Zero(originalCommission.Currency);
        }

        var capped = reducedSoFar > basis ? basis : reducedSoFar;
        var target = new Money(originalCommission.Amount * capped.Amount / basis.Amount, originalCommission.Currency).Round();
        var delta = target - alreadyReversed;
        return delta.IsNegative ? Money.Zero(originalCommission.Currency) : delta;
    }
}
