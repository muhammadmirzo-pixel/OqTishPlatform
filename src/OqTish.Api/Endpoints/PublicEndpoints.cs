using OqTish.Api.Common;
using OqTish.Application.Common;
using OqTish.Application.Public;

namespace OqTish.Api.Endpoints;

/// <summary>/api/public — anonymous catalogue (docs/API.md §2). Availability and reviews come with BE2.</summary>
public static class PublicEndpoints
{
    public static IEndpointRouteBuilder MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        var pub = app.MapGroup("/api/public").WithTags("Public").AllowAnonymous();

        pub.MapGet("/clinics", async (
                    string? q, string? serviceCategory, Guid? serviceId, string? district, decimal? lat, decimal? lng,
                    int? availableWithinDays, string? sort, int? page, int? pageSize,
                    PublicCatalogQueries queries, HttpContext http, CancellationToken ct) =>
                (await queries.SearchAsync(new ClinicSearchQuery(q, serviceCategory, serviceId, district, lat, lng, availableWithinDays, sort, page, pageSize), ct))
                    .ToHttp(http, TypedResults.Ok))
            .WithName("SearchClinics").WithSummary("Search approved clinics (filters, distance, sort, paging)")
            .Produces<Page<ClinicCardDto>>().ProducesProblem(400);

        pub.MapGet("/clinics/{clinicId:guid}", async (Guid clinicId, PublicCatalogQueries queries, HttpContext http, CancellationToken ct) =>
                (await queries.DetailAsync(clinicId, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("PublicClinic").WithSummary("Approved clinic with active branches, hours and offers (404 otherwise)")
            .Produces<ClinicDetailDto>().ProducesProblem(404);

        pub.MapGet("/facets", async (PublicCatalogQueries queries, HttpContext http, CancellationToken ct) =>
                (await queries.FacetsAsync(ct)).ToHttp(http, TypedResults.Ok))
            .WithName("PublicFacets").WithSummary("Districts, service categories and services for search filters")
            .Produces<FacetsDto>();

        return app;
    }
}
