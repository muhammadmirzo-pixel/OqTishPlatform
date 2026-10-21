using OqTish.Domain.Common;

namespace OqTish.Domain.Tenancy;

public enum ClinicVerificationStatus
{
    Pending = 1,
    Approved = 2,
    Suspended = 3,
    Rejected = 4,
}

/// <summary>
/// A tenant. Only <see cref="ClinicVerificationStatus.Approved"/> clinics are public. A
/// <see cref="ClinicVerificationStatus.Suspended"/> clinic disables every membership in it immediately
/// (the access scope is computed per request).
/// </summary>
public sealed class Clinic
{
    private Clinic()
    {
        Name = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string? LegalName { get; private set; }

    public string? Description { get; private set; }

    public string? Phone { get; private set; }

    public ClinicVerificationStatus VerificationStatus { get; private set; }

    /// <summary>Reason given for the last rejection or suspension (shown to the clinic owner).</summary>
    public string? StatusReason { get; private set; }

    public Guid? CreatedByUserId { get; private set; }

    /// <summary>
    /// Average of published review ratings (1.00–5.00), null until the first published review. Maintained
    /// by review moderation (BE2) through <see cref="SetRating"/>; read by public search.
    /// </summary>
    public decimal? RatingAverage { get; private set; }

    /// <summary>Number of published reviews behind <see cref="RatingAverage"/>.</summary>
    public int ReviewCount { get; private set; }

    /// <summary>The clinic logo (<c>common.stored_images</c>), null when none was uploaded.</summary>
    public Guid? LogoImageId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsSuspended => VerificationStatus == ClinicVerificationStatus.Suspended;

    public static Clinic Register(string name, string? legalName, string? description, string? phone, Guid? createdByUserId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Clinic
        {
            Id = IdGenerator.New(),
            Name = name.Trim(),
            LegalName = legalName?.Trim(),
            Description = description?.Trim(),
            Phone = phone,
            VerificationStatus = ClinicVerificationStatus.Pending,
            CreatedByUserId = createdByUserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void UpdateProfile(string name, string? legalName, string? description, string? phone, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        LegalName = legalName?.Trim();
        Description = description?.Trim();
        Phone = phone;
        UpdatedAt = now;
    }

    /// <summary>Sets or clears (null) the logo image; the caller deletes the previous image row.</summary>
    public void SetLogo(Guid? imageId, DateTimeOffset now)
    {
        LogoImageId = imageId;
        UpdatedAt = now;
    }

    /// <summary>Stores the published-review statistics (computed by the Reviews module).</summary>
    public void SetRating(decimal? average, int reviewCount, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(reviewCount);
        if (average is { } a && (a < 1m || a > 5m))
        {
            throw new ArgumentOutOfRangeException(nameof(average), "Rating average must be within 1..5.");
        }

        RatingAverage = reviewCount == 0 ? null : average is null ? null : decimal.Round(average.Value, 2, MidpointRounding.AwayFromZero);
        ReviewCount = reviewCount;
        UpdatedAt = now;
    }

    public void Approve(DateTimeOffset now) => SetStatus(ClinicVerificationStatus.Approved, null, now);

    public void Reject(string reason, DateTimeOffset now) => SetStatus(ClinicVerificationStatus.Rejected, reason, now);

    public void Suspend(string reason, DateTimeOffset now) => SetStatus(ClinicVerificationStatus.Suspended, reason, now);

    public void Reinstate(DateTimeOffset now) => SetStatus(ClinicVerificationStatus.Approved, null, now);

    private void SetStatus(ClinicVerificationStatus status, string? reason, DateTimeOffset now)
    {
        VerificationStatus = status;
        StatusReason = reason;
        UpdatedAt = now;
    }
}
