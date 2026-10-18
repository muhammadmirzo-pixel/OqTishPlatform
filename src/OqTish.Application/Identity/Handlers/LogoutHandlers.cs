using OqTish.Application.Common;

namespace OqTish.Application.Identity.Handlers;

/// <summary>
/// POST /api/auth/logout (current session) and POST /api/auth/logout-all (every session of the user).
/// Auth: user. Side effects: sessions revoked, audit <c>auth.logout</c> / <c>auth.logout_all</c> (same transaction).
/// </summary>
public sealed class LogoutHandler(IOqTishDb db, ITransactionRunner transactions, SessionService sessions, ICurrentUser currentUser, IAuditLog audit)
{
    public Task<Result> LogoutAsync(CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId || currentUser.SessionId is not { } sessionId)
        {
            return Task.FromResult<Result>(AuthErrors.Unauthenticated);
        }

        return transactions.RunAsync(async token =>
        {
            await sessions.RevokeAsync(userId, sessionId, SessionRevocationReasons.Logout, token);
            audit.Record(new AuditEvent("auth.logout", TargetType: "session", TargetId: sessionId.ToString()));
            await db.SaveChangesAsync(token);
            return Result.Success;
        }, ct);
    }

    public Task<Result> LogoutAllAsync(CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Task.FromResult<Result>(AuthErrors.Unauthenticated);
        }

        return transactions.RunAsync(async token =>
        {
            var revoked = await sessions.RevokeAllAsync(userId, SessionRevocationReasons.LogoutAll, null, token);
            audit.Record(new AuditEvent("auth.logout_all", TargetType: "user", TargetId: userId.ToString(), Details: new { revoked }));
            await db.SaveChangesAsync(token);
            return Result.Success;
        }, ct);
    }
}
