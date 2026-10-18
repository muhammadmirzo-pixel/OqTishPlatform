using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using OqTish.Application.Common.Security;
using OqTish.Infrastructure.Sms;

namespace OqTish.Infrastructure;

/// <summary>
/// Configuration checks that make the host refuse to start (CONVENTIONS §6):
/// <list type="bullet">
/// <item><c>Security:OtpPepper</c>: missing → only Development falls back to a built-in, public dev value;
/// elsewhere it must be set, ≥ 32 characters and not the dev value.</item>
/// <item><c>Sms:Provider</c>: <c>Fake</c> is refused in Production; empty means Fake in Development and is an
/// error elsewhere; <c>Eskiz</c> needs <c>Sms:Eskiz:Email</c> and <c>Sms:Eskiz:Password</c>.</item>
/// </list>
/// </summary>
public static class StartupGuards
{
    /// <summary>Public, Development-only pepper. Rejected outside Development.</summary>
    public const string DevelopmentOtpPepper = "development-only-otp-pepper-NOT-A-SECRET-change-me";

    public static (string Pepper, string SmsProvider) Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        var isDevelopment = environment.IsDevelopment();

        var pepper = configuration[$"{SecurityOptions.SectionName}:{nameof(SecurityOptions.OtpPepper)}"];
        if (string.IsNullOrWhiteSpace(pepper))
        {
            pepper = isDevelopment
                ? DevelopmentOtpPepper
                : throw new InvalidOperationException("Security:OtpPepper is not configured (set Security__OtpPepper; at least 32 random characters).");
        }
        else if (!isDevelopment && (pepper.Length < SecurityOptions.MinPepperLength || pepper == DevelopmentOtpPepper))
        {
            throw new InvalidOperationException($"Security:OtpPepper must be at least {SecurityOptions.MinPepperLength} random characters and not the development value.");
        }

        var provider = configuration[$"{SmsOptions.SectionName}:{nameof(SmsOptions.Provider)}"];
        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = isDevelopment
                ? SmsProviders.Fake
                : throw new InvalidOperationException("Sms:Provider is not configured (Eskiz, or Fake outside Production).");
        }

        if (string.Equals(provider, SmsProviders.Fake, StringComparison.OrdinalIgnoreCase))
        {
            if (environment.IsProduction())
            {
                throw new InvalidOperationException("Sms:Provider=Fake is not allowed in Production.");
            }

            return (pepper, SmsProviders.Fake);
        }

        if (string.Equals(provider, SmsProviders.Eskiz, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(configuration["Sms:Eskiz:Email"]) || string.IsNullOrWhiteSpace(configuration["Sms:Eskiz:Password"]))
            {
                throw new InvalidOperationException("Sms:Provider=Eskiz requires Sms:Eskiz:Email and Sms:Eskiz:Password.");
            }

            return (pepper, SmsProviders.Eskiz);
        }

        throw new InvalidOperationException($"Unknown Sms:Provider '{provider}'. Use Eskiz or Fake.");
    }
}
