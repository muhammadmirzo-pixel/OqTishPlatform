using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using OqTish.Application.Common.Security;

namespace OqTish.Infrastructure.Security;

/// <summary>ASP.NET Data Protection (AES-256-CBC + HMAC-SHA256, key ring in <c>identity.data_protection_keys</c>).</summary>
internal sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    public string Protect(string purpose, string plaintext) => provider.CreateProtector(purpose).Protect(plaintext);

    public string? Unprotect(string purpose, string protectedValue)
    {
        try
        {
            return provider.CreateProtector(purpose).Unprotect(protectedValue);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
