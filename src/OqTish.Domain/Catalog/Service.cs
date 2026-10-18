using OqTish.Domain.Common;

namespace OqTish.Domain.Catalog;

/// <summary>
/// A service a clinic offers (e.g. "Tish plombalash"). Prices and durations are per branch
/// (<see cref="BranchService"/>). <c>UNIQUE(clinic_id, id)</c> is the target of composite tenant FKs.
/// <see cref="Category"/> is a key from the fixed category catalogue (Application <c>ServiceCategories</c>).
/// </summary>
public sealed class Service
{
    public const int MaxNameLength = 200;

    private Service()
    {
        Name = string.Empty;
        Category = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    public string Name { get; private set; }

    public string Category { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Service Create(Guid clinicId, string name, string category, bool isActive, DateTimeOffset now)
    {
        var service = new Service { Id = IdGenerator.New(), ClinicId = clinicId, CreatedAt = now };
        service.Update(name, category, isActive, now);
        return service;
    }

    public void Update(string name, string category, bool isActive, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        Name = name.Trim();
        Category = category;
        IsActive = isActive;
        UpdatedAt = now;
    }
}
