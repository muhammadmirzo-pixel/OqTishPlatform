using Microsoft.AspNetCore.Identity;
using OqTish.Application.Common.Security;

namespace OqTish.Infrastructure.Security;

/// <summary>
/// ASP.NET Core Identity's <see cref="PasswordHasher{TUser}"/> (PBKDF2-HMAC-SHA512, V3 format) without the
/// Identity user store (ADR-016).
/// </summary>
internal sealed class PasswordHashService : IPasswordHashService
{
    private static readonly PasswordHasher<Marker> Hasher = new();
    private static readonly Marker User = new();
    private static readonly Lazy<string> Dummy = new(() => Hasher.HashPassword(User, Guid.NewGuid().ToString("N")));

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return Hasher.HashPassword(User, password);
    }

    public (bool Valid, bool NeedsRehash) Verify(string hash, string password)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(password);
        return Hasher.VerifyHashedPassword(User, hash, password) switch
        {
            PasswordVerificationResult.Success => (true, false),
            PasswordVerificationResult.SuccessRehashNeeded => (true, true),
            _ => (false, false),
        };
    }

    public void VerifyDummy(string password) => Hasher.VerifyHashedPassword(User, Dummy.Value, password ?? string.Empty);

    private sealed class Marker;
}
