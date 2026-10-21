namespace OqTish.Domain.Common;

/// <summary>
/// An uploaded image (clinic logo, user avatar) stored in PostgreSQL (<c>common.stored_images</c>); no
/// external blob store. Immutable: a replacement is a new row with a new id, so clients may cache an image
/// by id forever. The owner (clinic / user) references the row; replacing or removing deletes the old row.
/// </summary>
public sealed class StoredImage
{
    /// <summary>2 MiB: the largest image accepted (also a CHECK constraint on <c>size_bytes</c>).</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    public const string Jpeg = "image/jpeg";

    public const string Png = "image/png";

    public const string WebP = "image/webp";

    private StoredImage()
    {
        ContentType = string.Empty;
        Bytes = [];
        Sha256 = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary><c>image/jpeg</c>, <c>image/png</c> or <c>image/webp</c>, detected from the bytes (never the client's claim).</summary>
    public string ContentType { get; private set; }

    public byte[] Bytes { get; private set; }

    public int SizeBytes { get; private set; }

    /// <summary>Lower-case hex SHA-256 of <see cref="Bytes"/> (used as the HTTP ETag).</summary>
    public string Sha256 { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static StoredImage Create(byte[] bytes, string contentType, string sha256, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        if (bytes.Length is 0 or > MaxBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), "An image must have 1 byte to 2 MiB.");
        }

        if (contentType is not (Jpeg or Png or WebP))
        {
            throw new ArgumentOutOfRangeException(nameof(contentType), "Only JPEG, PNG and WebP images are stored.");
        }

        return new StoredImage
        {
            Id = IdGenerator.New(),
            ContentType = contentType,
            Bytes = bytes,
            SizeBytes = bytes.Length,
            Sha256 = sha256,
            CreatedAt = now,
        };
    }
}
