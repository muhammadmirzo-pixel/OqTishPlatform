using System.Net;
using System.Net.Http.Json;
using OqTish.Application.Identity;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Infrastructure.TestAuth;

namespace OqTish.IntegrationTests.Identity;

/// <summary>S16: logs of full sign-in flows never contain phones, OTP codes, cookies, initData, tokens or passwords.</summary>
public class LogRedactionTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Captured_logs_contain_no_secrets_or_personal_data()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateApiClient();
        var phone = NewPhone();

        var challenge = await RequestOtpAsync(client, phone);
        var code = LatestCode(factory, phone);
        await client.PostAsJsonAsync("/api/auth/otp/verify", new { challengeId = challenge.ChallengeId, code = "000000" == code ? "111111" : "000000" }, ct);
        var verify = await client.PostAsJsonAsync("/api/auth/otp/verify", new { challengeId = challenge.ChallengeId, code }, ct);
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var cookie = verify.Headers.GetValues("Set-Cookie").Single().Split(';')[0].Split('=', 2)[1];

        const string password = "Very-secret-pass-9";
        await client.PostAsJsonAsync("/api/me/password", new { email = "logcheck@example.test", newPassword = password }, ct);
        await factory.CreateApiClient().PostAsJsonAsync("/api/auth/password/login", new { email = "logcheck@example.test", password }, ct);
        await factory.CreateApiClient().PostAsJsonAsync("/api/auth/password/login", new { email = "logcheck@example.test", password = "Wrong-pass-123" }, ct);

        var initData = TelegramSigning.InitData(424242, DateTimeOffset.UtcNow);
        await factory.CreateApiClient().PostAsJsonAsync("/api/auth/telegram/webapp", new { initData }, ct);
        await factory.CreateApiClient().PostAsJsonAsync("/api/auth/telegram/webapp", new { initData = initData + "x" }, ct);

        var link = await (await client.PostAsync("/api/me/telegram/link", null, ct)).Content.ReadFromJsonAsync<TelegramLinkDto>(Json, ct);
        factory.Sms.FailSends = true;
        await client.PostAsJsonAsync("/api/me/phone/request", new { phone = NewPhone() }, ct);
        await client.PostAsync("/api/auth/logout", null, ct);

        var logs = factory.Logs.AllText;
        Assert.NotEmpty(factory.Logs.Entries);
        var forbidden = new[]
        {
            phone, phone[4..], PhoneNumber.DigitsOnly(phone), code, cookie, password, initData,
            initData.Split("hash=")[1], ApiFactory.TelegramBotToken, link!.DeepLink.Split("start=")[1],
        };
        foreach (var secret in forbidden)
        {
            Assert.False(logs.Contains(secret, StringComparison.Ordinal), $"Logs contain a forbidden value ({secret.Length} chars)");
        }
    }
}
