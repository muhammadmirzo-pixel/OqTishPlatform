// LINQ string calls below are translated to SQL lower()/strpos(); .NET culture rules never apply.
#pragma warning disable CA1304, CA1311, CA1862
using Microsoft.EntityFrameworkCore;
using OqTish.Application.Booking;
using OqTish.Application.Catalog;
using OqTish.Application.Common;
using OqTish.Application.Tenancy;
using OqTish.Domain.Catalog;
using OqTish.Domain.Common;
using OqTish.Domain.Tenancy;

namespace OqTish.Application.Public;

public sealed record ClinicSearchQuery(
    string? Q,
    string? ServiceCategory,
    Guid? ServiceId,
    string? District,
    decimal? Lat,
    decimal? Lng,
    int? AvailableWithinDays,
    string? Sort,
    int? Page,
    int? PageSize);

public sealed record ClinicCardDto(
    Guid Id,
    string Name,
    decimal? Rating,
    int ReviewCount,
    Money? MinPrice,
    int BranchCount,
    IReadOnlyList<string> Districts,
    decimal? DistanceKm,
    DateTimeOffset? NextFreeSlot,
    Guid? LogoId);

public sealed record PublicServiceOfferDto(Guid ServiceId, string Name, string Category, Money Price, PriceKind PriceKind, int DurationMinutes);

public sealed record PublicBranchDto(
    Guid Id,
    string Name,
    string District,
    string Address,
    decimal? Latitude,
    decimal? Longitude,
    string TimeZone,
    string Phone,
    IReadOnlyList<OpeningHoursDto> Hours,
    IReadOnlyList<PublicServiceOfferDto> Services);

public sealed record ClinicDetailDto(Guid Id, string Name, string Description, decimal? Rating, int ReviewCount, IReadOnlyList<PublicBranchDto> Branches, Guid? LogoId);

public sealed record FacetServiceDto(Guid Id, string Name, string Category);

public sealed record FacetsDto(IReadOnlyList<string> Districts, IReadOnlyList<ServiceCategory> Categories, IReadOnlyList<FacetServiceDto> Services);

/// <summary>
/// Public catalogue (docs/API.md §2). Auth: anonymous. Only <c>Approved</c> clinics (a suspended, pending or
/// rejected clinic is invisible: 404 for the detail), only active branches, and only active offers of active
/// services. Side effects: none.
/// <list type="bullet">
/// <item><c>q</c>: clinic name or offered service name contains the text (case-insensitive).</item>
/// <item><c>serviceId</c> / <c>serviceCategory</c> / <c>district</c>: the clinic has a matching active offer /
/// branch. <c>minPrice</c> and <c>distanceKm</c> are computed over the matching branches and offers.</item>
/// <item><c>lat</c> + <c>lng</c>: distance to the nearest matching branch with coordinates (Haversine in SQL,
/// km, 2 decimals). <c>sort=distance</c> requires them.</item>
/// <item><c>sort</c>: <c>rating</c> (default without coordinates; nulls last), <c>price</c> (min price, nulls
/// last), <c>distance</c> (default with coordinates), <c>name</c>. Ties break by name, then id.</item>
/// <item><c>availableWithinDays</c> (1..30): only clinics with at least one free slot (any matching offer and
/// branch) on the branch-local days today … today + N − 1 (availability engine, BE2; applied before paging).
/// <c>nextFreeSlot</c> = the earliest such slot within N days (default <c>Booking:SearchDefaultAvailabilityDays</c> = 7),
/// computed for the page's clinics; null when none.</item>
/// </list>
/// </summary>
public sealed class PublicCatalogQueries(IOqTishDb db, PublicAvailability availability, Microsoft.Extensions.Options.IOptions<BookingOptions> bookingOptions)
{
    private static readonly string[] Sorts = ["rating", "price", "distance", "name"];

