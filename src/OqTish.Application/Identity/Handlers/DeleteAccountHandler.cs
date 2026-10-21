using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Common.Images;
using OqTish.Domain.Identity;
using OqTish.Domain.Staff;

namespace OqTish.Application.Identity.Handlers;

/// <summary>
/// Extension point for self-deletion: modules that hold personal data of a user (appointments' contact
/// fields, reviews …) de-identify it here, inside the deletion transaction. Register as scoped.
/// </summary>
public interface IAccountDeletionContributor
{
    Task AnonymiseAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// POST /api/me/delete (OD-18). Auth: user. Refused (422 <c>account.delete_blocked</c>) while the user has a
/// non-removed clinic membership or an active platform grant. Otherwise anonymises in one transaction: user
/// status Deleted (display name cleared), patient profile anonymised, every identity, password, TOTP secret,
/// Telegram token/chat link and the avatar image removed, all sessions revoked, contributors run. The user row stays for
/// referential integrity. Side effects: audit <c>account.deleted</c>.
/// </summary>
public sealed class DeleteAccountHandler(
    IOqTishDb db,
    ITransactionRunner transactions,
    SessionService sessions,
    IEnumerable<IAccountDeletionContributor> contributors,
    ICurrentUser currentUser,
    IAuditLog audit,
    TimeProvider clock)
{
    public Task<Result> HandleAsync(CancellationToken ct)
    {
        var userId = currentUser.RequiredUserId;
        return transactions.RunAsync(async token =>
        {
            var blocked = await db.ClinicMemberships.AnyAsync(m => m.UserId == userId && m.Status != MembershipStatus.Removed, token)
                || await db.PlatformAdminGrants.AnyAsync(g => g.UserId == userId && g.RevokedAt == null, token);
            if (blocked)
            {
                return AuthErrors.DeleteBlocked;
            }

            var now = clock.GetUtcNow();
            var user = await db.Users.FirstAsync(u => u.Id == userId, token);
            var avatar = user.AvatarImageId;
            user.MarkDeleted(now);
            var profile = await db.PatientProfiles.FirstOrDefaultAsync(p => p.UserId == userId, token);
            profile?.Anonymise(now);

            await db.AuthIdentities.Where(i => i.UserId == userId).ExecuteDeleteAsync(token);
            await db.PasswordCredentials.Where(c => c.UserId == userId).ExecuteDeleteAsync(token);
            await db.TotpCredentials.Where(c => c.UserId == userId).ExecuteDeleteAsync(token);
            await db.TelegramLinkTokens.Where(c => c.UserId == userId).ExecuteDeleteAsync(token);
            await db.TelegramChatLinks.Where(c => c.UserId == userId).ExecuteDeleteAsync(token);
            await db.OtpChallenges.Where(c => c.UserId == userId).ExecuteDeleteAsync(token);
            await sessions.RevokeAllAsync(userId, SessionRevocationReasons.AccountDeleted, null, token);
            foreach (var contributor in contributors)
            {
                await contributor.AnonymiseAsync(userId, token);
            }

            audit.Record(new AuditEvent("account.deleted", TargetType: "user", TargetId: userId.ToString()));
            await db.SaveChangesAsync(token);
            await StoredImageStore.DeleteAsync(db, avatar, token);
            return Result.Success;
        }, ct);
    }
}
