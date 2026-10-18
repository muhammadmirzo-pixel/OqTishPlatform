using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Common.Security;
using OqTish.Domain.Identity;

namespace OqTish.Application.Identity;

/// <summary>
/// TOTP verification with replay protection: a code is accepted once, only for a step newer than the last
/// accepted one (conditional UPDATE of <c>last_used_step</c>, so two concurrent uses cannot both pass).
/// </summary>
public sealed class TotpService(IOqTishDb db, ISecretProtector protector, TimeProvider clock)
{
    public const string ProtectorPurpose = "OqTish.Identity.Totp.v1";

    public Task<bool> IsEnabledAsync(Guid userId, CancellationToken ct) =>
        db.TotpCredentials.AsNoTracking().AnyAsync(t => t.UserId == userId && t.EnabledAt != null, ct);

    /// <summary>
    /// Second factor at sign-in: succeeds immediately when the user has no TOTP; otherwise requires a valid,
    /// unused code (401 <c>auth.totp_required</c> / <c>auth.totp_invalid</c>).
    /// </summary>
    public async Task<Result> RequireSecondFactorAsync(Guid userId, string? code, CancellationToken ct)
    {
        if (!await IsEnabledAsync(userId, ct))
        {
            return Result.Success;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return AuthErrors.TotpRequired;
        }

        return await VerifyAndConsumeAsync(userId, code, AuthErrors.TotpInvalid, ct);
    }

    /// <summary>Verifies against the enabled secret and records the step. Returns <paramref name="invalid"/> on failure.</summary>
    public async Task<Result> VerifyAndConsumeAsync(Guid userId, string? code, Error invalid, CancellationToken ct)
    {
        var credential = await db.TotpCredentials.AsNoTracking()
            .FirstOrDefaultAsync(t => t.UserId == userId && t.EnabledAt != null, ct);
        if (credential is null || DecryptSecret(credential) is not { } secret)
        {
            return invalid;
        }

        if (Totp.Verify(secret, code, clock.GetUtcNow(), credential.LastUsedStep) is not { } step)
        {
            return invalid;
        }

        var updated = await db.TotpCredentials
            .Where(t => t.UserId == userId && (t.LastUsedStep == null || t.LastUsedStep < step))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.LastUsedStep, step), ct);
        return updated == 1 ? Result.Success : invalid;
    }

    public string Protect(byte[] secret) => protector.Protect(ProtectorPurpose, Base32.Encode(secret));

    public byte[]? DecryptSecret(TotpCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        return protector.Unprotect(ProtectorPurpose, credential.ProtectedSecret) is { } base32 ? Base32.Decode(base32) : null;
    }
}
