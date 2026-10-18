namespace OqTish.Domain.Identity;

/// <summary>Patient-facing profile. Contains no medical data (OD-06).</summary>
public sealed class PatientProfile
{
    public const int MaxNameLength = 100;

    private PatientProfile()
    {
        FullName = string.Empty;
    }

    public Guid UserId { get; private set; }

    public string FullName { get; private set; }

    /// <summary>E.164 contact phone, defaulted from the verified phone identity.</summary>
    public string? ContactPhone { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsAnonymised { get; private set; }

    public static PatientProfile Create(Guid userId, string fullName, string? contactPhone, DateTimeOffset now) => new()
    {
        UserId = userId,
        FullName = fullName.Trim(),
        ContactPhone = contactPhone,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void Rename(string fullName, DateTimeOffset now)
    {
        FullName = fullName.Trim();
        UpdatedAt = now;
    }

    public void SetContactPhone(string? phone, DateTimeOffset now)
    {
        ContactPhone = phone;
        UpdatedAt = now;
    }

    public void Anonymise(DateTimeOffset now)
    {
        FullName = "—";
        ContactPhone = null;
        IsAnonymised = true;
        UpdatedAt = now;
    }
}
