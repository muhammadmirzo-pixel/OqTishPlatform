using OqTish.Domain.Common;

namespace OqTish.Domain.Tenancy;

/// <summary>
/// A clinic location. <c>UNIQUE(clinic_id, id)</c> is the target of composite tenant FKs. Local times
/// (hours, schedules) are interpreted in <see cref="TimeZone"/> (IANA id). A database trigger creates
/// the matching <see cref="BranchBookingLock"/> row on insert.
/// </summary>
public sealed class Branch
{
    public const string DefaultTimeZone = "Asia/Tashkent";

    private Branch()
    {
        Name = string.Empty;
        District = string.Empty;
        Address = string.Empty;
        TimeZone = DefaultTimeZone;
    }

    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    public string Name { get; private set; }

    public string District { get; private set; }

    public string Address { get; private set; }

    public decimal? Latitude { get; private set; }

    public decimal? Longitude { get; private set; }

    public string? Phone { get; private set; }

    public string TimeZone { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Branch Create(
        Guid clinicId,
        string name,
        string district,
        string address,
        decimal? latitude,
        decimal? longitude,
        string? phone,
        string? timeZone,
        DateTimeOffset now)
    {
        var branch = new Branch
        {
            Id = IdGenerator.New(),
            ClinicId = clinicId,
            IsActive = true,
            CreatedAt = now,
        };
        branch.Update(name, district, address, latitude, longitude, phone, timeZone, true, now);
        return branch;
    }

    public void Update(
        string name,
        string district,
        string address,
        decimal? latitude,
        decimal? longitude,
        string? phone,
        string? timeZone,
        bool isActive,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        District = district.Trim();
        Address = address.Trim();
        Latitude = latitude;
        Longitude = longitude;
        Phone = phone;
        TimeZone = string.IsNullOrWhiteSpace(timeZone) ? DefaultTimeZone : timeZone;
        IsActive = isActive;
        UpdatedAt = now;
    }
}
