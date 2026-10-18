namespace OqTish.Application.Common.Security;

/// <summary>
/// Reversible encryption for secrets at rest (TOTP secrets). Implemented with ASP.NET Data Protection;
/// the key ring is persisted in the database (<c>identity.data_protection_keys</c>).
/// </summary>
public interface ISecretProtector
{
    string Protect(string purpose, string plaintext);

    /// <summary>Returns null when the payload cannot be decrypted (tampered or key lost).</summary>
    string? Unprotect(string purpose, string protectedValue);
}

/// <summary>Password hashing (ASP.NET Core Identity <c>PasswordHasher</c>, PBKDF2).</summary>
public interface IPasswordHashService
{
    string Hash(string password);

    /// <summary>Verifies a password. <c>NeedsRehash</c> is true when the stored format is outdated.</summary>
    (bool Valid, bool NeedsRehash) Verify(string hash, string password);

    /// <summary>Runs a full verification against a fixed hash (equalises timing for unknown accounts).</summary>
    void VerifyDummy(string password);
}