    public async Task<Result<Page<ClinicCardDto>>> SearchAsync(ClinicSearchQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var paging = PageRequest.Create(query.Page, query.PageSize);
        var errors = new FieldErrors();
        if (!paging.IsSuccess)
        {
            foreach (var (field, messages) in paging.Error.FieldErrors ?? new Dictionary<string, string[]>())
            {
                foreach (var m in messages)
                {
                    errors.Add(field, m);
                }
            }
        }

        var hasGeo = query.Lat is not null && query.Lng is not null;
        if ((query.Lat is null) != (query.Lng is null))
        {
            errors.Add("lat", "lat and lng go together.");
        }

        if (query.Lat is { } la && (la < -90 || la > 90))
        {
            errors.Add("lat", "Must be within -90..90.");
        }

        if (query.Lng is { } lo && (lo < -180 || lo > 180))
        {
            errors.Add("lng", "Must be within -180..180.");
        }

        var sort = string.IsNullOrWhiteSpace(query.Sort) ? (hasGeo ? "distance" : "rating") : query.Sort.Trim().ToLowerInvariant();
        if (!Sorts.Contains(sort))
        {
            errors.Add("sort", "Use rating, price, distance or name.");
        }
        else if (sort == "distance" && !hasGeo)
        {
            errors.Add("sort", "Sorting by distance needs lat and lng.");
        }

        if (query.AvailableWithinDays is { } days && days is < 1 or > 30)
        {
            errors.Add("availableWithinDays", "Must be 1..30.");
        }

        if (query.ServiceCategory is { Length: > 0 } cat && !ServiceCategories.IsKnown(cat))
        {
            errors.Add("serviceCategory", "Unknown category.");
        }

        if (query.Q is { Length: > 100 })
        {
            errors.Add("q", "At most 100 characters.");
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var activeBranches = db.Branches.AsNoTracking().Where(b => b.IsActive);
        var offers =
            from o in db.BranchServices.AsNoTracking()
            join s in db.Services.AsNoTracking() on o.ServiceId equals s.Id
            join b in db.Branches.AsNoTracking() on o.BranchId equals b.Id
            where o.IsActive && s.IsActive && b.IsActive
            select new { o.ClinicId, o.BranchId, b.District, ServiceId = s.Id, s.Name, s.Category, o.Price };

        var matchingOffers = offers;
        var matchingBranches = activeBranches;
        var serviceFilter = query.ServiceId is not null || !string.IsNullOrEmpty(query.ServiceCategory);
        if (query.ServiceId is { } serviceId)
        {
            matchingOffers = matchingOffers.Where(o => o.ServiceId == serviceId);
        }

        if (!string.IsNullOrEmpty(query.ServiceCategory))
        {
            var category = query.ServiceCategory;
            matchingOffers = matchingOffers.Where(o => o.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(query.District))
        {
            var district = query.District.Trim().ToLower();
            matchingOffers = matchingOffers.Where(o => o.District.ToLower() == district);
            matchingBranches = matchingBranches.Where(b => b.District.ToLower() == district);
        }

        if (serviceFilter)
        {
            var mo = matchingOffers;
            matchingBranches = matchingBranches.Where(b => mo.Any(o => o.BranchId == b.Id));
        }

        var mb = matchingBranches;
        var clinics = db.Clinics.AsNoTracking()
            .Where(c => c.VerificationStatus == ClinicVerificationStatus.Approved && mb.Any(b => b.ClinicId == c.Id));
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim().ToLower();
            clinics = clinics.Where(c => c.Name.ToLower().Contains(term) || offers.Any(o => o.ClinicId == c.Id && o.Name.ToLower().Contains(term)));
        }

        var slotDays = query.AvailableWithinDays ?? bookingOptions.Value.SearchDefaultAvailabilityDays;
        IReadOnlyDictionary<Guid, DateTimeOffset>? freeSlots = null;
        if (query.AvailableWithinDays is not null)
        {
            var candidateIds = await clinics.Select(c => c.Id).ToListAsync(ct);
            freeSlots = await availability.NextFreeSlotsAsync(candidateIds, query.ServiceId, query.ServiceCategory, query.District, slotDays, ct);
            var available = freeSlots.Keys.ToList();
            clinics = clinics.Where(c => available.Contains(c.Id));
        }

        var moffers = matchingOffers;
        IQueryable<SearchRow> rows;
        if (hasGeo)
        {
            var lat = query.Lat!.Value;
            var lng = query.Lng!.Value;
            rows = clinics.Select(c => new SearchRow
            {
                Id = c.Id,
                Name = c.Name,
                Rating = c.RatingAverage,
                ReviewCount = c.ReviewCount,
                LogoId = c.LogoImageId,
                MinPrice = moffers.Where(o => o.ClinicId == c.Id).Min(o => (decimal?)o.Price),
                BranchCount = activeBranches.Count(b => b.ClinicId == c.Id),
                DistanceKm = mb.Where(b => b.ClinicId == c.Id && b.Latitude != null && b.Longitude != null)
                    .Min(b => (decimal?)GeoFunctions.DistanceKm(lat, lng, b.Latitude!.Value, b.Longitude!.Value)),
            });
        }
        else
        {
            rows = clinics.Select(c => new SearchRow
            {
                Id = c.Id,
                Name = c.Name,
                Rating = c.RatingAverage,
                ReviewCount = c.ReviewCount,
                LogoId = c.LogoImageId,
                MinPrice = moffers.Where(o => o.ClinicId == c.Id).Min(o => (decimal?)o.Price),
                BranchCount = activeBranches.Count(b => b.ClinicId == c.Id),
                DistanceKm = null,
            });
        }

        var ordered = sort switch
        {
            "price" => rows.OrderBy(r => r.MinPrice == null).ThenBy(r => r.MinPrice).ThenBy(r => r.Name).ThenBy(r => r.Id),
            "distance" => rows.OrderBy(r => r.DistanceKm == null).ThenBy(r => r.DistanceKm).ThenBy(r => r.Name).ThenBy(r => r.Id),
            "name" => rows.OrderBy(r => r.Name).ThenBy(r => r.Id),
            _ => rows.OrderBy(r => r.Rating == null).ThenByDescending(r => r.Rating).ThenByDescending(r => r.ReviewCount).ThenBy(r => r.Name).ThenBy(r => r.Id),
        };

        var page = await ordered.ToPageAsync(paging.Value, ct);
        var ids = page.Items.Select(r => r.Id).ToList();
        freeSlots ??= await availability.NextFreeSlotsAsync(ids, query.ServiceId, query.ServiceCategory, query.District, slotDays, ct);
        var districts = await activeBranches.Where(b => ids.Contains(b.ClinicId)).Select(b => new { b.ClinicId, b.District }).Distinct().ToListAsync(ct);
        return new Page<ClinicCardDto>(
            [.. page.Items.Select(r => new ClinicCardDto(
                r.Id, r.Name, r.Rating, r.ReviewCount, r.MinPrice is { } p ? Money.Uzs(p) : null, r.BranchCount,
                [.. districts.Where(d => d.ClinicId == r.Id).Select(d => d.District).Order(StringComparer.Ordinal)],
                r.DistanceKm, freeSlots.TryGetValue(r.Id, out var slot) ? slot : (DateTimeOffset?)null, r.LogoId))],
            page.PageNumber, page.PageSize, page.TotalCount);
    }

    public async Task<Result<ClinicDetailDto>> DetailAsync(Guid clinicId, CancellationToken ct)
    {
        var clinic = await db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clinicId && c.VerificationStatus == ClinicVerificationStatus.Approved, ct);
        if (clinic is null)
        {
            return CommonErrors.NotFound;
        }

        var branches = await db.Branches.AsNoTracking().Where(b => b.ClinicId == clinic.Id && b.IsActive).OrderBy(b => b.Name).ThenBy(b => b.Id).ToListAsync(ct);
        var ids = branches.Select(b => b.Id).ToList();
        var hours = await db.BranchOpeningHours.AsNoTracking().Where(h => ids.Contains(h.BranchId)).ToListAsync(ct);
        var offers = await (
                from o in db.BranchServices.AsNoTracking()
                join s in db.Services.AsNoTracking() on o.ServiceId equals s.Id
                where ids.Contains(o.BranchId) && o.IsActive && s.IsActive
                orderby s.Name, s.Id
                select new { Offer = o, s.Name, s.Category })
            .ToListAsync(ct);
        return new ClinicDetailDto(clinic.Id, clinic.Name, clinic.Description ?? string.Empty, clinic.RatingAverage, clinic.ReviewCount,
            [.. branches.Select(b => new PublicBranchDto(
                b.Id, b.Name, b.District, b.Address, b.Latitude, b.Longitude, b.TimeZone, b.Phone ?? string.Empty,
                b.ToDto(hours).Hours,
                [.. offers.Where(o => o.Offer.BranchId == b.Id).Select(o => new PublicServiceOfferDto(
                    o.Offer.ServiceId, o.Name, o.Category, o.Offer.PriceMoney, o.Offer.PriceKind, o.Offer.DurationMinutes))]))],
            clinic.LogoImageId);
    }

