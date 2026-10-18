namespace OqTish.Application.Catalog;

/// <summary>One entry of the fixed service-category catalogue (the key is stored; the name is Uzbek).</summary>
public sealed record ServiceCategory(string Key, string Name);

/// <summary>
/// Fixed category catalogue shared by every clinic, so that public filters work across clinics. The UI
/// localises by <see cref="ServiceCategory.Key"/>; <see cref="ServiceCategory.Name"/> is the Uzbek default.
/// </summary>
public static class ServiceCategories
{
    public static readonly IReadOnlyList<ServiceCategory> All =
    [
        new("diagnostics", "Diagnostika va konsultatsiya"),
        new("hygiene", "Gigiena va professional tozalash"),
        new("therapy", "Terapiya (plomba, kanal davolash)"),
        new("surgery", "Jarrohlik (tish olish)"),
        new("implants", "Implantatsiya"),
        new("prosthetics", "Protezlash (koronka, protez)"),
        new("orthodontics", "Ortodontiya (breket, eleyner)"),
        new("pediatric", "Bolalar stomatologiyasi"),
        new("whitening", "Oqartirish va estetika"),
        new("periodontics", "Parodontologiya"),
        new("other", "Boshqa"),
    ];

    public static bool IsKnown(string? key) => key is not null && All.Any(c => c.Key == key);
}
