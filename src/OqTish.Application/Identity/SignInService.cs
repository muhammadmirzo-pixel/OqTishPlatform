using OqTish.Application.Common;
using OqTish.Domain.Identity;

namespace OqTish.Application.Identity;

/// <summary>
/// Final step shared by every sign-in method, run inside the caller's transaction: rejects non-Active users
/// (403 <c>auth.account_suspended</c>), creates the server-side session, audits <c>auth.signed_in</c>,
/// saves, and builds <see cref="MeDto"/>.
/// </summary>
public sealed class SignInService(IOqTishDb db, SessionService sessions, MeQuery me, IAuditLog audit, TimeProvider clock)
{
    public async Task<Result<SignInOutcome>> CompleteAsync(User user, AuthIdentity? identity, string method, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.Status != UserStatus.Active)
        {
            return AuthErrors.AccountSuspended;
        }

        identity?.MarkUsed(clock.GetUtcNow());
        var (session, token) = sessions.Create(user, method);
        audit.Record(new AuditEvent("auth.signed_in", TargetType: "session", TargetId: session.Id.ToString(), Details: new { method }, ActorUserIdOverride: user.Id));
        await db.SaveChangesAsync(ct);

        var dto = await me.BuildAsync(user.Id, ct) ?? throw new InvalidOperationException("Signed-in user vanished.");
        return new SignInOutcome(dto, token, session.ExpiresAt);
    }

    /// <summary>Creates an Active user with a patient profile (new phone / Telegram sign-in).</summary>
    public User CreatePatientUser(string? displayName, string? contactPhone)
    {
        var now = clock.GetUtcNow();
        var user = User.Create(displayName, now);
        db.Users.Add(user);
        db.PatientProfiles.Add(PatientProfile.Create(user.Id, displayName ?? string.Empty, contactPhone, now));
        return user;
    }
}
