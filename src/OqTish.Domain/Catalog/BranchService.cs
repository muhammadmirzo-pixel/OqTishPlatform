using OqTish.Domain.Common;

namespace OqTish.Domain.Catalog;

/// <summary><c>Fixed</c>: the quoted price. <c>From</c>: a starting price ("dan").</summary>
public enum PriceKind
{
    Fixed = 1,
    From = 2,
}

/// <summary>
/// A service offered at one branch, with its price and duration. Key (branch_id, service_id); composite FKs
/// (clinic_id, branch_id) and (clinic_id, service_id) make cross-clinic offers impossible, and
/// <c>UNIQUE(clinic_id, branch_id, service_id)</c> is the target of the appointment FK (BE2).
/// </summary>
public sealed class BranchService
{
    public const int MinDurationMinutes = 5;
    public const int MaxDurationMinutes = 480;

    private BranchService()
    {
    }

    public Guid ClinicId { get; private set; }

    public Guid BranchId { get; private set; }

    public Guid ServiceId { get; private set; }

    /// <summary>Amount in <see cref="Currency"/>, numeric(19,2), ≥ 0.</summary>
    public decimal Price { get; private set; }

    public Currency Currency { get; private set; }

    public PriceKind PriceKind { get; private set; }

    public int DurationMinutes { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Money PriceMoney => new(Price, Currency);

    public static BranchService Create(Guid clinicId, Guid branchId, Guid serviceId, Money price, PriceKind kind, int durationMinutes, bool isActive, DateTimeOffset now)
    {
        var offer = new BranchService { ClinicId = clinicId, BranchId = branchId, ServiceId = serviceId, CreatedAt = now };
        offer.Update(price, kind, durationMinutes, isActive, now);
        return offer;
    }

    public void Update(Money price, PriceKind kind, int durationMinutes, bool isActive, DateTimeOffset now)
    {
        if (price.IsNegative)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "Price must not be negative.");
        }

        if (durationMinutes is < MinDurationMinutes or > MaxDurationMinutes)
        {
            throw new ArgumentOutOfRangeException(nameof(durationMinutes), "Duration must be 5..480 minutes.");
        }

        Price = price.Round().Amount;
        Currency = price.Currency;
        PriceKind = kind;
        DurationMinutes = durationMinutes;
        IsActive = isActive;
        UpdatedAt = now;
    }
}
