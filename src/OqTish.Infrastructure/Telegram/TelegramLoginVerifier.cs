using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Application.Identity;
using OqTish.Application.Identity.Telegram;

namespace OqTish.Infrastructure.Telegram;

/// <summary>
/// Telegram signature checks (https://core.telegram.org/bots/webapps#validating-data-received-via-the-mini-app,
/// https://core.telegram.org/widgets/login#checking-authorization). Hashes are compared in constant time;
/// <c>auth_date</c> must be at most <c>Telegram:AuthMaxAgeSeconds</c> old (and not more than 5 min in the future).
/// </summary>
internal sealed class TelegramLoginVerifier(IOptions<TelegramOptions> options, TimeProvider clock) : ITelegramLoginVerifier
{
    private const int MaxPayloadLength = 8192;
    private static readonly TimeSpan FutureSkew = TimeSpan.FromMinutes(5);

    public Result<TelegramUser> VerifyWebAppInitData(string initData)
    {
        var botToken = options.Value.BotToken;
        if (string.IsNullOrEmpty(botToken) || string.IsNullOrEmpty(initData) || initData.Length > MaxPayloadLength)
        {
            return AuthErrors.TelegramInvalid;
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in initData.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                return AuthErrors.TelegramInvalid;
            }

            var key = WebUtility.UrlDecode(pair[..eq]);
            if (!fields.TryAdd(key, WebUtility.UrlDecode(pair[(eq + 1)..])))
            {
                return AuthErrors.TelegramInvalid;
            }
        }

        var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(botToken));
        if (!SignatureAndAgeValid(fields, secret) || !fields.TryGetValue("user", out var userJson))
        {
            return AuthErrors.TelegramInvalid;
        }

        try
        {
            using var doc = JsonDocument.Parse(userJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var id) || !id.TryGetInt64(out var telegramId) || telegramId <= 0)
            {
                return AuthErrors.TelegramInvalid;
            }

            return new TelegramUser(telegramId, GetString(root, "first_name"), GetString(root, "last_name"), GetString(root, "username"));
        }
        catch (JsonException)
        {
            return AuthErrors.TelegramInvalid;
        }
    }

    public Result<TelegramUser> VerifyLoginWidget(IReadOnlyDictionary<string, string> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var botToken = options.Value.BotToken;
        if (string.IsNullOrEmpty(botToken) || fields.Count > 20 || fields.Sum(f => f.Key.Length + f.Value.Length) > MaxPayloadLength)
        {
            return AuthErrors.TelegramInvalid;
        }

        var secret = SHA256.HashData(Encoding.UTF8.GetBytes(botToken));
        if (!SignatureAndAgeValid(fields, secret)
            || !fields.TryGetValue("id", out var idText)
            || !long.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out var telegramId)
            || telegramId <= 0)
        {
            return AuthErrors.TelegramInvalid;
        }

        return new TelegramUser(
            telegramId,
            fields.GetValueOrDefault("first_name"),
            fields.GetValueOrDefault("last_name"),
            fields.GetValueOrDefault("username"));
    }

    /// <summary>data_check_string = sorted "key=value" lines of every field except <c>hash</c>.</summary>
    internal static string DataCheckString(IReadOnlyDictionary<string, string> fields) =>
        string.Join('\n', fields
            .Where(f => f.Key != "hash")
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => f.Key + "=" + f.Value));

    private bool SignatureAndAgeValid(IReadOnlyDictionary<string, string> fields, byte[] secret)
    {
        if (!fields.TryGetValue("hash", out var hashHex) || hashHex.Length != 64)
        {
            return false;
        }

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(hashHex);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(DataCheckString(fields)));
        if (!CryptographicOperations.FixedTimeEquals(expected, provided))
        {
            return false;
        }

        if (!fields.TryGetValue("auth_date", out var authDateText)
            || !long.TryParse(authDateText, NumberStyles.None, CultureInfo.InvariantCulture, out var authDate)
            || authDate > 253_402_300_799)
        {
            return false;
        }

        var issued = DateTimeOffset.FromUnixTimeSeconds(authDate);
        var now = clock.GetUtcNow();
        return issued <= now + FutureSkew && now - issued <= TimeSpan.FromSeconds(options.Value.AuthMaxAgeSeconds);
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
