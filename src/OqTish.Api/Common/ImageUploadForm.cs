using Microsoft.AspNetCore.Mvc;
using OqTish.Application.Common;
using OqTish.Application.Common.Images;
using OqTish.Domain.Common;

namespace OqTish.Api.Common;

/// <summary>
/// Reads the image of a <c>multipart/form-data</c> upload (field <c>file</c>) for the logo/avatar endpoints.
/// The form is read manually instead of binding <c>IFormFile</c>, so every problem becomes the standard
/// 400 <c>common.validation</c> field error on <c>file</c> (not a generic 400/415) and no antiforgery-token
/// metadata is attached: CSRF is covered, as for every other mutation, by the required
/// <c>X-OqTish-Client</c> header (<see cref="ClientHeaderMiddleware"/>, OD-23).
/// </summary>
public static class ImageUploadForm
{
    /// <summary>Request body limit of the upload endpoints (the global Kestrel limit is 1 MiB): 2 MiB image + multipart overhead.</summary>
    public const long MaxRequestBytes = 3 * 1024 * 1024;

    /// <summary>
    /// Raises the body size limit for one endpoint. Deliberately no <c>Accepts("multipart/form-data")</c>
    /// metadata: it would make routing answer other content types with a bare 415 instead of the field error.
    /// </summary>
    public static RouteHandlerBuilder AcceptsImageUpload(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes));
    }

    /// <summary>The uploaded bytes (not yet validated as an image), or a field error on <c>file</c>.</summary>
    public static async Task<Result<byte[]>> ReadAsync(HttpRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.HasFormContentType)
        {
            return ImageRules.Required;
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(ct);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return ImageRules.TooLarge;
        }
        catch (InvalidDataException)
        {
            // Malformed multipart body (boundary, section length limits).
            return ImageRules.Required;
        }

        if (form.Files.GetFile(ImageRules.Field) is not { Length: > 0 } file)
        {
            return ImageRules.Required;
        }

        if (file.Length > StoredImage.MaxBytes)
        {
            return ImageRules.TooLarge;
        }

        var bytes = new byte[file.Length];
        await using var stream = file.OpenReadStream();
        await stream.ReadExactlyAsync(bytes, ct);
        return bytes;
    }
}
