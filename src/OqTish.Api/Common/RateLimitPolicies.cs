using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using OqTish.Application.Identity;

namespace OqTish.Api.Common;

/// <summary>
/// ASP.NET Core rate limiter policies (per client IP, fixed 1-minute windows), on top of the database
/// limits for OTP (per phone/IP per 15 min) and the per-account password lockout.
/// Config <c>RateLimiting:*</c>; rejection → 429 <c>auth.rate_limited</c> + Retry-After.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>OTP sends (sign-in, phone link, password reset request).</summary>
    public const string OtpSend = "auth-otp";

    /// <summary>Code/credential checks (OTP verify, password login, reset confirm, Telegram sign-in).</summary>
    public const string Credentials = "auth-credentials";

    /// <summary>Patient booking requests (POST /appointments), per signed-in user per hour (SECURITY.md §4).</summary>
    public const string Booking = "booking";

    public static IServiceCollection AddOqTishRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var global = configuration.GetValue("RateLimiting:GlobalPermitPerMinute", 300);
        var otp = configuration.GetValue("RateLimiting:OtpSendPermitPerMinute", 10);
        var credentials = configuration.GetValue("RateLimiting:CredentialsPermitPerMinute", 20);
        var booking = configuration.GetValue("RateLimiting:BookingPermitPerHour", 10);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http => PerIp(http, global));
            options.AddPolicy(OtpSend, http => PerIp(http, otp));
            options.AddPolicy(Credentials, http => PerIp(http, credentials));
            options.AddPolicy(Booking, http => RateLimitPartition.GetFixedWindowLimiter(
                SessionPartition(http),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = booking, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
            options.OnRejected = (context, _) =>
            {
                var retry = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var after) ? after : TimeSpan.FromSeconds(60);
                context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retry.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                return new ValueTask(ProblemResults.WriteAsync(context.HttpContext, AuthErrors.RateLimited(retry)));
            };
        });
        return services;
    }

    /// <summary>
    /// The limiter runs before authentication, so the booking bucket is keyed by a hash of the session cookie
    /// (one bucket per signed-in session; unauthenticated calls are rejected later with 401) or the IP.
    /// </summary>
    private static string SessionPartition(HttpContext http) =>
        Auth.SessionCookie.Read(http.Request) is { } token
            ? "s:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)))[..32]
            : "ip:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown");

    private static RateLimitPartition<string> PerIp(HttpContext http, int permitPerMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
}
