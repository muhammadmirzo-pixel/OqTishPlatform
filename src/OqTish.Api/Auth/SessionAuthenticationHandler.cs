using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using OqTish.Api.Common;
using OqTish.Application.Common;
using OqTish.Application.Identity;

namespace OqTish.Api.Auth;

/// <summary>
/// Authenticates every request from the session cookie against <c>identity.sessions</c>
/// (<see cref="SessionService.ValidateAsync"/>): revoked, expired, idle, stamp-mismatched sessions and
/// suspended/deleted users fail immediately, so there is no stale access after revocation. When the
/// sliding expiry was extended, the cookie is re-issued. Challenge → 401 <c>auth.unauthenticated</c>,
/// forbid → 403 <c>common.forbidden</c> (ProblemDetails).
/// </summary>
internal sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    SessionService sessions,
    SessionCookie cookie) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Session";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = SessionCookie.Read(Request);
        if (token is null)
        {
            return AuthenticateResult.NoResult();
        }

        var session = await sessions.ValidateAsync(token, Context.RequestAborted);
        if (session is null)
        {
            cookie.Delete(Context);
            return AuthenticateResult.Fail("Session is not valid.");
        }

        if (session.Renewed)
        {
            cookie.Append(Context, token, session.ExpiresAt);
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, session.UserId.ToString()),
                new Claim(HttpRequestContext.SessionClaim, session.SessionId.ToString()),
            ],
            SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        ProblemResults.WriteAsync(Context, AuthErrors.Unauthenticated);

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        ProblemResults.WriteAsync(Context, CommonErrors.Forbidden);
}
