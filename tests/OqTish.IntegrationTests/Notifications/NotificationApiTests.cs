using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OqTish.Application.Identity.Telegram;
using OqTish.Application.Staff;
using OqTish.Domain.Notifications;
using OqTish.Infrastructure.Telegram;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Infrastructure.TestAuth;

namespace OqTish.IntegrationTests.Notifications;

/// <summary>Admin notifications, the Telegram webhook, the dev Telegram endpoint and the config over HTTP (real API).</summary>
public class NotificationApiTests(PostgresFixture postgres)
{
    private const string Secret = "test-webhook-secret-0123456789-abcdefghij";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, string?> Webhook() => new()
    {
        ["Telegram:Mode"] = "Webhook",
        ["Telegram:WebhookSecret"] = Secret,
        ["Telegram:WebhookBaseUrl"] = "https://example.test",
    };

    private static string WebhookPath => "/api/telegram/webhook/" + new TelegramOptions { WebhookSecret = Secret }.WebhookPathSegment;

    private static StringContent Update(long chatId, string text, string chatType = "private") => new(
        $$$"""{"update_id":1,"message":{"message_id":1,"chat":{"id":{{{chatId}}},"type":"{{{chatType}}}"},"from":{"id":{{{chatId}}},"is_bot":false,"language_code":"en"},"text":"{{{text}}}"}}""",
        Encoding.UTF8, "application/json");

    private static async Task<Guid> FailedMessageAsync(ApiFactory factory, string dedup = "test:admin:1")
    {
        await using var db = factory.CreateDbContext();
        var message = OutboxMessage.Create("appointment.confirmed.patient", dedup, "{\"secretPhone\":\"+998901234567\",\"name\":\"Test Bemor\"}", DateTimeOffset.UtcNow, null);
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync(Ct);
        await NotificationKit.ExecAsync(factory.ConnectionString,
            "UPDATE notify.outbox_messages SET status = 'Failed', attempts = 6, last_error_code = 'telegram.http_500', last_error = 'Internal Server Error', channel = 'Telegram' WHERE id = @id",
            ("id", message.Id));
        return message.Id;
    }

