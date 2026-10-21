using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using OqTish.Domain.Common;

namespace OqTish.Application.Common.Images;

/// <summary>An upload that passed <see cref="ImageRules.Validate"/>: the type comes from the magic bytes.</summary>
public sealed record ValidatedImage(byte[] Bytes, string ContentType, string Sha256);

/// <summary>
/// Upload rules for stored images (clinic logo, avatar): JPEG, PNG or WebP, recognised by their magic bytes
/// (the client-supplied content type and file name are ignored), at most <see cref="StoredImage.MaxBytes"/>.
/// Every failure is 400 <c>common.validation</c> with a field error on <see cref="Field"/>.
/// </summary>
public static class ImageRules
{
    /// <summary>The multipart form field that carries the file.</summary>
    public const string Field = "file";

    public static readonly Error Required = Error.InvalidField(Field, "Required: send the image as multipart/form-data in the field 'file'.");

    public static readonly Error TooLarge = Error.InvalidField(Field, "The image must be at most 2 MB.");

    public static readonly Error UnsupportedType = Error.InvalidField(Field, "Upload a JPEG, PNG or WebP image.");

    private static ReadOnlySpan<byte> JpegMagic => [0xFF, 0xD8, 0xFF];

    private static ReadOnlySpan<byte> PngMagic => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> RiffMagic => "RIFF"u8;

    private static ReadOnlySpan<byte> WebPMagic => "WEBP"u8;

    /// <summary>The content type proven by the file signature, or null when it is not a supported image.</summary>
    public static string? DetectContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(JpegMagic))
        {
            return StoredImage.Jpeg;
        }

        if (bytes.StartsWith(PngMagic))
        {
            return StoredImage.Png;
        }

        // RIFF container: "RIFF" <uint32 size> "WEBP" …
        if (bytes.Length >= 12 && bytes.StartsWith(RiffMagic) && bytes.Slice(8, 4).SequenceEqual(WebPMagic))
        {
            return StoredImage.WebP;
        }

        return null;
    }

    public static Result<ValidatedImage> Validate(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return Required;
        }

        if (bytes.Length > StoredImage.MaxBytes)
        {
            return TooLarge;
        }

        if (DetectContentType(bytes) is not { } contentType)
        {
            return UnsupportedType;
        }

        return new ValidatedImage(bytes, contentType, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }
}

/// <summary>Adds and deletes <see cref="StoredImage"/> rows for the owners (clinic logo, user avatar).</summary>
internal static class StoredImageStore
{
    /// <summary>Adds the row to the unit of work (inserted by the caller's SaveChanges).</summary>
    public static StoredImage Add(IOqTishDb db, ValidatedImage image, DateTimeOffset now)
    {
        var stored = StoredImage.Create(image.Bytes, image.ContentType, image.Sha256, now);
        db.StoredImages.Add(stored);
        return stored;
    }

    /// <summary>Deletes a replaced/removed image immediately (call after the owner no longer references it).</summary>
    public static async Task DeleteAsync(IOqTishDb db, Guid? imageId, CancellationToken ct)
    {
        if (imageId is { } id)
        {
            await db.StoredImages.Where(i => i.Id == id).ExecuteDeleteAsync(ct);
        }
    }
}

/// <summary>Bytes and metadata of a stored image for <c>GET /api/files/images/{id}</c>.</summary>
public sealed record StoredImageContent(Guid Id, string ContentType, string Sha256, byte[] Bytes);

/// <summary>
/// GET /api/files/images/{id}. Auth: anonymous (ids are UUID v7, not enumerable; only logos and avatars are
/// stored). Unknown id → 404 <c>common.not_found</c>. Side effects: none.
/// </summary>
public sealed class StoredImageQuery(IOqTishDb db)
{
    public async Task<Result<StoredImageContent>> GetAsync(Guid id, CancellationToken ct)
    {
        var image = await db.StoredImages.AsNoTracking()
            .Where(i => i.Id == id)
            .Select(i => new StoredImageContent(i.Id, i.ContentType, i.Sha256, i.Bytes))
            .FirstOrDefaultAsync(ct);
        return image is null ? CommonErrors.NotFound : image;
    }
}
