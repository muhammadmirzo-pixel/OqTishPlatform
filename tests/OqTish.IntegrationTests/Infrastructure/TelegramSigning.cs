using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace OqTish.IntegrationTests.Infrastructure;

/// <summary>Produces Telegram-signed payloads exactly as Telegram does (test bot token).</summary>
public static class TelegramSigning
{
    public static string InitData(long telegramId, DateTimeOffset authDate, string botToken = ApiFactory.TelegramBotToken, string firstName = "Ali")
    {
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_date"] = authDate.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            ["query_id"] = "AAH-test",
            ["user"] = $"{{\"id\":{telegramId},\"first_name\":\"{firstName}\",\"username\":\"u{telegramId}\"}}",
        };
        var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(botToken));
        var hash = Convert.ToHexStringLower(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(string.Join('\n', fields.Select(f => $"{f.Key}={f.Value}")))));
        return string.Join('&', fields.Select(f => $"{f.Key}={WebUtility.UrlEncode(f.Value)}")) + "&hash=" + hash;
    }

    public static Dictionary<string, object> Widget(long telegramId, DateTimeOffset authDate, string botToken = ApiFactory.TelegramBotToken)
    {
        var auth = authDate.ToUnixTimeSeconds();
        var check = $"auth_date={auth}\nfirst_name=Vali\nid={telegramId}\nusername=w{telegramId}";
        var secret = SHA256.HashData(Encoding.UTF8.GetBytes(botToken));
        var hash = Convert.ToHexStringLower(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(check)));
        return new Dictionary<string, object>
        {
            ["id"] = telegramId, ["first_name"] = "Vali", ["username"] = $"w{telegramId}", ["auth_date"] = auth, ["hash"] = hash,
        };
    }
}
