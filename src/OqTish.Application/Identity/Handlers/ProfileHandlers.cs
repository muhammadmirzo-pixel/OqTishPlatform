using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Application.Common.Security;
using OqTish.Domain.Identity;

namespace OqTish.Application.Identity.Handlers;

public sealed record UpdateProfileCommand(string? FullName);

/// <param name="Email">Required when the account has no e-mail yet (first password); otherwise optional (changes the e-mail).</param>
public sealed record SetPasswordCommand(string? Email, string? CurrentPassword, string? NewPassword);

/// <summary>
/// PUT /api/me/profile. Auth: user. Creates or renames the patient profile and the display name.
/// Side effects: none besides the rows (no audit: not security relevant).
/// </summary>
public sealed class UpdateProfileHandler(IOqTishDb db, ICurrentUser currentUser, MeQuery me, TimeProvider clock)
{
    public async Task<Result<MeDto>> HandleAsync(UpdateProfileCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var name = IdentifierRules.FullName(command.FullName);
        if (!name.IsSuccess)
        {
            return name.Error;
        }

        var userId = currentUser.RequiredUserId;
        var now = clock.GetUtcNow();
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        var profile = await db.PatientProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null)
        {
            var phone = await db.AuthIdentities.AsNoTracking()
                .Where(i => i.UserId == userId && i.Kind == AuthIdentityKind.Phone && i.VerifiedAt != null)
                .Select(i => i.NormalizedIdentifier)
                .FirstOrDefaultAsync(ct);
            db.PatientProfiles.Add(PatientProfile.Create(userId, name.Value, phone, now));
        }
        else
        {
            profile.Rename(name.Value, now);
        }

        user.Rename(name.Value, now);
        await db.SaveChangesAsync(ct);
        return (await me.BuildAsync(userId, ct))!;
    }
}

/// <summary>
/// POST /api/me/password. Auth: user. Changing an existing password requires <c>currentPassword</c>
/// (400 <c>auth.current_password_invalid</c>). Setting the first password requires a verified phone
/// (422 <c>auth.phone_verification_required</c>) and an e-mail used as the login name (409
/// <c>auth.identity_in_use</c> if taken). Rotates the security stamp and revokes all other sessions.
/// Side effects: audit <c>auth.password.changed</c>.
/// </summary>
public sealed class SetPasswordHandler(
    IOqTishDb db,
    ITransactionRunner transactions,
    IRowLocks locks,
    IPasswordHashService passwords,
    IKeyedHasher hasher,
    SessionService sessions,
    ICurrentUser currentUser,
    IAuditLog audit,
    TimeProvider clock,
    IOptions<AuthOptions> options)
{
    public async Task<Result> HandleAsync(SetPasswordCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var policy = IdentifierRules.Password(command.NewPassword, options.Value.Password);
        if (!policy.IsSuccess)
        {
            return policy;
        }

        string? email = null;
        if (!string.IsNullOrWhiteSpace(command.Email))
        {
            var parsed = IdentifierRules.Email(command.Email);
            if (!parsed.IsSuccess)
            {
                return parsed.Error;
            }

            email = parsed.Value;
        }

        var userId = currentUser.RequiredUserId;
        var sessionId = currentUser.SessionId;
        return await transactions.RunAsync(async token =>
        {
            var now = clock.GetUtcNow();
            var credential = await db.PasswordCredentials.FirstOrDefaultAsync(c => c.UserId == userId, token);
            if (credential is not null)
            {
                if (string.IsNullOrEmpty(command.CurrentPassword) || !passwords.Verify(credential.PasswordHash, command.CurrentPassword).Valid)
                {
                    return AuthErrors.CurrentPasswordInvalid;
                }
            }
            else
            {
                var phoneVerified = await db.AuthIdentities.AnyAsync(
                    i => i.UserId == userId && i.Kind == AuthIdentityKind.Phone && i.VerifiedAt != null, token);
                if (!phoneVerified)
                {
                    return AuthErrors.PhoneVerificationRequired;
                }
            }

            var emailIdentity = await db.AuthIdentities
                .FirstOrDefaultAsync(i => i.UserId == userId && i.Kind == AuthIdentityKind.EmailPassword, token);
            if (emailIdentity is null && email is null)
            {
                return Error.InvalidField("email", "An e-mail address is required as the sign-in name.");
            }

            if (email is not null && emailIdentity?.NormalizedIdentifier != email)
            {
                await locks.AdvisoryLockAsync("identity:email:" + hasher.Hash(HashPurposes.Email, email), token);
                var taken = await db.AuthIdentities.AnyAsync(
                    i => i.Kind == AuthIdentityKind.EmailPassword && i.NormalizedIdentifier == email && i.UserId != userId, token);
                if (taken)
                {
                    return AuthErrors.IdentityInUse;
                }

                if (emailIdentity is null)
                {
                    db.AuthIdentities.Add(AuthIdentity.Create(userId, AuthIdentityKind.EmailPassword, email, verified: false, now));
                }
                else
                {
                    emailIdentity.ChangeIdentifier(email, verified: false, now);
                }
            }

            var hash = passwords.Hash(command.NewPassword!);
            if (credential is null)
            {
                db.PasswordCredentials.Add(PasswordCredential.Create(userId, hash, now));
            }
            else
            {
                credential.SetHash(hash, now);
            }

            var user = await db.Users.FirstAsync(u => u.Id == userId, token);
            user.RotateSecurityStamp(now);
            await sessions.RevokeAllAsync(userId, SessionRevocationReasons.PasswordChanged, sessionId, token);
            audit.Record(new AuditEvent("auth.password.changed", TargetType: "user", TargetId: userId.ToString(), Details: new { emailChanged = email is not null }));
            await db.SaveChangesAsync(token);
            if (sessionId is { } current)
            {
                await sessions.RefreshStampAsync(current, user.SecurityStamp, token);
            }

            return Result.Success;
        }, ct);
    }
}