    [Fact]
    public async Task Admin_notifications_need_platform_audit_view()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var id = await FailedMessageAsync(factory);
        var patient = await SignInAsPatientAsync(factory);
        var verifier = await ConsoleKit.SignInAsPlatformAdminAsync(factory, PlatformPermissions.ClinicVerify);
        var anonymous = factory.CreateApiClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/notifications", Ct)).StatusCode);
        await AssertProblemAsync(await patient.Client.GetAsync("/api/admin/notifications", Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await verifier.Client.GetAsync("/api/admin/notifications", Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await patient.Client.PostAsync($"/api/admin/notifications/{id}/retry", null, Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await verifier.Client.PostAsync($"/api/admin/notifications/{id}/retry", null, Ct), HttpStatusCode.Forbidden, "common.forbidden");
    }

    [Fact]
    public async Task Admin_list_shows_delivery_state_without_message_bodies_and_retry_moves_failed_to_pending()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var id = await FailedMessageAsync(factory);
        await FailedMessageAsync(factory, "test:admin:2");
        var admin = await ConsoleKit.SignInAsPlatformAdminAsync(factory, PlatformPermissions.AuditView);

        var response = await admin.Client.GetAsync("/api/admin/notifications?status=Failed&pageSize=1", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("901234567", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Test Bemor", text, StringComparison.Ordinal);
        Assert.DoesNotContain("payload", text, StringComparison.OrdinalIgnoreCase);
        using (var doc = JsonDocument.Parse(text))
        {
            Assert.Equal(2, doc.RootElement.GetProperty("totalCount").GetInt32());
            var item = doc.RootElement.GetProperty("items")[0];
            Assert.Equal("appointment.confirmed.patient", item.GetProperty("type").GetString());
            Assert.Equal("patient", item.GetProperty("audience").GetString());
            Assert.Equal("Telegram", item.GetProperty("channel").GetString());
            Assert.Equal("Failed", item.GetProperty("status").GetString());
            Assert.Equal(6, item.GetProperty("attempts").GetInt32());
            Assert.Equal("telegram.http_500", item.GetProperty("lastErrorCode").GetString());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("nextAttemptAt").ValueKind);
        }

        var empty = await admin.Client.GetFromJsonAsync<JsonElement>("/api/admin/notifications?status=Sent", Ct);
        Assert.Equal(0, empty.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.GetAsync("/api/admin/notifications?status=Nope", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.Client.PostAsync($"/api/admin/notifications/{id}/retry", null, Ct)).StatusCode);
        await using (var db = factory.CreateDbContext())
        {
            var m = await db.OutboxMessages.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
            Assert.Equal((OutboxStatus.Pending, 0), (m.Status, m.Attempts));
            Assert.True(m.NextAttemptAt <= DateTimeOffset.UtcNow.AddMinutes(1));
        }

        Assert.Equal(1, await ConsoleKit.AuditCountAsync(factory, "admin.notification.retried"));
        await AssertProblemAsync(await admin.Client.PostAsync($"/api/admin/notifications/{id}/retry", null, Ct), HttpStatusCode.Conflict, "admin.invalid_status_transition");
        await AssertProblemAsync(await admin.Client.PostAsync($"/api/admin/notifications/{Guid.NewGuid()}/retry", null, Ct), HttpStatusCode.NotFound, "common.not_found");
    }

    [Fact]
    public async Task Webhook_requires_the_secret_path_and_header_and_hands_updates_to_the_bot()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, settings: Webhook());
        var client = factory.CreateApiClient(withClientHeader: false);
        var mailbox = factory.Services.GetRequiredService<FakeTelegramMailbox>();

        await AssertProblemAsync(await client.PostAsync("/api/telegram/webhook/wrong-path", Update(1001, "/start"), Ct), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await client.PostAsync(WebhookPath, Update(1001, "/start"), Ct), HttpStatusCode.Unauthorized, "telegram.webhook_unauthorized");
        using (var wrong = new HttpRequestMessage(HttpMethod.Post, WebhookPath) { Content = Update(1001, "/start") })
        {
            wrong.Headers.Add("X-Telegram-Bot-Api-Secret-Token", Secret + "x");
            await AssertProblemAsync(await client.SendAsync(wrong, Ct), HttpStatusCode.Unauthorized, "telegram.webhook_unauthorized");
        }

        Assert.Empty(mailbox.For(1001));

        using var ok = new HttpRequestMessage(HttpMethod.Post, WebhookPath) { Content = Update(1001, "/start") };
        ok.Headers.Add("X-Telegram-Bot-Api-Secret-Token", Secret);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(ok, Ct)).StatusCode);
        Assert.StartsWith("Hello!", Assert.Single(mailbox.For(1001)).Text, StringComparison.Ordinal);

        using var garbage = new HttpRequestMessage(HttpMethod.Post, WebhookPath) { Content = new StringContent("{ not json", Encoding.UTF8, "application/json") };
        garbage.Headers.Add("X-Telegram-Bot-Api-Secret-Token", Secret);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(garbage, Ct)).StatusCode);

        // The API registers the webhook on startup (Fake client records it).
        for (var i = 0; i < 50 && mailbox.WebhookUrl is null; i++)
        {
            await Task.Delay(100, Ct);
        }

        Assert.Equal("https://example.test" + WebhookPath, mailbox.WebhookUrl);
        Assert.Equal(Secret, mailbox.WebhookSecret);
    }

    [Fact]
    public async Task Webhook_is_404_in_polling_mode_even_with_the_right_secret()
    {
        var settings = Webhook();
        settings["Telegram:Mode"] = "Polling";
        await using var factory = await ApiFactory.CreateAsync(postgres, settings: settings);
        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath) { Content = Update(1002, "/start") };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", Secret);
        await AssertProblemAsync(await factory.CreateApiClient(withClientHeader: false).SendAsync(request, Ct), HttpStatusCode.NotFound, "common.not_found");
    }

    [Fact]
    public async Task Dev_telegram_endpoints_show_captured_messages_in_development_and_are_404_in_production()
    {
        await using (var dev = await ApiFactory.CreateAsync(postgres))
        {
            var client = dev.CreateApiClient();
            var simulated = await client.PostAsJsonAsync("/api/dev/telegram/simulate", new { telegramUserId = 3131, text = "/help", languageCode = "ru" }, Ct);
            Assert.Equal("Help", (await simulated.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("outcome").GetString());
            var messages = await client.GetFromJsonAsync<List<JsonElement>>("/api/dev/telegram?chatId=3131", Ct);
            var message = Assert.Single(messages!);
            Assert.Equal(3131, message.GetProperty("chatId").GetInt64());
            Assert.StartsWith("Команды:", message.GetProperty("text").GetString(), StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/dev/telegram/simulate", new { text = "/help" }, Ct)).StatusCode);
        }

        await using var production = await ApiFactory.CreateAsync(postgres, "Production");
        var prod = production.CreateApiClient();
        await AssertProblemAsync(await prod.GetAsync("/api/dev/telegram", Ct), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await prod.PostAsJsonAsync("/api/dev/telegram/simulate", new { telegramUserId = 1, text = "/help" }, Ct), HttpStatusCode.NotFound, "common.not_found");
    }

    [Fact]
    public async Task Telegram_config_exposes_the_bot_id_only_when_enabled()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var config = await factory.CreateApiClient().GetFromJsonAsync<JsonElement>("/api/telegram/config", Ct);
        Assert.True(config.GetProperty("enabled").GetBoolean());
        Assert.Equal(123456, config.GetProperty("botId").GetInt64());

        await using var disabled = await ApiFactory.CreateAsync(postgres, settings: new Dictionary<string, string?> { ["Telegram:Enabled"] = "false" });
        var off = await disabled.CreateApiClient().GetFromJsonAsync<JsonElement>("/api/telegram/config", Ct);
        Assert.False(off.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, off.GetProperty("botId").ValueKind);
    }

    [Fact]
    public async Task Production_refuses_the_fake_telegram_client_and_an_incomplete_webhook_setup()
    {
        await using var fake = await ApiFactory.CreateAsync(postgres, "Production", new Dictionary<string, string?> { ["Telegram:Client"] = "Fake" });
        Assert.Contains("Fake", Assert.ThrowsAny<Exception>(() => fake.CreateApiClient()).ToString(), StringComparison.Ordinal);

        await using var webhook = await ApiFactory.CreateAsync(postgres, "Production", new Dictionary<string, string?> { ["Telegram:Mode"] = "Webhook" });
        Assert.Contains("WebhookSecret", Assert.ThrowsAny<Exception>(() => webhook.CreateApiClient()).ToString(), StringComparison.Ordinal);
    }
}
