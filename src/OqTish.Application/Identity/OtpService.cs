using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Application.Common.Security;
using OqTish.Application.Identity.Sms;
using OqTish.Domain.Identity;

namespace OqTish.Application.Identity;

/// <summary>
/// Issues and verifies SMS one-time codes (SECURITY.md §1/§4).
/// <list type="bullet">
/// <item>Codes: 6 random digits, stored as HMAC-SHA256(<c>Security:OtpPepper</c>, challengeId:code).</item>
/// <item>Limits per identifier (phone, or e-mail for password reset): one code per 60 s, 3 per 15 min;
/// per client IP: 10 per 15 min → 429 <c>auth.rate_limited</c> with Retry-After. Checked under a
/// transaction-scoped advisory lock so concurrent requests cannot exceed them.</item>
/// <item>Verification: every submission counts as an attempt (atomic conditional UPDATE); 5 attempts, 5-min
/// TTL; the challenge is consumed by <see cref="ConsumeAsync"/> inside the caller's transaction.</item>
/// <item>The SMS is sent after commit, directly (not via the outbox). A send failure is logged without
/// the phone and the response is unchanged, so callers cannot learn whether a number exists.</item>
/// </list>
/// </summary>
public sealed partial class OtpService(
    IOqTishDb db,
    ITransactionRunner transactions,
    IRowLocks locks,
    IKeyedHasher hasher,
    ISmsSender sms,
    IRequestContext request,
    TimeProvider clock,
    IOptions<AuthOptions> options,
    ILogger<OtpService> logger)
{
    private OtpOptions Options => options.Value.Otp;

    /// <summary>
    /// Creates a challenge and sends the code to <paramref name="phone"/>. With <paramref name="phone"/> null a
    /// decoy challenge is created that can never be verified (unknown account in password reset).
    /// </summary>
    /// <param name="identifierHash">Rate-limit identity: the keyed hash of the phone or e-mail.</param>
    public async Task<Result<OtpRequestedDto>> IssueAsync(
        OtpPurpose purpose,
        string identifierHash,
        string? phone,
        Guid? userId,
        CancellationToken ct)
    {
        var code = SecureTokens.NumericCode(Options.CodeLength);
        var challengeId = OtpChallenge.NewId();
        var ipHash = hasher.Hash(HashPurposes.Ip, request.RemoteIp ?? "unknown");

        var issued = await transactions.RunAsync<OtpRequestedDto>(async token =>
        {
            await locks.AdvisoryLockAsync("otp:" + identifierHash, token);
            var now = clock.GetUtcNow();
            var windowStart = now - TimeSpan.FromMinutes(Options.WindowMinutes);

            var recent = await db.OtpChallenges.AsNoTracking()
                .Where(c => c.IdentifierHash == identifierHash && c.CreatedAt > windowStart)
                .Select(c => c.CreatedAt)
                .ToListAsync(token);

            if (recent.Count > 0)
            {
                var sinceLast = now - recent.Max();
                var cooldown = TimeSpan.FromSeconds(Options.ResendCooldownSeconds);
                if (sinceLast < cooldown)
                {
                    return AuthErrors.RateLimited(cooldown - sinceLast);
                }
            }

            if (recent.Count >= Options.MaxPerPhone)
            {
                return AuthErrors.RateLimited(recent.Min() + TimeSpan.FromMinutes(Options.WindowMinutes) - now);
            }

            var perIp = await db.OtpChallenges.AsNoTracking()
                .CountAsync(c => c.IpHash == ipHash && c.CreatedAt > windowStart, token);
            if (perIp >= Options.MaxPerIp)
            {
                return AuthErrors.RateLimited(TimeSpan.FromMinutes(Options.WindowMinutes));
            }

            db.OtpChallenges.Add(OtpChallenge.Create(
                challengeId,
                purpose,
                identifierHash,
                phone,
                userId,
                hasher.Hash(HashPurposes.OtpCode, CodeMaterial(challengeId, code)),
                ipHash,
                now,
                TimeSpan.FromSeconds(Options.TtlSeconds),
                Options.MaxAttempts));
            await db.SaveChangesAsync(token);
            return new OtpRequestedDto(challengeId, Options.TtlSeconds, Options.ResendCooldownSeconds);
        }, ct);

        if (issued.IsSuccess && phone is not null)
        {
            try
            {
                await sms.SendAsync(phone, Options.MessageTemplate.Replace("{code}", code, StringComparison.Ordinal), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never log the phone or the code; the challenge id is enough to correlate.
                LogSmsFailed(logger, challengeId, purpose, ex.GetType().Name);
            }
        }

        return issued;
    }

    /// <summary>
    /// Counts one attempt and checks the code. Does not consume the challenge: call <see cref="ConsumeAsync"/>
    /// inside the transaction that acts on it. <paramref name="expectedUserId"/> binds LinkPhone challenges to
    /// the signed-in account (ignored for other purposes).
    /// </summary>
    public async Task<Result<OtpChallenge>> VerifyAsync(Guid challengeId, string? code, OtpPurpose purpose, Guid? expectedUserId, CancellationToken ct)
    {
        if (!IdentifierRules.IsCodeFormat(code, Options.CodeLength))
        {
            return Error.InvalidField("code", $"Enter the {Options.CodeLength}-digit code.");
        }

        // Atomic: only a live, unconsumed challenge with attempts left is counted (DB time for the deadline).
        var counted = await db.OtpChallenges
            .Where(c => c.Id == challengeId && c.Purpose == purpose && c.ConsumedAt == null
                && c.ExpiresAt > DateTimeOffset.UtcNow && c.Attempts < c.MaxAttempts)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Attempts, c => c.Attempts + 1), ct);

        var challenge = await db.OtpChallenges.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == challengeId && c.Purpose == purpose, ct);
        if (challenge is null || challenge.ConsumedAt is not null)
        {
            return AuthErrors.OtpInvalid;
        }

        if (counted == 0)
        {
            return challenge.ExpiresAt <= clock.GetUtcNow() ? AuthErrors.OtpExpired
                : challenge.Attempts >= challenge.MaxAttempts ? AuthErrors.OtpAttemptsExceeded
                : AuthErrors.OtpInvalid;
        }

        if (!hasher.Verify(HashPurposes.OtpCode, CodeMaterial(challenge.Id, code!), challenge.CodeHash))
        {
            return challenge.Attempts >= challenge.MaxAttempts ? AuthErrors.OtpAttemptsExceeded : AuthErrors.OtpInvalid;
        }

        // Ownership is checked only after the code matched, so decoy and real challenges behave identically.
        var ownerMatches = purpose switch
        {
            OtpPurpose.LinkPhone => expectedUserId is not null && challenge.UserId == expectedUserId,
            OtpPurpose.PasswordReset => challenge.UserId is not null && challenge.Phone is not null,
            _ => challenge.UserId is null,
        };
        return ownerMatches ? challenge : AuthErrors.OtpInvalid;
    }

    /// <summary>Marks the challenge consumed (once). Returns false if another request consumed it or it expired.</summary>
    public async Task<bool> ConsumeAsync(Guid challengeId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return await db.OtpChallenges
            .Where(c => c.Id == challengeId && c.ConsumedAt == null && c.ExpiresAt > DateTimeOffset.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), ct) == 1;
    }

    private static string CodeMaterial(Guid challengeId, string code) => challengeId.ToString("N") + ":" + code;

    [LoggerMessage(Level = LogLevel.Warning, Message = "SMS delivery failed for OTP challenge {ChallengeId} ({Purpose}): {ExceptionType}")]
    private static partial void LogSmsFailed(ILogger logger, Guid challengeId, OtpPurpose purpose, string exceptionType);
}
