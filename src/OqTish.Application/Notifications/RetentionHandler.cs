using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Application.Identity;
using OqTish.Domain.Notifications;

namespace OqTish.Application.Notifications;

/// <summary>Rows deleted by one retention run (counts only).</summary>
public sealed record RetentionResult(int OtpChallenges, int Sessions, int OutboxMessages, int LinkTokens, int DevMessages);

/// <summary>
/// Nightly retention (OD-18; Worker <c>RetentionService</c>; docs/SERVICES.md §5.7). Deletes, with database time:
/// OTP challenges created more than <c>Retention:OtpChallengeHours</c> (24) ago; sessions that ended (revoked, expired
/// or idle-dead) more than <c>SessionDays</c> (30) ago; Sent/Skipped/Cancelled outbox rows older than <c>OutboxDays</c>
/// (30) and Failed ones older than <c>FailedOutboxDays</c> (90) — their delivery attempts go with them (FK cascade);
/// Telegram link tokens expired more than <c>LinkTokenHours</c> (24) ago; Development capture rows. Pending and
/// Processing outbox rows, the audit log and the ledger are never touched.
/// </summary>
public sealed class RetentionHandler(IOqTishDb db, IDbClock clock, IOptions<RetentionOptions> options, IOptions<AuthOptions> authOptions)
{
    public async Task<RetentionResult> RunAsync(CancellationToken ct)
    {
        var o = options.Value;
        var now = await clock.NowAsync(ct);

        var otpCutoff = now.AddHours(-Math.Max(1, o.OtpChallengeHours));
        var otp = await db.OtpChallenges.Where(c => c.CreatedAt < otpCutoff).ExecuteDeleteAsync(ct);

        var sessionCutoff = now.AddDays(-Math.Max(1, o.SessionDays));
        var idleCutoff = sessionCutoff.AddDays(-Math.Max(1, authOptions.Value.Session.IdleTimeoutDays));
        var sessions = await db.Sessions
            .Where(s => (s.RevokedAt != null && s.RevokedAt < sessionCutoff) || s.ExpiresAt < sessionCutoff || s.LastSeenAt < idleCutoff)
            .ExecuteDeleteAsync(ct);

        var outboxCutoff = now.AddDays(-Math.Max(1, o.OutboxDays));
        var failedCutoff = now.AddDays(-Math.Max(1, o.FailedOutboxDays));
        var outbox = await db.OutboxMessages
            .Where(m => ((m.Status == OutboxStatus.Sent || m.Status == OutboxStatus.Skipped || m.Status == OutboxStatus.Cancelled) && m.CreatedAt < outboxCutoff)
                || (m.Status == OutboxStatus.Failed && m.CreatedAt < failedCutoff))
            .ExecuteDeleteAsync(ct);

        var tokenCutoff = now.AddHours(-Math.Max(1, o.LinkTokenHours));
        var tokens = await db.TelegramLinkTokens.Where(t => t.ExpiresAt < tokenCutoff).ExecuteDeleteAsync(ct);

        var devCutoff = now.AddDays(-Math.Max(1, o.DevMessageDays));
        var dev = await db.DevCapturedMessages.Where(m => m.CreatedAt < devCutoff).ExecuteDeleteAsync(ct);

        return new RetentionResult(otp, sessions, outbox, tokens, dev);
    }
}
