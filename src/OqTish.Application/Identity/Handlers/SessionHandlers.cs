using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;

namespace OqTish.Application.Identity.Handlers;

/// <summary>
/// GET /api/me/sessions (active sessions, most recently used first) and DELETE /api/me/sessions/{id}
/// (revoke one; another user's id → 404). Auth: user. Side effects: audit <c>auth.session.revoked</c>.
/// </summary>
public sealed class SessionsHandler(
    IOqTishDb db,
    ITransactionRunner transactions,
    SessionService sessions,
    ICurrentUser currentUser,
    IAuditLog audit,
    TimeProvider clock)
{
    public async Task<Result<IReadOnlyList<SessionDto>>> ListAsync(CancellationToken ct)
    {
        var userId = currentUser.RequiredUserId;
        var current = currentUser.SessionId;
        var now = clock.GetUtcNow();
        var rows = await db.Sessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastSeenAt)
            .ThenBy(s => s.Id)
            .Take(100)
            .Select(s => new SessionDto(s.Id, s.CreatedAt, s.LastSeenAt, s.ExpiresAt, s.AuthMethod, s.UserAgent, s.Id == current))
            .ToListAsync(ct);
        return rows;
    }

    public Task<Result> RevokeAsync(Guid sessionId, CancellationToken ct)
    {
        var userId = currentUser.RequiredUserId;
        return transactions.RunAsync(async token =>
        {
            if (await sessions.RevokeAsync(userId, sessionId, SessionRevocationReasons.UserRevoked, token) == 0)
            {
                return CommonErrors.NotFound;
            }

            audit.Record(new AuditEvent("auth.session.revoked", TargetType: "session", TargetId: sessionId.ToString()));
            await db.SaveChangesAsync(token);
            return Result.Success;
        }, ct);
    }
}
