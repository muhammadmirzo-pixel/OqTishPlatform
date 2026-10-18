using OqTish.Application.Common;

namespace OqTish.Application.Identity.Telegram;

/// <summary>A Telegram user proven by a valid signature.</summary>
public sealed record TelegramUser(long Id, string? FirstName, string? LastName, string? Username)
{
    public string DisplayName =>
        string.Join(' ', new[] { FirstName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } name
            ? name
            : Username ?? "Telegram";
}

/// <summary>
/// Verifies Telegram sign-in payloads with the bot token (constant-time HMAC comparison, max age).
/// Failure → 401 <c>auth.telegram_invalid</c>.
/// </summary>
public interface ITelegramLoginVerifier
{
    /// <summary>
    /// Mini App <c>initData</c> (URL-encoded query string). Key = HMAC_SHA256("WebAppData", botToken);
    /// hash = hex(HMAC_SHA256(key, data_check_string)).
    /// </summary>
    Result<TelegramUser> VerifyWebAppInitData(string initData);

    /// <summary>
    /// Login Widget fields (all received fields except <c>hash</c> form the data-check-string).
    /// Key = SHA256(botToken); hash = hex(HMAC_SHA256(key, data_check_string)).
    /// </summary>
    Result<TelegramUser> VerifyLoginWidget(IReadOnlyDictionary<string, string> fields);
}
