using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using OqTish.Application.Common.Security;

namespace OqTish.Infrastructure.Security;

/// <summary>HMAC-SHA256 keyed with <c>Security:OtpPepper</c> (validated at startup).</summary>
internal sealed class HmacKeyedHasher(IOptions<SecurityOptions> options) : IKeyedHasher
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(options.Value.OtpPepper);

    public string Hash(string purpose, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(purpose);
        ArgumentNullException.ThrowIfNull(value);
        return Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(purpose + ":" + value)));
    }

    public bool Verify(string purpose, string value, string expectedHash)
    {
        ArgumentNullException.ThrowIfNull(expectedHash);
        var actual = Encoding.ASCII.GetBytes(Hash(purpose, value));
        var expected = Encoding.ASCII.GetBytes(expectedHash);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
