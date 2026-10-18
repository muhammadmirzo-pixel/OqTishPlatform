using System.Net;
using System.Net.Http.Json;
using OqTish.Application.Identity;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Infrastructure.TestAuth;

namespace OqTish.IntegrationTests.Identity;

public class TelegramSignInTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Valid_init_data_creates_a_patient_once()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var first = await factory.CreateApiClient().PostAsJsonAsync("/api/auth/telegram/webapp", new { initData = TelegramSigning.InitData(9001, DateTimeOffset.UtcNow, firstName: "Aziz") }, Ct);
        var second = await factory.CreateApiClient().PostAsJsonAsync("/api/auth/telegram/webapp", new { initData = TelegramSigning.InitData(9001, DateTimeOffset.UtcNow.AddMinutes(-1)) }, Ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var a = (await first.Content.ReadFromJsonAsync<MeDto>(Json, Ct))!;
        var b = (await second.Content.ReadFromJsonAsync<MeDto>(Json, Ct))!;
        Assert.Equal(a.Id, b.Id);
        Assert.Equal("Aziz", a.DisplayName);
        Assert.True(a.TelegramLinked);
        Assert.False(a.PhoneVerified);
    }

    [Fact]
    public async Task Tampered_and_stale_init_data_are_rejected()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateApiClient();
        var tampered = TelegramSigning.InitData(9002, DateTimeOffset.UtcNow).Replace("9002", "9003", StringComparison.Ordinal);
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/auth/telegram/webapp", new { initData = tampered }, Ct), HttpStatusCode.Unauthorized, "auth.telegram_invalid");

        var stale = TelegramSigning.InitData(9002, DateTimeOffset.UtcNow.AddHours(-25));
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/auth/telegram/webapp", new { initData = stale }, Ct), HttpStatusCode.Unauthorized, "auth.telegram_invalid");

        var otherBot = TelegramSigning.InitData(9002, DateTimeOffset.UtcNow, botToken: "999:other");
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/auth/telegram/webapp", new { initData = otherBot }, Ct), HttpStatusCode.Unauthorized, "auth.telegram_invalid");
    }

    [Fact]
    public async Task Login_widget_hash_is_verified()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateApiClient();
        var widget = TelegramSigning.Widget(9010, DateTimeOffset.UtcNow);
        var ok = await client.PostAsJsonAsync("/api/auth/telegram/widget", widget, Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var tampered = new Dictionary<string, object>(widget) { ["first_name"] = "Eve" };
        await AssertProblemAsync(await factory.CreateApiClient().PostAsJsonAsync("/api/auth/telegram/widget", tampered, Ct), HttpStatusCode.Unauthorized, "auth.telegram_invalid");
    }
}
