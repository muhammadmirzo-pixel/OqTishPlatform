using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Common.Security;
using OqTish.Domain.Identity;

namespace OqTish.Application.Identity.Handlers;

public sealed record RequestOtpCommand(string? Phone);

public sealed record VerifyOtpCommand(Guid ChallengeId, string? Code, string? TotpCode);

/// <summary>
/// POST /api/auth/otp/request. Auth: anon. Sends a sign-in code to any valid Uzbek number (known or not:
/// the response is identical). Limits: see <see cref="OtpService"/>. Side effects: otp_challenges row, SMS.
/// </summary>
public sealed class RequestOtpHandler(OtpService otp, IKeyedHasher hasher)
{
    public async Task<Result<OtpRequestedDto>> HandleAsync(RequestOtpCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var phone = IdentifierRules.Phone(command.Phone);
        if (!phone.IsSuccess)
        {
            return phone.Error;
        }

        return await otp.IssueAsync(OtpPurpose.SignIn, hasher.Hash(HashPurposes.Phone, phone.Value), phone.Value, null, ct);
    }
}

/// <summary>
/// POST /api/auth/otp/verify. Auth: anon. Verifies the code and signs in the phone's account, creating a
/// patient user (Active, verified phone identity, patient profile) for a new number. Accounts with TOTP
/// enabled must also send <c>totpCode</c> (401 <c>auth.totp_required</c>). Side effects: session,
/// audit <c>auth.signed_in</c>, consumed challenge.
/// </summary>
public sealed class VerifyOtpHandler(
    IOqTishDb db,
    ITransactionRunner transactions,
    IRowLocks locks,
    IKeyedHasher hasher,
    OtpService otp,
    TotpService totp,
    SignInService signIn,
    TimeProvider clock)
{
    public async Task<Result<SignInOutcome>> HandleAsync(VerifyOtpCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var verified = await otp.VerifyAsync(command.ChallengeId, command.Code, OtpPurpose.SignIn, null, ct);
        if (!verified.IsSuccess)
        {
            return verified.Error;
        }

        var phone = verified.Value.Phone!;
        return await transactions.RunAsync<SignInOutcome>(async token =>
        {
            await locks.AdvisoryLockAsync("identity:phone:" + hasher.Hash(HashPurposes.Phone, phone), token);
            if (!await otp.ConsumeAsync(command.ChallengeId, token))
            {
                return AuthErrors.OtpInvalid;
            }

            var identity = await db.AuthIdentities
                .FirstOrDefaultAsync(i => i.Kind == AuthIdentityKind.Phone && i.NormalizedIdentifier == phone, token);
            User user;
            if (identity is null)
            {
                user = signIn.CreatePatientUser(null, phone);
                identity = AuthIdentity.Create(user.Id, AuthIdentityKind.Phone, phone, verified: true, clock.GetUtcNow());
                db.AuthIdentities.Add(identity);
            }
            else
            {
                user = await db.Users.FirstAsync(u => u.Id == identity.UserId, token);
                identity.MarkVerified(clock.GetUtcNow());
                if (user.Status == UserStatus.Active)
                {
                    var second = await totp.RequireSecondFactorAsync(user.Id, command.TotpCode, token);
                    if (!second.IsSuccess)
                    {
                        return second.Error;
                    }
                }
            }

            return await signIn.CompleteAsync(user, identity, AuthMethods.Otp, token);
        }, ct);
    }
}
