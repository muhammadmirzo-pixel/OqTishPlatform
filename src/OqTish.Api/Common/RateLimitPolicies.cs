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

    public static IServiceCollection AddOqTishRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var global = configuration.GetValue("RateLimiting:GlobalPermitPerMinute", 300);
        var otp = configuration.GetValue("RateLimiting:OtpSendPermitPerMinute", 10);
        var credentials = configuration.GetValue("RateLimiting:CredentialsPermitPerMinute", 20);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http => PerIp(http, global));
            options.AddPolicy(OtpSend, http => PerIp(http, otp));
            options.AddPolicy(Credentials, http => PerIp(http, credentials));
            options.OnRejected = (context, _) =>
            {
                var retry = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var after) ? after : TimeSpan.FromSeconds(60);
                context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retry.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                return new ValueTask(ProblemResults.WriteAsync(context.HttpContext, AuthErrors.RateLimited(retry)));
            };
        });
        return services;
    }

    private static RateLimitPartition<string> PerIp(HttpContext http, int permitPerMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
}
