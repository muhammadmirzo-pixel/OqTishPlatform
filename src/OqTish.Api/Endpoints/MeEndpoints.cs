using OqTish.Api.Auth;
using OqTish.Api.Common;
using OqTish.Application.Common;
using OqTish.Application.Identity;
using OqTish.Application.Identity.Handlers;

namespace OqTish.Api.Endpoints;

/// <summary>/api/me — the signed-in user's account (docs/API.md §1). Invitations are mapped by the Staff module.</summary>
public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/api/me").WithTags("Me");

        me.MapGet("", async (MeQuery query, HttpContext http, CancellationToken ct) =>
                (await query.HandleAsync(ct)).ToHttp(http, TypedResults.Ok))
            .RequireAuthorization()
            .WithName("GetMe").WithSummary("Current user, memberships with effective permissions, platform permissions")
            .Produces<MeDto>().ProducesProblem(401);

        me.MapPut("/profile", async (UpdateProfileCommand body, UpdateProfileHandler handler, HttpContext http, CancellationToken ct) =>
                (await handler.HandleAsync(body, ct)).ToHttp(http, TypedResults.Ok))
            .RequireAuthorization()
            .WithName("UpdateProfile").WithSummary("Set the patient display name")
            .Produces<MeDto>().ProducesProblem(400).ProducesProblem(401);

        me.MapPut("/avatar", async (AvatarHandler handler, HttpContext http, CancellationToken ct) =>
                (await handler.SetAsync(await ImageUploadForm.ReadAsync(http.Request, ct), ct)).ToHttp(http, TypedResults.Ok))
            .RequireAuthorization()
            .AcceptsImageUpload()
            .WithName("SetAvatar").WithSummary("Upload/replace the own avatar: multipart field 'file', JPEG/PNG/WebP ≤ 2 MB")
            .Produces<AvatarDto>().ProducesProblem(400).ProducesProblem(401);

        me.MapDelete("/avatar", async (AvatarHandler handler, HttpContext http, CancellationToken ct) =>
                (await handler.RemoveAsync(ct)).ToHttp(http, TypedResults.NoContent))
            .RequireAuthorization()
            .WithName("RemoveAvatar").WithSummary("Remove the own avatar")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(401);

        me.MapPost("/password", async (SetPasswordCommand body, SetPasswordHandler handler, HttpContext http, CancellationToken ct) =>
                (await handler.HandleAsync(body, ct)).ToHttp(http, TypedResults.NoContent))
            .RequireAuthorization().RequireRateLimiting(RateLimitPolicies.Credentials)
            .WithName("SetPassword").WithSummary("Set or change the password (first time: needs a verified phone and an e-mail)")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(400).ProducesProblem(401).ProducesProblem(409).ProducesProblem(422);

        me.MapPost("/phone/request", async (RequestPhoneLinkCommand body, RequestPhoneLinkHandler handler, HttpContext http, CancellationToken ct) =>
                (await handler.HandleAsync(body, ct)).ToHttp(http, r => TypedResults.Accepted((string?)null, r)))
            .RequireAuthorization().RequireRateLimiting(RateLimitPolicies.OtpSend)
            .WithName("RequestPhoneLink").WithSummary("Send a code to prove control of a phone")
            .Produces<OtpRequestedDto>(StatusCodes.Status202Accepted).ProducesProblem(400).ProducesProblem(401).ProducesProblem(429);

        me.MapPost("/phone/verify", async (VerifyPhoneLinkCommand body, VerifyPhoneLinkHandler handler, HttpContext http, CancellationToken ct) =>
                (await handler.HandleAsync(body, ct)).ToHttp(http, TypedResults.Ok))
            .RequireAuthorization().RequireRateLimiting(RateLimitPolicies.Credentials)
            .WithName("VerifyPhoneLink").WithSummary("Link the proven phone (409 auth.identity_in_use if another account owns it; never merged)")
            .Produces<MeDto>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(409);

        me.MapPost("/totp/setup", async (TotpHandler handler, HttpContext http, CancellationToken ct) =>
                (await handler.SetupAsync(ct)).ToHttp(http, TypedResults.Ok))
            .RequireAuthorization()
            .WithName("SetupTotp").WithSummary("Create a pending authenticator secret")
            .Produces<TotpSetupDto>().ProducesProblem(401).ProducesProblem(409);

        me.MapPost("/totp/enable", async (TotpCodeCommand body, TotpHandler handler, HttpContext http, CancellationToken ct) =>
                (await handler.EnableAsync(body, ct)).ToHttp(http, TypedResults.NoContent))
            .RequireAuthorization().RequireRateLimiting(RateLimitPolicies.Credentials)
            .WithName("EnableTotp").WithSummary("Enable TOTP with a code from the authenticator; revokes other sessions")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(400).ProducesProblem(401).ProducesProblem(409);

        me.MapPost("/totp/disable", async (TotpCodeCommand body, TotpHandler handler, HttpContext http, CancellationToken ct) =>
                (await handler.DisableAsync(body, ct)).ToHttp(http, TypedResults.NoContent))
            .RequireAuthorization().RequireRateLimiting(RateLimitPolicies.Credentials)
            .WithName("DisableTotp").WithSummary("Disable TOTP with a valid code")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(400).ProducesProblem(401).ProducesProblem(409);

        me.MapPost("/telegram/link", async (TelegramLinkService handler, HttpContext http, CancellationToken ct) =>
                (await handler.CreateLinkAsync(ct)).ToHttp(http, TypedResults.Ok))
            .RequireAuthorization()
            .WithName("CreateTelegramLink").WithSummary("One-time bot deep link (10 min) that links Telegram to this account")
            .Produces<TelegramLinkDto>().ProducesProblem(401).ProducesProblem(422);

        me.MapDelete("/telegram", async (TelegramLinkService handler, HttpContext http, CancellationToken ct) =>
                (await handler.UnlinkAsync(ct)).ToHttp(http, TypedResults.NoContent))
            .RequireAuthorization()
            .WithName("UnlinkTelegram").WithSummary("Unlink Telegram (refused when it is the only sign-in method)")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(401).ProducesProblem(422);

        me.MapGet("/sessions", async (SessionsHandler handler, HttpContext http, CancellationToken ct) =>
                (await handler.ListAsync(ct)).ToHttp(http, TypedResults.Ok))
            .RequireAuthorization()
            .WithName("ListSessions").WithSummary("Active sessions of the current user")
            .Produces<IReadOnlyList<SessionDto>>().ProducesProblem(401);

        me.MapDelete("/sessions/{id:guid}", async (Guid id, SessionsHandler handler, ICurrentUser user, SessionCookie cookie, HttpContext http, CancellationToken ct) =>
                (await handler.RevokeAsync(id, ct)).ToHttp(http, () => id == user.SessionId ? AuthEndpoints.SignedOut(cookie, http) : TypedResults.NoContent()))
            .RequireAuthorization()
            .WithName("RevokeSession").WithSummary("Revoke one of the current user's sessions")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(401).ProducesProblem(404);

        me.MapPost("/delete", async (DeleteAccountHandler handler, SessionCookie cookie, HttpContext http, CancellationToken ct) =>
                (await handler.HandleAsync(ct)).ToHttp(http, () => AuthEndpoints.SignedOut(cookie, http)))
            .RequireAuthorization()
            .WithName("DeleteAccount").WithSummary("Anonymise the account (OD-18); staff must leave clinics first")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(401).ProducesProblem(422);

        return app;
    }
}
