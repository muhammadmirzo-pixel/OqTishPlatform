using Microsoft.Net.Http.Headers;
using OqTish.Api.Common;
using OqTish.Application.Common.Images;

namespace OqTish.Api.Endpoints;

/// <summary>/api/files — stored images (clinic logos, avatars), docs/API.md §10.</summary>
public static class FileEndpoints
{
    /// <summary>Images never change under an id (a replacement gets a new id), so they are cacheable forever.</summary>
    public const string ImageCacheControl = "public, max-age=31536000, immutable";

    public static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        var files = app.MapGroup("/api/files").WithTags("Files");

        files.MapGet("/images/{id:guid}", async (Guid id, StoredImageQuery query, HttpContext http, CancellationToken ct) =>
                (await query.GetAsync(id, ct)).ToHttp(http, image =>
                {
                    http.Response.Headers.CacheControl = ImageCacheControl;
                    http.Response.Headers.XContentTypeOptions = "nosniff";
                    // FileContentHttpResult answers If-None-Match with 304 for this ETag.
                    return TypedResults.File(image.Bytes, image.ContentType, entityTag: new EntityTagHeaderValue("\"" + image.Sha256 + "\""));
                }))
            .AllowAnonymous()
            .WithName("GetImage").WithSummary("Stored image bytes (logo/avatar) with immutable caching, ETag and nosniff")
            .Produces(StatusCodes.Status200OK, (Type?)null, "image/jpeg", "image/png", "image/webp")
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(404);

        return app;
    }
}
