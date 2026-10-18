using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using OqTish.Application.Identity.Telegram;
using OqTish.Infrastructure.Telegram;

namespace OqTish.UnitTests.Identity;

public class TelegramLoginVerifierTests
{
    private const string BotToken = "123456:UNIT-test-token";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    private static TelegramLoginVerifier Verifier(DateTimeOffset now) =>
        new(Options.Create(new TelegramOptions { Enabled = true, BotToken = BotToken, BotUsername = "bot" }), new FixedTimeProvider(now));

    /// <summary>Builds initData exactly like Telegram: URL-encoded fields + hex HMAC hash.</summary>
    public static string SignInitData(string botToken, IDictionary<string, string> fields)
    {
        var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(botToken));
        var check = string.Join('\n', fields.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => $"{f.Key}={f.Value}"));
        var hash = Convert.ToHexStringLower(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(check)));
        return string.Join('&', fields.Select(f => $"{f.Key}={WebUtility.UrlEncode(f.Value)}")) + "&hash=" + hash;
    }

    public static Dictionary<string, string> SignWidget(string botToken, Dictionary<string, string> fields)
    {
        var secret = SHA256.HashData(Encoding.UTF8.GetBytes(botToken));
        var check = string.Join('\n', fields.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => $"{f.Key}={f.Value}"));
        return new Dictionary<string, string>(fields) { ["hash"] = Convert.ToHexStringLower(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(check))) };
    }

    private static Dictionary<string, string> InitFields(DateTimeOffset authDate) => new()
    {
        ["query_id"] = "AAHdF6IQAAAAAN0XohDhrOrc",
        ["user"] = "{\"id\":279058397,\"first_name\":\"Ali\",\"last_name\":\"Valiyev\",\"username\":\"ali\",\"language_code\":\"uz\"}",
        ["auth_date"] = authDate.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    [Fact]
    public void Valid_init_data_yields_the_user()
    {
        var result = Verifier(Now).VerifyWebAppInitData(SignInitData(BotToken, InitFields(Now.AddMinutes(-5))));
        Assert.True(result.IsSuccess);
        Assert.Equal(279058397, result.Value.Id);
        Assert.Equal("Ali Valiyev", result.Value.DisplayName);
    }

    [Fact]
    public void Tampered_init_data_is_rejected()
    {
        var data = SignInitData(BotToken, InitFields(Now)).Replace("279058397", "279058398", StringComparison.Ordinal);
        Assert.Equal("auth.telegram_invalid", Verifier(Now).VerifyWebAppInitData(data).Error!.Code);
    }

    [Fact]
    public void Init_data_signed_with_another_token_is_rejected() =>
        Assert.False(Verifier(Now).VerifyWebAppInitData(SignInitData("999:other", InitFields(Now))).IsSuccess);

    [Fact]
    public void Stale_init_data_is_rejected()
    {
        var data = SignInitData(BotToken, InitFields(Now.AddHours(-24).AddSeconds(-1)));
        Assert.False(Verifier(Now).VerifyWebAppInitData(data).IsSuccess);
        Assert.True(Verifier(Now).VerifyWebAppInitData(SignInitData(BotToken, InitFields(Now.AddHours(-23)))).IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("user=x")]
    [InlineData("hash=zz&auth_date=1")]
    public void Garbage_is_rejected(string data) => Assert.False(Verifier(Now).VerifyWebAppInitData(data).IsSuccess);

    [Fact]
    public void Widget_hash_valid_tampered_and_stale()
    {
        var fields = new Dictionary<string, string>
        {
            ["id"] = "42", ["first_name"] = "Vali", ["username"] = "vali",
            ["auth_date"] = Now.AddMinutes(-1).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        var signed = SignWidget(BotToken, fields);
        Assert.Equal(42, Verifier(Now).VerifyLoginWidget(signed).Value.Id);

        var tampered = new Dictionary<string, string>(signed) { ["first_name"] = "Eve" };
        Assert.False(Verifier(Now).VerifyLoginWidget(tampered).IsSuccess);

        Assert.False(Verifier(Now.AddDays(2)).VerifyLoginWidget(signed).IsSuccess);

        // The Mini App key derivation must not validate widget data (different secret).
        var webAppSecretSigned = new Dictionary<string, string>(fields)
        {
            ["hash"] = SignInitData(BotToken, fields).Split("hash=")[1],
        };
        Assert.False(Verifier(Now).VerifyLoginWidget(webAppSecretSigned).IsSuccess);
    }
}
