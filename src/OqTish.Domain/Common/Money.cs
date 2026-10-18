using System.Globalization;

namespace OqTish.Domain.Common;

/// <summary>
/// An amount of money in one currency (docs/FINANCIAL_RULES.md §2). Backed by <see cref="decimal"/>;
/// binary floating point is banned by an architecture test. Arithmetic between different currencies
/// throws, because mixing them is always a bug. Rounding happens only through <see cref="Round"/>
/// (2 decimal places, <see cref="MidpointRounding.AwayFromZero"/>, OD-F5).
/// </summary>
public readonly record struct Money : IComparable<Money>
{
    /// <summary>Number of decimal places stored (column type numeric(19,2)).</summary>
    public const int Scale = 2;

    /// <summary>Largest absolute amount that fits numeric(19,2).</summary>
    public const decimal MaxAbsoluteAmount = 99_999_999_999_999_999.99m;

    public Money(decimal amount, Currency currency)
    {
        if (!Enum.IsDefined(currency))
        {
            throw new ArgumentOutOfRangeException(nameof(currency), currency, "Unsupported currency.");
        }

        if (Math.Abs(amount) > MaxAbsoluteAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount exceeds numeric(19,2).");
        }

        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public Currency Currency { get; }

    public bool IsZero => Amount == 0m;

    public bool IsNegative => Amount < 0m;

    public bool IsPositive => Amount > 0m;

    public static Money Zero(Currency currency) => new(0m, currency);

    public static Money Uzs(decimal amount) => new(amount, Currency.UZS);

    /// <summary>True when the amount has at most <see cref="Scale"/> significant decimal places.</summary>
    public static bool HasValidScale(decimal amount) => decimal.Round(amount, Scale) == amount;

    /// <summary>
    /// Creates money from user input: rejects more than 2 decimal places instead of silently rounding.
    /// </summary>
    public static bool TryCreateExact(decimal amount, Currency currency, out Money money)
    {
        money = default;
        if (!HasValidScale(amount) || Math.Abs(amount) > MaxAbsoluteAmount || !Enum.IsDefined(currency))
        {
            return false;
        }

        money = new Money(decimal.Round(amount, Scale), currency);
        return true;
    }

    /// <summary>Rounds to 2 decimal places, midpoints away from zero (the only rounding rule).</summary>
    public Money Round() => new(decimal.Round(Amount, Scale, MidpointRounding.AwayFromZero), Currency);

    public Money Negate() => new(-Amount, Currency);

    public Money Abs() => new(Math.Abs(Amount), Currency);

    /// <summary>Multiplies by a factor (e.g. a commission rate). The result is unrounded: call <see cref="Round"/>.</summary>
    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public int CompareTo(Money other)
    {
        EnsureSameCurrency(other);
        return Amount.CompareTo(other.Amount);
    }

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public static Money operator -(Money value) => value.Negate();

    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    /// <summary>Invariant "1500000.00" form used on the wire.</summary>
    public string ToAmountString() =>
        decimal.Round(Amount, Scale, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    public override string ToString() => $"{ToAmountString()} {Currency}";

    private void EnsureSameCurrency(Money other)
    {
        if (other.Currency != Currency)
        {
            throw new InvalidOperationException($"Currency mismatch: {Currency} vs {other.Currency}.");
        }
    }
}
