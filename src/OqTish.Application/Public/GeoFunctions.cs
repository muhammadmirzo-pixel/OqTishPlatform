[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Scope = "namespace", Target = "~N:OqTish.Application.Public", Justification = "Module folder name from CONVENTIONS §1; C#-only codebase.")]

namespace OqTish.Application.Public;

/// <summary>
/// Database functions usable in LINQ queries (translated to SQL, never executed in .NET). Mapped by
/// <c>OqTishDbContext</c>; the SQL function is created by migration <c>CatalogScheduling</c>.
/// </summary>
public static class GeoFunctions
{
    /// <summary>
    /// <c>tenancy.distance_km</c>: Haversine great-circle distance between two WGS-84 points in kilometres,
    /// rounded to 2 decimals (earth radius 6371 km, OD-16).
    /// </summary>
    public static decimal DistanceKm(decimal latitude1, decimal longitude1, decimal latitude2, decimal longitude2) =>
        throw new InvalidOperationException("GeoFunctions.DistanceKm is translated to SQL and cannot be called directly.");
}
