using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Application.Common.Security;
using OqTish.Domain.Identity;

namespace OqTish.Application.Identity;

/// <summary>A validated session for the current request.</summary>
public sealed record ValidatedSession(Guid SessionId, Guid UserId, DateTimeOffset ExpiresAt, bool Renewed);

/// <summary>
/// Server-side sessions (ADR-016). The cookie value is a 256-bit random token; the database stores only
/// its SHA-256. Validation runs on every request and rejects revoked, expired, idle (14 d), stamp-mismatched
/// sessions and non-Active users, so revocation and suspension take effect on the next request.
/// <c>last_seen_at</c> and the sliding expiry are written at most every <c>Auth:Session:TouchIntervalSeconds</c>.
/// </summary>
public sealed class SessionService(
    IOqTishDb db,
    IRequestContext request,
    IKeyedHasher hasher,
    TimeProvider clock,
    IOptions<AuthOptions> options)
{
    private SessionOptions Options => options.Value.Session;

    /// <summary>Adds a new session to the unit of work (saved by the caller's SaveChanges) and returns the cookie token.</summary>
    public (Session Session, string Token) Create(User user, string authMethod)
    {
        ArgumentNullException.ThrowIfNull(user);
        var token = SecureTokens.Create(32);
        var ipHash = request.RemoteIp is { } ip ? hasher.Hash(HashPurposes.Ip, ip) : null;
        var session = Session.Create(
            user.Id,
            SecureTokens.Sha256(token),
            user.SecurityStamp,
            authMethod,
            clock.GetUtcNow(),
            TimeSpan.FromDays(Options.SlidingLifetimeDays),
            ipHash,
            request.UserAgent);
        db.Sessions.Add(session);
        return (session, token);
    }

    public async Task<ValidatedSession?> ValidateAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 128)
        {
            return null;
        }

        var hash = SecureTokens.Sha256(token);
        var now = clock.GetUtcNow();
        var row = await (
                from s in db.Sessions.AsNoTracking()
                join u in db.Users.AsNoTracking() on s.UserId equals u.Id
                where s.TokenHash == hash && s.RevokedAt == null
                select new { s.Id, s.UserId, s.CreatedAt, s.ExpiresAt, s.LastSeenAt, s.SecurityStamp, UserStamp = u.SecurityStamp, u.Status })
            .FirstOrDefaultAsync(ct);

        if (row is null
            || row.Status != UserStatus.Active
            || row.ExpiresAt <= now
            || row.CreatedAt + TimeSpan.FromDays(Options.AbsoluteLifetimeDays) <= now
            || row.LastSeenAt + TimeSpan.FromDays(Options.IdleTimeoutDays) <= now
            || !SecureTokens.FixedTimeEquals(row.SecurityStamp, row.UserStamp))
        {
            return null;
        }

        if (now - row.LastSeenAt < TimeSpan.FromSeconds(Options.TouchIntervalSeconds))
        {
            return new ValidatedSession(row.Id, row.UserId, row.ExpiresAt, Renewed: false);
        }

        // Sliding renewal never extends past the absolute cap (review R3): a stolen cookie that keeps
        // being used still dies AbsoluteLifetimeDays after sign-in.
        var sliding = now + TimeSpan.FromDays(Options.SlidingLifetimeDays);
        var absolute = row.CreatedAt + TimeSpan.FromDays(Options.AbsoluteLifetimeDays);
        var expires = sliding < absolute ? sliding : absolute;
        await db.Sessions
            .Where(s => s.Id == row.Id && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, now).SetProperty(x => x.ExpiresAt, expires), ct);
        return new ValidatedSession(row.Id, row.UserId, expires, Renewed: true);
    }

    public Task<int> RevokeAsync(Guid userId, Guid sessionId, string reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return db.Sessions
            .Where(s => s.Id == sessionId && s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokedReason, reason), ct);
    }

    /// <summary>Revokes every active session of the user, optionally keeping one (the caller's).</summary>
    public Task<int> RevokeAllAsync(Guid userId, string reason, Guid? exceptSessionId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return db.Sessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && (exceptSessionId == null || s.Id != exceptSessionId))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokedReason, reason), ct);
    }

    /// <summary>Copies the user's (rotated) security stamp into one session so it survives the rotation.</summary>
    public Task<int> RefreshStampAsync(Guid sessionId, string securityStamp, CancellationToken ct) =>
        db.Sessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.SecurityStamp, securityStamp), ct);
}

public static class SessionRevocationReasons
{
    public const string Logout = "logout";
    public const string LogoutAll = "logout_all";
    public const string UserRevoked = "user_revoked";
    public const string PasswordChanged = "password_changed";
    public const string PasswordReset = "password_reset";
    public const string TotpEnabled = "totp_enabled";
    public const string AccountDeleted = "account_deleted";
    public const string Suspended = "suspended";
}
