namespace OqTish.Application.Common.Security;

/// <summary>
/// HMAC-SHA256 with the server pepper (<c>Security:OtpPepper</c>), domain-separated by purpose. Used for
/// values that must be matched later but never stored in clear: OTP codes, phone lookups for rate
/// limits and invitations, IP addresses.
/// </summary>
public interface IKeyedHasher
{
    /// <summary>Lower-case hex HMAC of <c>purpose + ":" + value</c>.</summary>
    string Hash(string purpose, string value);

    /// <summary>Constant-time comparison of <see cref="Hash"/> output with an expected hex value.</summary>
    bool Verify(string purpose, string value, string expectedHash);
}

public static class HashPurposes
{
    public const string Phone = "phone";
    public const string Ip = "ip";
    public const string Email = "email";
    public const string OtpCode = "otp";
}
