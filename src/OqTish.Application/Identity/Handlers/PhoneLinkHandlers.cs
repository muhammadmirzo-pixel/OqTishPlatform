using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Common.Security;
using OqTish.Domain.Identity;

namespace OqTish.Application.Identity.Handlers;

public sealed record RequestPhoneLinkCommand(string? Phone);

public sealed record VerifyPhoneLinkCommand(Guid ChallengeId, string? Code);

/// <summary>
/// POST /api/me/phone/request. Auth: user. Sends a LinkPhone code bound to the caller's account. Ownership of
/// the number by another account is NOT revealed here (only after proof of control, at verify).
/// </summary>
public sealed class RequestPhoneLinkHandler(OtpService otp, IKeyedHasher hasher, ICurrentUser currentUser)
{
    public async Task<Result<OtpRequestedDto>> HandleAsync(RequestPhoneLinkCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var phone = IdentifierRules.Phone(command.Phone);
        if (!phone.IsSuccess)
        {
            return phone.Error;
        }

        return await otp.IssueAsync(OtpPurpose.LinkPhone, hasher.Hash(HashPurposes.Phone, phone.Value), phone.Value, currentUser.RequiredUserId, ct);
    }
}

/// <summary>
/// POST /api/me/phone/verify. Auth: user. Links (or replaces) the caller's phone identity. A number owned by
/// another account → 409 <c>auth.identity_in_use</c>: nothing is merged, the challenge is consumed and an
/// audit row <c>identity.link_conflict</c> is committed for support. Side effects: audit <c>identity.phone_linked</c>.
/// </summary>
public sealed class VerifyPhoneLinkHandler(
    IOqTishDb db,
    ITransactionRunner transactions,
    IRowLocks locks,
    IKeyedHasher hasher,
    OtpService otp,
    MeQuery me,
    ICurrentUser currentUser,
    IAuditLog audit,
    TimeProvider clock)
{
    public async Task<Result<MeDto>> HandleAsync(VerifyPhoneLinkCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var userId = currentUser.RequiredUserId;
        var verified = await otp.VerifyAsync(command.ChallengeId, command.Code, OtpPurpose.LinkPhone, userId, ct);
        if (!verified.IsSuccess)
        {
            return verified.Error;
        }

        var phone = verified.Value.Phone!;
        var outcome = await transactions.RunAsync<bool>(async token =>
        {
            await locks.AdvisoryLockAsync("identity:phone:" + hasher.Hash(HashPurposes.Phone, phone), token);
            if (!await otp.ConsumeAsync(command.ChallengeId, token))
            {
                return AuthErrors.OtpInvalid;
            }

            var now = clock.GetUtcNow();
            var owner = await db.AuthIdentities
                .FirstOrDefaultAsync(i => i.Kind == AuthIdentityKind.Phone && i.NormalizedIdentifier == phone, token);
            if (owner is not null && owner.UserId != userId)
            {
                // Committed on purpose: the conflict is recorded for support; nothing is merged.
                audit.Record(new AuditEvent("identity.link_conflict", TargetType: "user", TargetId: owner.UserId.ToString(), Details: new { kind = "Phone" }));
                await db.SaveChangesAsync(token);
                return false;
            }

            if (owner is null)
            {
                var mine = await db.AuthIdentities.FirstOrDefaultAsync(i => i.UserId == userId && i.Kind == AuthIdentityKind.Phone, token);
                if (mine is null)
                {
                    db.AuthIdentities.Add(AuthIdentity.Create(userId, AuthIdentityKind.Phone, phone, verified: true, now));
                }
                else
                {
                    mine.ChangeIdentifier(phone, verified: true, now);
                }
            }
            else
            {
                owner.MarkVerified(now);
            }

            var profile = await db.PatientProfiles.FirstOrDefaultAsync(p => p.UserId == userId, token);
            if (profile is { ContactPhone: null })
            {
                profile.SetContactPhone(phone, now);
            }

            audit.Record(new AuditEvent("identity.phone_linked", TargetType: "user", TargetId: userId.ToString()));
            await db.SaveChangesAsync(token);
            return true;
        }, ct);

        if (!outcome.IsSuccess)
        {
            return outcome.Error;
        }

        return outcome.Value ? (await me.BuildAsync(userId, ct))! : AuthErrors.IdentityInUse;
    }
}
