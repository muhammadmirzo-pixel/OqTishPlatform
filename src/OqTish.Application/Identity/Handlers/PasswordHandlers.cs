using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Application.Common.Security;
using OqTish.Domain.Identity;

namespace OqTish.Application.Identity.Handlers;

public sealed record PasswordLoginCommand(string? Email, string? Password, string? TotpCode);

public sealed record RequestPasswordResetCommand(string? Email);

public sealed record ConfirmPasswordResetCommand(Guid ChallengeId, string? Code, string? NewPassword);

/// <summary>
/// POST /api/auth/password/login. Auth: anon. E-mail + password (+ TOTP when enabled). Unknown e-mail,
/// wrong password and a locked account all answer 401 <c>auth.invalid_credentials</c> (a dummy hash is
/// verified for unknown e-mails to equalise timing). 10 consecutive failures (password or TOTP) lock the
/// password sign-in for 15 min. Side effects: session, audit <c>auth.signed_in</c> /
/// <c>auth.password.login_failed</c>.
/// </summary>
public sealed class PasswordLoginHandler(
    IOqTishDb db,
    ITransactionRunner transactions,
    IPasswordHashService passwords,
    TotpService totp,
    SignInService signIn,
    IAuditLog audit,
    TimeProvider clock,
    IOptions<AuthOptions> options)
{
    public async Task<Result<SignInOutcome>> HandleAsync(PasswordLoginCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var email = IdentifierRules.Email(command.Email);
        if (!email.IsSuccess || string.IsNullOrEmpty(command.Password) || command.Password.Length > options.Value.Password.MaxLength)
        {
            return AuthErrors.InvalidCredentials;
        }

        var identity = await db.AuthIdentities
            .FirstOrDefaultAsync(i => i.Kind == AuthIdentityKind.EmailPassword && i.NormalizedIdentifier == email.Value, ct);
        var credential = identity is null ? null : await db.PasswordCredentials.FirstOrDefaultAsync(c => c.UserId == identity.UserId, ct);
        if (identity is null || credential is null)
        {
            passwords.VerifyDummy(command.Password);
            return AuthErrors.InvalidCredentials;
        }

        var now = clock.GetUtcNow();
        var (valid, needsRehash) = passwords.Verify(credential.PasswordHash, command.Password);
        if (credential.IsLockedOut(now) || !valid)
        {
            return await FailAsync(credential, identity.UserId, "password", ct);
        }

        var user = await db.Users.FirstAsync(u => u.Id == identity.UserId, ct);
        if (user.Status != UserStatus.Active)
        {
            return AuthErrors.AccountSuspended;
        }

        var second = await totp.RequireSecondFactorAsync(user.Id, command.TotpCode, ct);
        if (!second.IsSuccess)
        {
            return second.Error.Code == AuthErrors.TotpRequired.Code ? second.Error : await FailAsync(credential, user.Id, "totp", ct);
        }

        return await transactions.RunAsync<SignInOutcome>(async token =>
        {
            credential.RegisterSuccess(clock.GetUtcNow());
            if (needsRehash)
            {
                credential.SetHash(passwords.Hash(command.Password), clock.GetUtcNow());
            }

            return await signIn.CompleteAsync(user, identity, AuthMethods.Password, token);
        }, ct);
    }

    private async Task<Error> FailAsync(PasswordCredential credential, Guid userId, string factor, CancellationToken ct)
    {
        var p = options.Value.Password;
        credential.RegisterFailure(clock.GetUtcNow(), p.MaxFailedAttempts, TimeSpan.FromMinutes(p.LockoutMinutes));
        audit.Record(new AuditEvent("auth.password.login_failed", TargetType: "user", TargetId: userId.ToString(), Details: new { factor }, ActorUserIdOverride: userId));
        await db.SaveChangesAsync(ct);
        return factor == "totp" ? AuthErrors.TotpInvalid : AuthErrors.InvalidCredentials;
    }
}

/// <summary>
/// POST /api/auth/password/reset/request (OD-22). Auth: anon. Always 202 with a challenge id: for an
/// account with a verified phone the code is sent there; otherwise a decoy challenge (never verifiable) is
/// created, so the response and the rate limits (per e-mail) are identical. Side effects: challenge, SMS.
/// </summary>
public sealed class RequestPasswordResetHandler(IOqTishDb db, OtpService otp, IKeyedHasher hasher)
{
    public async Task<Result<PasswordResetRequestedDto>> HandleAsync(RequestPasswordResetCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var email = IdentifierRules.Email(command.Email);
        if (!email.IsSuccess)
        {
            return email.Error;
        }

        var target = await (
                from e in db.AuthIdentities.AsNoTracking()
                join p in db.AuthIdentities.AsNoTracking() on e.UserId equals p.UserId
                join u in db.Users.AsNoTracking() on e.UserId equals u.Id
                where e.Kind == AuthIdentityKind.EmailPassword && e.NormalizedIdentifier == email.Value
                    && p.Kind == AuthIdentityKind.Phone && p.VerifiedAt != null
                    && u.Status == UserStatus.Active
                select new { e.UserId, Phone = p.NormalizedIdentifier })
            .FirstOrDefaultAsync(ct);

        var issued = await otp.IssueAsync(
            OtpPurpose.PasswordReset,
            hasher.Hash(HashPurposes.Email, email.Value),
            target?.Phone,
            target?.UserId,
            ct);
        return issued.Map(i => new PasswordResetRequestedDto(i.ChallengeId));
    }
}

/// <summary>
/// POST /api/auth/password/reset/confirm. Auth: anon. Verifies the code, sets the new password, rotates the
/// security stamp and revokes every session. Side effects: audit <c>auth.password.reset</c>.
/// </summary>
public sealed class ConfirmPasswordResetHandler(
    IOqTishDb db,
    ITransactionRunner transactions,
    OtpService otp,
    IPasswordHashService passwords,
    SessionService sessions,
    IAuditLog audit,
    TimeProvider clock,
    IOptions<AuthOptions> options)
{
    public async Task<Result> HandleAsync(ConfirmPasswordResetCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var policy = IdentifierRules.Password(command.NewPassword, options.Value.Password);
        if (!policy.IsSuccess)
        {
            return policy;
        }

        var verified = await otp.VerifyAsync(command.ChallengeId, command.Code, OtpPurpose.PasswordReset, null, ct);
        if (!verified.IsSuccess)
        {
            return verified.Error;
        }

        var userId = verified.Value.UserId!.Value;
        return await transactions.RunAsync(async token =>
        {
            if (!await otp.ConsumeAsync(command.ChallengeId, token))
            {
                return AuthErrors.OtpInvalid;
            }

            var now = clock.GetUtcNow();
            var user = await db.Users.FirstAsync(u => u.Id == userId, token);
            var hash = passwords.Hash(command.NewPassword!);
            var credential = await db.PasswordCredentials.FirstOrDefaultAsync(c => c.UserId == userId, token);
            if (credential is null)
            {
                db.PasswordCredentials.Add(PasswordCredential.Create(userId, hash, now));
            }
            else
            {
                credential.SetHash(hash, now);
            }

            user.RotateSecurityStamp(now);
            await sessions.RevokeAllAsync(userId, SessionRevocationReasons.PasswordReset, null, token);
            audit.Record(new AuditEvent("auth.password.reset", TargetType: "user", TargetId: userId.ToString(), ActorUserIdOverride: userId));
            await db.SaveChangesAsync(token);
            return Result.Success;
        }, ct);
    }
}
