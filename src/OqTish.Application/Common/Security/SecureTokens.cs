using System.Security.Cryptography;
using System.Text;

namespace OqTish.Application.Common.Security;

/// <summary>Random tokens (sessions, link tokens) and their SHA-256 storage form (CONVENTIONS §6).</summary>
public static class SecureTokens
{
    /// <summary>A URL-safe (base64url, no padding) token with <paramref name="bytes"/> bytes of entropy.</summary>
    public static string Create(int bytes = 32)
    {
        Span<byte> buffer = stackalloc byte[bytes];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToBase64String(buffer).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Lower-case hex SHA-256 of the token's UTF-8 bytes.</summary>
    public static string Sha256(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    /// <summary>Constant-time equality of two strings' UTF-8 bytes.</summary>
    public static bool FixedTimeEquals(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    }

    /// <summary>A uniformly random numeric code with <paramref name="digits"/> digits (leading zeros kept).</summary>
    public static string NumericCode(int digits = 6)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(digits, 4);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(digits, 9);
        var max = 1;
        for (var i = 0; i < digits; i++)
        {
            max *= 10;
        }

        return RandomNumberGenerator.GetInt32(0, max).ToString(new string('0', digits), System.Globalization.CultureInfo.InvariantCulture);
    }
}
