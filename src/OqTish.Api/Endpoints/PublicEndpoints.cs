using OqTish.Api.Common;
using OqTish.Application.Booking;
using OqTish.Application.Common;
using OqTish.Application.Public;
using OqTish.Application.Reviews;

namespace OqTish.Api.Endpoints;

/// <summary>/api/public — anonymous catalogue (docs/API.md §2), availability and published reviews (BE2).</summary>
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

        pub.MapGet("/clinics/{clinicId:guid}/reviews", async (Guid clinicId, int? page, int? pageSize, ReviewHandlers handler, HttpContext http, CancellationToken ct) =>
                (await handler.PublicListAsync(clinicId, page, pageSize, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("PublicClinicReviews").WithSummary("Published reviews of an approved clinic, newest first")
            .Produces<Page<ReviewDto>>().ProducesProblem(400).ProducesProblem(404);

        pub.MapGet("/branches/{branchId:guid}/availability", async (Guid branchId, Guid? serviceId, string? date, AvailabilityQuery query, HttpContext http, CancellationToken ct) =>
                (await query.HandleAsync(branchId, serviceId, date, ct)).ToHttp(http, TypedResults.Ok))
            .WithName("BranchAvailability").WithSummary("Free start times of a service on a branch-local date (15-min grid, 60-min lead, 30-day horizon)")
            .Produces<AvailabilityDto>().ProducesProblem(400).ProducesProblem(404);

        pub.MapGet("/facets", async (PublicCatalogQueries queries, HttpContext http, CancellationToken ct) =>
                (await queries.FacetsAsync(ct)).ToHttp(http, TypedResults.Ok))
            .WithName("PublicFacets").WithSummary("Districts, service categories and services for search filters")
            .Produces<FacetsDto>();

        return app;
    }
}
