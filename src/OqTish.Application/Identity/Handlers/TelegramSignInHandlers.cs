using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Application.Identity.Telegram;
using OqTish.Domain.Identity;

namespace OqTish.Application.Identity.Handlers;

public sealed record TelegramWebAppSignInCommand(string? InitData, string? TotpCode);

/// <param name="Fields">Every Login Widget field as received (string form), excluding our own <c>totpCode</c>.</param>
public sealed record TelegramWidgetSignInCommand(IReadOnlyDictionary<string, string> Fields, string? TotpCode);

/// <summary>
/// POST /api/auth/telegram/webapp and /api/auth/telegram/widget. Auth: anon. Verifies the Telegram
/// signature (<see cref="ITelegramLoginVerifier"/>), then signs in the account linked to that Telegram id,
/// or creates a new patient user for an unknown id (never matched to other accounts by name or phone,
/// OD-21). Accounts with TOTP enabled must send <c>totpCode</c>. Disabled integration → 422
/// <c>telegram.disabled</c>. Side effects: user/identity/profile (new users), session, audit.
/// </summary>
public sealed class TelegramSignInHandler(
    IOqTishDb db,
    ITransactionRunner transactions,
    IRowLocks locks,
    ITelegramLoginVerifier verifier,
    TotpService totp,
    SignInService signIn,
    TimeProvider clock,
    IOptions<TelegramOptions> options)
{
    public Task<Result<SignInOutcome>> HandleAsync(TelegramWebAppSignInCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!TelegramConfigQuery.IsEnabled(options.Value))
        {
            return Task.FromResult<Result<SignInOutcome>>(AuthErrors.TelegramDisabled);
        }

        var user = verifier.VerifyWebAppInitData(command.InitData ?? string.Empty);
        return user.IsSuccess
            ? SignInAsync(user.Value, command.TotpCode, AuthMethods.TelegramWebApp, ct)
            : Task.FromResult<Result<SignInOutcome>>(user.Error);
    }

    public Task<Result<SignInOutcome>> HandleAsync(TelegramWidgetSignInCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!TelegramConfigQuery.IsEnabled(options.Value))
        {
            return Task.FromResult<Result<SignInOutcome>>(AuthErrors.TelegramDisabled);
        }

        var user = verifier.VerifyLoginWidget(command.Fields);
        return user.IsSuccess
            ? SignInAsync(user.Value, command.TotpCode, AuthMethods.TelegramWidget, ct)
            : Task.FromResult<Result<SignInOutcome>>(user.Error);
    }

    private Task<Result<SignInOutcome>> SignInAsync(TelegramUser telegramUser, string? totpCode, string method, CancellationToken ct)
    {
        var telegramId = telegramUser.Id.ToString(CultureInfo.InvariantCulture);
        return transactions.RunAsync<SignInOutcome>(async token =>
        {
            await locks.AdvisoryLockAsync("identity:telegram:" + telegramId, token);
            var identity = await db.AuthIdentities
                .FirstOrDefaultAsync(i => i.Kind == AuthIdentityKind.Telegram && i.NormalizedIdentifier == telegramId, token);
            User user;
            if (identity is null)
            {
                user = signIn.CreatePatientUser(Truncate(telegramUser.DisplayName), null);
                identity = AuthIdentity.Create(user.Id, AuthIdentityKind.Telegram, telegramId, verified: true, clock.GetUtcNow());
                db.AuthIdentities.Add(identity);
            }
            else
            {
                user = await db.Users.FirstAsync(u => u.Id == identity.UserId, token);
                if (user.Status == UserStatus.Active)
                {
                    var second = await totp.RequireSecondFactorAsync(user.Id, totpCode, token);
                    if (!second.IsSuccess)
                    {
                        return second.Error;
                    }
                }
            }

            return await signIn.CompleteAsync(user, identity, method, token);
        }, ct);
    }

    private static string Truncate(string name) =>
        name.Length > PatientProfile.MaxNameLength ? name[..PatientProfile.MaxNameLength] : name;
}

/// <summary>GET /api/telegram/config. Auth: anon. <c>{ enabled, botUsername, miniAppUrl }</c> for the UI.</summary>
public sealed class TelegramConfigQuery(IOptions<TelegramOptions> options)
{
    public TelegramConfigDto Handle()
    {
        var o = options.Value;
        var enabled = IsEnabled(o);
        return new TelegramConfigDto(
            enabled,
            enabled ? o.BotUsername : null,
            enabled && !string.IsNullOrWhiteSpace(o.MiniAppUrl) ? o.MiniAppUrl : null,
            enabled ? o.BotId : null);
    }

    /// <summary>Enabled flag plus a bot token and username: without them nothing Telegram-related can work.</summary>
    public static bool IsEnabled(TelegramOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return o.Enabled && !string.IsNullOrWhiteSpace(o.BotToken) && !string.IsNullOrWhiteSpace(o.BotUsername);
    }
}
