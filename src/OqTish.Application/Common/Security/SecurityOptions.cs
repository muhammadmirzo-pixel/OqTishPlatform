namespace OqTish.Application.Common.Security;

/// <summary>Configuration section <c>Security</c>.</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>Minimum pepper length accepted outside Development.</summary>
    public const int MinPepperLength = 32;

    /// <summary>
    /// Server secret for HMAC of OTP codes and of identifiers (phone, IP) stored for matching/rate limits.
    /// Required outside Development (startup fails otherwise). Changing it invalidates outstanding OTPs and
    /// pending invitations only.
    /// </summary>
    public string OtpPepper { get; set; } = string.Empty;

    /// <summary>
    /// SECURITY.md §8: ClinicOwner/FinanceManager memberships and platform permissions take effect only
    /// after the user enabled TOTP. Default true; false only in appsettings.Development.json.
    /// </summary>
    public bool RequireTotpForPrivilegedRoles { get; set; } = true;
}
