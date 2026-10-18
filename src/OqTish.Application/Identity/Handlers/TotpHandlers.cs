using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Domain.Identity;

namespace OqTish.Application.Identity.Handlers;

public sealed record TotpCodeCommand(string? Code);

/// <summary>
/// POST /api/me/totp/setup | enable | disable. Auth: user.
/// <list type="bullet">
/// <item>setup: new 160-bit secret stored encrypted as a pending credential → <c>{ secret, otpauthUri }</c>;
/// 409 <c>auth.totp_already_enabled</c> when enabled.</item>
/// <item>enable: a valid code for the pending secret enables it, revokes every other session; audit
/// <c>auth.totp.enabled</c>. Wrong code → 400 <c>auth.totp_invalid</c>; no pending secret → 409
/// <c>auth.totp_not_enabled</c>.</item>
/// <item>disable: a valid, unused code removes the credential; audit <c>auth.totp.disabled</c>.</item>
/// </list>
/// </summary>
public sealed class TotpHandler(
    IOqTishDb db,
    ITransactionRunner transactions,
    TotpService totp,
    SessionService sessions,
    ICurrentUser currentUser,
    IAuditLog audit,
    TimeProvider clock)
{
    public const string Issuer = "OqTish";

    public async Task<Result<TotpSetupDto>> SetupAsync(CancellationToken ct)
    {
        var userId = currentUser.RequiredUserId;
        var credential = await db.TotpCredentials.FirstOrDefaultAsync(t => t.UserId == userId, ct);
        if (credential is { IsEnabled: true })
        {
            return AuthErrors.TotpAlreadyEnabled;
        }

        var secret = Totp.GenerateSecret();
        var protectedSecret = totp.Protect(secret);
        if (credential is null)
        {
            db.TotpCredentials.Add(TotpCredential.CreatePending(userId, protectedSecret, clock.GetUtcNow()));
        }
        else
        {
            credential.ReplacePendingSecret(protectedSecret, clock.GetUtcNow());
        }

        await db.SaveChangesAsync(ct);

        var identities = await db.AuthIdentities.AsNoTracking()
            .Where(i => i.UserId == userId && (i.Kind == AuthIdentityKind.EmailPassword || i.Kind == AuthIdentityKind.Phone))
            .Select(i => new { i.Kind, i.NormalizedIdentifier })
            .ToListAsync(ct);
        var label = identities.FirstOrDefault(i => i.Kind == AuthIdentityKind.EmailPassword)?.NormalizedIdentifier
            ?? (identities.FirstOrDefault(i => i.Kind == AuthIdentityKind.Phone) is { } phone ? PhoneNumber.Mask(phone.NormalizedIdentifier) : "account");
        var base32 = Base32.Encode(secret);
        return new TotpSetupDto(base32, Totp.BuildUri(Issuer, label, base32));
    }

    public Task<Result> EnableAsync(TotpCodeCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var userId = currentUser.RequiredUserId;
        var sessionId = currentUser.SessionId;
        return transactions.RunAsync(async token =>
        {
            var credential = await db.TotpCredentials.FirstOrDefaultAsync(t => t.UserId == userId, token);
            if (credential is null)
            {
                return AuthErrors.TotpNotEnabled;
            }

            if (credential.IsEnabled)
            {
                return AuthErrors.TotpAlreadyEnabled;
            }

            if (totp.DecryptSecret(credential) is not { } secret
                || Totp.Verify(secret, command.Code, clock.GetUtcNow(), null) is not { } step)
            {
                return AuthErrors.TotpInvalidForSignedIn;
            }

            credential.Enable(step, clock.GetUtcNow());
            await sessions.RevokeAllAsync(userId, SessionRevocationReasons.TotpEnabled, sessionId, token);
            audit.Record(new AuditEvent("auth.totp.enabled", TargetType: "user", TargetId: userId.ToString()));
            await db.SaveChangesAsync(token);
            return Result.Success;
        }, ct);
    }

    public Task<Result> DisableAsync(TotpCodeCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var userId = currentUser.RequiredUserId;
        return transactions.RunAsync(async token =>
        {
            if (!await totp.IsEnabledAsync(userId, token))
            {
                return AuthErrors.TotpNotEnabled;
            }

            var verified = await totp.VerifyAndConsumeAsync(userId, command.Code, AuthErrors.TotpInvalidForSignedIn, token);
            if (!verified.IsSuccess)
            {
                return verified;
            }

            await db.TotpCredentials.Where(t => t.UserId == userId).ExecuteDeleteAsync(token);
            audit.Record(new AuditEvent("auth.totp.disabled", TargetType: "user", TargetId: userId.ToString()));
            await db.SaveChangesAsync(token);
            return Result.Success;
        }, ct);
    }
}
