using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using OqTish.Application.Common.Security;
using OqTish.Application.Identity.Telegram;
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

    /// <summary>
    /// Telegram checks: <c>Telegram:Client</c> = Real | Fake (empty → Fake in Development, Real elsewhere; Fake is refused
    /// in Production); <c>Telegram:Mode</c> = Polling | Webhook; when Telegram is enabled in Webhook mode,
    /// <c>Telegram:WebhookSecret</c> (32–256 chars of A–Z a–z 0–9 _ -) and an absolute HTTPS <c>Telegram:WebhookBaseUrl</c>
    /// are required. Returns the client kind.
    /// </summary>
    public static string ValidateTelegram(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        var client = configuration["Telegram:Client"];
        if (string.IsNullOrWhiteSpace(client))
        {
            client = environment.IsDevelopment() ? TelegramClients.Fake : TelegramClients.Real;
        }
        else if (string.Equals(client, TelegramClients.Fake, StringComparison.OrdinalIgnoreCase))
        {
            client = TelegramClients.Fake;
        }
        else if (string.Equals(client, TelegramClients.Real, StringComparison.OrdinalIgnoreCase))
        {
            client = TelegramClients.Real;
        }
        else
        {
            throw new InvalidOperationException($"Unknown Telegram:Client '{client}'. Use Real or Fake.");
        }

        if (client == TelegramClients.Fake && environment.IsProduction())
        {
            throw new InvalidOperationException("Telegram:Client=Fake is not allowed in Production.");
        }

        var mode = configuration["Telegram:Mode"];
        var webhook = string.Equals(mode, TelegramModes.Webhook, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(mode) && !webhook && !string.Equals(mode, TelegramModes.Polling, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unknown Telegram:Mode '{mode}'. Use Polling or Webhook.");
        }

        var enabled = bool.TryParse(configuration["Telegram:Enabled"], out var e) && e;
        if (enabled && webhook)
        {
            var secret = configuration["Telegram:WebhookSecret"] ?? string.Empty;
            if (secret.Length is < 32 or > 256 || !secret.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-'))
            {
                throw new InvalidOperationException("Telegram:Mode=Webhook requires Telegram:WebhookSecret (32-256 characters of A-Z, a-z, 0-9, '_' and '-').");
            }

            if (!Uri.TryCreate(configuration["Telegram:WebhookBaseUrl"], UriKind.Absolute, out var baseUrl) || baseUrl.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("Telegram:Mode=Webhook requires an absolute https Telegram:WebhookBaseUrl.");
            }
        }

        return client;
    }
}
