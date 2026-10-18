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
