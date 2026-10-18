using System.Text.Json;
using OqTish.Api.Auth;
using OqTish.Api.Common;
using OqTish.Application.Identity;
using OqTish.Application.Identity.Handlers;

namespace OqTish.Api.Endpoints;

/// <summary>/api/auth — sign-in, password reset, logout (docs/API.md §1). No business logic here.</summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth");

        auth.MapPost("/otp/request", async (RequestOtpCommand body, RequestOtpHandler handler, HttpContext http, CancellationToken ct) =>
                (await handler.HandleAsync(body, ct)).ToHttp(http, r => TypedResults.Accepted((string?)null, r)))
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.OtpSend)
            .WithName("RequestOtp").WithSummary("Send a sign-in code by SMS (identical response for known and unknown numbers)")
            .Produces<OtpRequestedDto>(StatusCodes.Status202Accepted).ProducesProblem(400).ProducesProblem(429);

        auth.MapPost("/otp/verify", async (VerifyOtpCommand body, VerifyOtpHandler handler, SessionCookie cookie, HttpContext http, CancellationToken ct) =>
                SignedIn(await handler.HandleAsync(body, ct), cookie, http))
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Credentials)
            .WithName("VerifyOtp").WithSummary("Verify the SMS code and sign in (creates a patient account for a new number)")
            .Produces<MeDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(429);

        auth.MapPost("/password/login", async (PasswordLoginCommand body, PasswordLoginHandler handler, SessionCookie cookie, HttpContext http, CancellationToken ct) =>
                SignedIn(await handler.HandleAsync(body, ct), cookie, http))
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Credentials)
            .WithName("PasswordLogin").WithSummary("E-mail + password (+ TOTP) sign-in for staff and platform admins")
            .Produces<MeDto>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(429);

        auth.MapPost("/password/reset/request", async (RequestPasswordResetCommand body, RequestPasswordResetHandler handler, HttpContext http, CancellationToken ct) =>
                (await handler.HandleAsync(body, ct)).ToHttp(http, r => TypedResults.Accepted((string?)null, r)))
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.OtpSend)
            .WithName("RequestPasswordReset").WithSummary("Send a reset code to the account's verified phone (always 202)")
            .Produces<PasswordResetRequestedDto>(StatusCodes.Status202Accepted).ProducesProblem(400).ProducesProblem(429);

        auth.MapPost("/password/reset/confirm", async (ConfirmPasswordResetCommand body, ConfirmPasswordResetHandler handler, HttpContext http, CancellationToken ct) =>
                (await handler.HandleAsync(body, ct)).ToHttp(http, TypedResults.NoContent))
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Credentials)
            .WithName("ConfirmPasswordReset").WithSummary("Set a new password with the reset code; revokes all sessions")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(400).ProducesProblem(429);

        auth.MapPost("/telegram/webapp", async (TelegramWebAppSignInCommand body, TelegramSignInHandler handler, SessionCookie cookie, HttpContext http, CancellationToken ct) =>
                SignedIn(await handler.HandleAsync(body, ct), cookie, http))
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Credentials)
            .WithName("TelegramWebAppSignIn").WithSummary("Telegram Mini App sign-in (initData HMAC, at most 24 h old)")
            .Produces<MeDto>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(422).ProducesProblem(429);

        auth.MapPost("/telegram/widget", async (Dictionary<string, JsonElement> body, TelegramSignInHandler handler, SessionCookie cookie, HttpContext http, CancellationToken ct) =>
                SignedIn(await handler.HandleAsync(ToWidgetCommand(body), ct), cookie, http))
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Credentials)
            .WithName("TelegramWidgetSignIn").WithSummary("Telegram Login Widget sign-in (fields as received from Telegram, plus optional totpCode)")
            .Produces<MeDto>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(422).ProducesProblem(429);

        auth.MapPost("/logout", async (LogoutHandler handler, SessionCookie cookie, HttpContext http, CancellationToken ct) =>
                (await handler.LogoutAsync(ct)).ToHttp(http, () => SignedOut(cookie, http)))
            .RequireAuthorization()
            .WithName("Logout").WithSummary("Revoke the current session")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(401);

        auth.MapPost("/logout-all", async (LogoutHandler handler, SessionCookie cookie, HttpContext http, CancellationToken ct) =>
                (await handler.LogoutAllAsync(ct)).ToHttp(http, () => SignedOut(cookie, http)))
            .RequireAuthorization()
            .WithName("LogoutAll").WithSummary("Revoke every session of the current user")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(401);

        return app;
    }

    internal static IResult SignedIn(OqTish.Application.Common.Result<SignInOutcome> result, SessionCookie cookie, HttpContext http) =>
        result.ToHttp(http, outcome =>
        {
            cookie.Append(http, outcome.SessionToken, outcome.ExpiresAt);
            return TypedResults.Ok(outcome.Me);
        });

    internal static IResult SignedOut(SessionCookie cookie, HttpContext http)
    {
        cookie.Delete(http);
        return TypedResults.NoContent();
    }

    /// <summary>Widget fields keep Telegram's exact string form (numbers as raw text) for the hash check.</summary>
    private static TelegramWidgetSignInCommand ToWidgetCommand(Dictionary<string, JsonElement> body)
    {
        string? totp = null;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in body)
        {
            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
                _ => null,
            };
            if (text is null)
            {
                continue;
            }

            if (key == "totpCode")
            {
                totp = text;
            }
            else
            {
                fields[key] = text;
            }
        }

        return new TelegramWidgetSignInCommand(fields, totp);
    }
}
