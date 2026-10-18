using OqTish.Domain.Common;

namespace OqTish.Domain.Identity;

public enum AuthIdentityKind
{
    Phone = 1,
    Telegram = 2,
    EmailPassword = 3,
}

/// <summary>
/// A way to sign in. <c>UNIQUE(kind, normalized_identifier)</c> guarantees an identifier belongs to one
/// account, and <c>UNIQUE(user_id, kind)</c> gives each account at most one identifier per kind.
/// Accounts are never merged (SECURITY.md §1).
/// </summary>
public sealed class AuthIdentity
{
    private AuthIdentity()
    {
        NormalizedIdentifier = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public AuthIdentityKind Kind { get; private set; }

    /// <summary>Phone: E.164 (+998XXXXXXXXX). Telegram: numeric user id. Email: lower-case address.</summary>
    public string NormalizedIdentifier { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public static AuthIdentity Create(Guid userId, AuthIdentityKind kind, string normalizedIdentifier, bool verified, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedIdentifier);
        return new AuthIdentity
        {
            Id = IdGenerator.New(),
            UserId = userId,
            Kind = kind,
            NormalizedIdentifier = normalizedIdentifier,
            VerifiedAt = verified ? now : null,
            CreatedAt = now,
        };
    }

    public void ChangeIdentifier(string normalizedIdentifier, bool verified, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedIdentifier);
        NormalizedIdentifier = normalizedIdentifier;
        VerifiedAt = verified ? now : null;
    }

    public void MarkVerified(DateTimeOffset now) => VerifiedAt ??= now;

    public void MarkUsed(DateTimeOffset now) => LastUsedAt = now;
}