    public async Task<Result<FacetsDto>> FacetsAsync(CancellationToken ct)
    {
        var publicBranches =
            from b in db.Branches.AsNoTracking()
            join c in db.Clinics.AsNoTracking() on b.ClinicId equals c.Id
            where b.IsActive && c.VerificationStatus == ClinicVerificationStatus.Approved
            select b;
        var districts = await publicBranches.Where(b => b.District != "").Select(b => b.District).Distinct().OrderBy(d => d).ToListAsync(ct);
        var services = await (
                from o in db.BranchServices.AsNoTracking()
                join s in db.Services.AsNoTracking() on o.ServiceId equals s.Id
                where o.IsActive && s.IsActive && publicBranches.Any(b => b.Id == o.BranchId)
                select new { s.Id, s.Name, s.Category })
            .Distinct()
            .OrderBy(s => s.Name).ThenBy(s => s.Id)
            .Take(500)
            .ToListAsync(ct);
        return new FacetsDto(districts, ServiceCategories.All, [.. services.Select(s => new FacetServiceDto(s.Id, s.Name, s.Category))]);
    }

    private sealed class SearchRow
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public decimal? Rating { get; init; }

        public int ReviewCount { get; init; }

        public decimal? MinPrice { get; init; }

        public int BranchCount { get; init; }

        public decimal? DistanceKm { get; init; }

        public Guid? LogoId { get; init; }
    }
}
