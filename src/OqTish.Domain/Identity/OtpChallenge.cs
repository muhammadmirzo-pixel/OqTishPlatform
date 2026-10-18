using OqTish.Domain.Common;

namespace OqTish.Domain.Identity;

public enum OtpPurpose
{
    SignIn = 1,
    LinkPhone = 2,
    PasswordReset = 3,
}

/// <summary>
/// A one-time code sent by SMS. Only an HMAC of the code is stored. Attempts, expiry and consumption are
/// updated with conditional statements so that concurrent verifications cannot both succeed.
/// Rows are deleted after 24 h by the retention job (OD-18).
/// </summary>
public sealed class OtpChallenge
{
    private OtpChallenge()
    {
        IdentifierHash = string.Empty;
        CodeHash = string.Empty;
    }

    public Guid Id { get; private set; }

    public OtpPurpose Purpose { get; private set; }

    /// <summary>Keyed hash of the normalised phone; used for rate limits without storing the phone twice.</summary>
    public string IdentifierHash { get; private set; }

    /// <summary>The E.164 phone the code was sent to (null for a decoy password-reset challenge).</summary>
    public string? Phone { get; private set; }

    /// <summary>The account the challenge belongs to (LinkPhone, PasswordReset); null for SignIn.</summary>
    public Guid? UserId { get; private set; }

    public string CodeHash { get; private set; }

    public string? IpHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public int Attempts { get; private set; }

    public int MaxAttempts { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    public static OtpChallenge Create(
        Guid id,
        OtpPurpose purpose,
        string identifierHash,
        string? phone,
        Guid? userId,
        string codeHash,
        string? ipHash,
        DateTimeOffset now,
        TimeSpan ttl,
        int maxAttempts) => new()
        {
            Id = id,
            Purpose = purpose,
            IdentifierHash = identifierHash,
            Phone = phone,
            UserId = userId,
            CodeHash = codeHash,
            IpHash = ipHash,
            CreatedAt = now,
            ExpiresAt = now + ttl,
            MaxAttempts = maxAttempts,
        };

    public static Guid NewId() => IdGenerator.New();
}
