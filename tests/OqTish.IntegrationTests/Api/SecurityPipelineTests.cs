using System.Net;
using System.Net.Http.Json;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Infrastructure.TestAuth;

namespace OqTish.IntegrationTests.Api;

public class SecurityPipelineTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null)]
    [InlineData("evil")]
    public async Task Non_get_without_client_header_is_403(string? header)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateApiClient(withClientHeader: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/otp/request") { Content = JsonContent.Create(new { phone = NewPhone() }) };
        if (header is not null)
        {
            request.Headers.Add("X-OqTish-Client", header);
        }

        var response = await client.SendAsync(request, Ct);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "security.client_header_missing");
    }

    [Fact]
    public async Task Csrf_check_runs_before_authentication_and_get_is_exempt()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var signedIn = await SignInAsPatientAsync(factory);
        signedIn.Client.DefaultRequestHeaders.Remove("X-OqTish-Client");

        await AssertProblemAsync(await signedIn.Client.PostAsync("/api/auth/logout", null, Ct), HttpStatusCode.Forbidden, "security.client_header_missing");
        Assert.Equal(HttpStatusCode.OK, (await signedIn.Client.GetAsync("/api/me", Ct)).StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("X-OqTish-Client", "tg-miniapp");
        Assert.Equal(HttpStatusCode.NoContent, (await signedIn.Client.SendAsync(request, Ct)).StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/dev/sms?phone=%2B998901234567")]
    [InlineData("POST", "/api/dev/seed")]
    public async Task Dev_endpoints_are_404_in_production(string method, string path)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, "Production");
        var response = await factory.CreateApiClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Ct);
        await AssertProblemAsync(response, HttpStatusCode.NotFound, "common.not_found");
    }

    [Fact]
    public async Task Dev_sms_mailbox_is_readable_in_development()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateApiClient();
        var phone = NewPhone();
        await RequestOtpAsync(client, phone);
        var messages = await client.GetFromJsonAsync<List<System.Text.Json.JsonElement>>($"/api/dev/sms?phone={Uri.EscapeDataString(phone)}", Ct);
        Assert.Contains(LatestCode(factory, phone), Assert.Single(messages!).GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_refuses_to_start_with_fake_sms()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, "Production", new Dictionary<string, string?> { ["Sms:Provider"] = "Fake" });
        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateApiClient());
        Assert.Contains("Fake", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_refuses_to_start_without_otp_pepper()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, "Production", new Dictionary<string, string?> { ["Security:OtpPepper"] = "" });
        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateApiClient());
        Assert.Contains("OtpPepper", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_json_is_problem_with_validation_code()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        using var content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json");
        var response = await factory.CreateApiClient().PostAsync("/api/auth/otp/verify", content, Ct);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "common.validation");
    }

    [Fact]
    public async Task Telegram_config_is_public()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var config = await factory.CreateApiClient().GetFromJsonAsync<System.Text.Json.JsonElement>("/api/telegram/config", Ct);
        Assert.True(config.GetProperty("enabled").GetBoolean());
        Assert.Equal("oqtish_test_bot", config.GetProperty("botUsername").GetString());
        Assert.Equal("https://example.test/tg", config.GetProperty("miniAppUrl").GetString());
        Assert.DoesNotContain(ApiFactory.TelegramBotToken, config.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Telegram_disabled_reports_disabled()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, settings: new Dictionary<string, string?> { ["Telegram:Enabled"] = "false" });
        var client = factory.CreateApiClient();
        var config = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/telegram/config", Ct);
        Assert.False(config.GetProperty("enabled").GetBoolean());
        var signIn = await client.PostAsJsonAsync("/api/auth/telegram/webapp", new { initData = TelegramSigning.InitData(5, DateTimeOffset.UtcNow) }, Ct);
        await AssertProblemAsync(signIn, HttpStatusCode.UnprocessableEntity, "telegram.disabled");
    }
}
