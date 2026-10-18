using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Application.Common.Security;
using OqTish.Application.Identity.Telegram;
using OqTish.Domain.Identity;
using OqTish.Domain.Notifications;

namespace OqTish.Application.Identity.Handlers;

/// <summary>Outcome of a consumed deep-link token (for the bot's reply).</summary>
public sealed record TelegramLinkResult(Guid UserId);

/// <summary>
/// Telegram account linking.
/// <list type="bullet">
/// <item><see cref="CreateLinkAsync"/> — POST /api/me/telegram/link (auth: user): one-time token (256-bit,
/// stored as SHA-256, 10 min) → <c>https://t.me/&lt;bot&gt;?start=&lt;token&gt;</c>. Earlier unused tokens of the
/// user are deleted.</item>
/// <item><see cref="ConsumeTelegramLinkTokenAsync"/> — called by the bot (BE4) on <c>/start &lt;token&gt;</c>:
/// links the Telegram id to the token's user and stores the chat id. The token works once. A Telegram id
/// already linked to another account → <c>auth.identity_in_use</c> (no merge, audit
/// <c>identity.link_conflict</c> committed). Invalid/expired/used token → 422 <c>telegram.link_invalid</c>.</item>
/// <item><see cref="UnlinkAsync"/> — DELETE /api/me/telegram (auth: user): removes the Telegram identity and
/// chat link; refused (422 <c>auth.last_identity</c>) when Telegram is the account's only sign-in method.</item>
/// </list>
/// </summary>
public sealed class TelegramLinkService(
    IOqTishDb db,
    ITransactionRunner transactions,
    IRowLocks locks,
    ICurrentUser currentUser,
    IAuditLog audit,
    TimeProvider clock,
    IOptions<TelegramOptions> telegramOptions,
    IOptions<AuthOptions> authOptions)
{
    public static readonly Error LinkInvalid = Error.Rule("telegram.link_invalid", "The Telegram link is invalid, used or expired.");

    public static readonly Error LastIdentity = Error.Rule("auth.last_identity", "Telegram is the only way to sign in to this account; link a phone first.");

    public async Task<Result<TelegramLinkDto>> CreateLinkAsync(CancellationToken ct)
    {
        var options = telegramOptions.Value;
        if (!TelegramConfigQuery.IsEnabled(options))
        {
            return AuthErrors.TelegramDisabled;
        }

        var userId = currentUser.RequiredUserId;
        var token = SecureTokens.Create(32);
        var link = TelegramLinkToken.Create(userId, SecureTokens.Sha256(token), clock.GetUtcNow(), TimeSpan.FromMinutes(authOptions.Value.TelegramLinkTokenMinutes));
        return await transactions.RunAsync<TelegramLinkDto>(async t =>
        {
            await db.TelegramLinkTokens.Where(x => x.UserId == userId && x.UsedAt == null).ExecuteDeleteAsync(t);
            db.TelegramLinkTokens.Add(link);
            await db.SaveChangesAsync(t);
            return new TelegramLinkDto($"https://t.me/{options.BotUsername.TrimStart('@')}?start={token}", link.ExpiresAt);
        }, ct);
    }

    public async Task<Result<TelegramLinkResult>> ConsumeTelegramLinkTokenAsync(string token, long telegramUserId, long chatId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 64)
        {
            return LinkInvalid;
        }

        var hash = SecureTokens.Sha256(token);
        var telegramId = telegramUserId.ToString(CultureInfo.InvariantCulture);
        var outcome = await transactions.RunAsync<(Guid UserId, bool Linked)>(async t =>
        {
            var now = clock.GetUtcNow();
            var link = await db.TelegramLinkTokens.AsNoTracking().FirstOrDefaultAsync(x => x.TokenHash == hash, t);
            if (link is null)
            {
                return LinkInvalid;
            }

            var consumed = await db.TelegramLinkTokens
                .Where(x => x.Id == link.Id && x.UsedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAt, now), t);
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == link.UserId, t);
            if (consumed != 1 || user is not { Status: UserStatus.Active })
            {
                return LinkInvalid;
            }

            await locks.AdvisoryLockAsync("identity:telegram:" + telegramId, t);
            var owner = await db.AuthIdentities
                .FirstOrDefaultAsync(i => i.Kind == AuthIdentityKind.Telegram && i.NormalizedIdentifier == telegramId, t);
            if (owner is not null && owner.UserId != link.UserId)
            {
                audit.Record(new AuditEvent("identity.link_conflict", TargetType: "user", TargetId: owner.UserId.ToString(),
                    Details: new { kind = "Telegram" }, ActorUserIdOverride: link.UserId));
                await db.SaveChangesAsync(t);
                return (link.UserId, false);
            }

            if (owner is null)
            {
                var mine = await db.AuthIdentities.FirstOrDefaultAsync(i => i.UserId == link.UserId && i.Kind == AuthIdentityKind.Telegram, t);
                if (mine is null)
                {
                    db.AuthIdentities.Add(AuthIdentity.Create(link.UserId, AuthIdentityKind.Telegram, telegramId, verified: true, now));
                }
                else
                {
                    mine.ChangeIdentifier(telegramId, verified: true, now);
                }
            }

            var chat = await db.TelegramChatLinks.FirstOrDefaultAsync(c => c.UserId == link.UserId, t);
            if (chat is null)
            {
                db.TelegramChatLinks.Add(TelegramChatLink.Create(link.UserId, telegramUserId, chatId, now));
            }
            else
            {
                chat.Relink(telegramUserId, chatId, now);
            }

            audit.Record(new AuditEvent("identity.telegram_linked", TargetType: "user", TargetId: link.UserId.ToString(), ActorUserIdOverride: link.UserId));
            await db.SaveChangesAsync(t);
            return (link.UserId, true);
        }, ct);

        if (!outcome.IsSuccess)
        {
            return outcome.Error;
        }

        return outcome.Value.Linked ? new TelegramLinkResult(outcome.Value.UserId) : AuthErrors.IdentityInUse;
    }

    public Task<Result> UnlinkAsync(CancellationToken ct)
    {
        var userId = currentUser.RequiredUserId;
        return transactions.RunAsync(async t =>
        {
            var identities = await db.AuthIdentities.Where(i => i.UserId == userId).ToListAsync(t);
            var telegram = identities.FirstOrDefault(i => i.Kind == AuthIdentityKind.Telegram);
            var hasPassword = await db.PasswordCredentials.AnyAsync(c => c.UserId == userId, t);
            if (telegram is not null)
            {
                var otherSignIn = identities.Any(i => i.Kind == AuthIdentityKind.Phone && i.VerifiedAt != null)
                    || (hasPassword && identities.Any(i => i.Kind == AuthIdentityKind.EmailPassword));
                if (!otherSignIn)
                {
                    return LastIdentity;
                }

                db.AuthIdentities.Remove(telegram);
            }

            await db.TelegramChatLinks.Where(c => c.UserId == userId).ExecuteDeleteAsync(t);
            audit.Record(new AuditEvent("identity.telegram_unlinked", TargetType: "user", TargetId: userId.ToString()));
            await db.SaveChangesAsync(t);
            return Result.Success;
        }, ct);
    }
}
