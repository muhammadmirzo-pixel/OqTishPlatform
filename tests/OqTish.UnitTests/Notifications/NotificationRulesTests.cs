using Microsoft.Extensions.Configuration;
using OqTish.Application.Identity.Telegram;
using OqTish.Application.Notifications;
using OqTish.Application.Notifications.Telegram;
using OqTish.Domain.Notifications;
using OqTish.Infrastructure;

namespace OqTish.UnitTests.Notifications;

/// <summary>BE4 pure rules: retry/backoff schedule, templates, languages, command parsing, Telegram options and startup guards.</summary>
public class NotificationRulesTests
{
    private static readonly TimeSpan[] Schedule = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(6)];

    private static NoticeModel Model(string clinic = "Demo Tabassum Stomatologiya") => new(
        clinic, "Chilonzor filiali", "Bunyodkor ko'chasi 1", "Tish tozalash", "Dr. Aziza", "Asia/Tashkent",
        new DateTimeOffset(2026, 10, 20, 5, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 21, 6, 30, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.Zero), "NoDoctorAvailable", "AB12CD34", "24h");

    [Fact]
    public void Backoff_without_jitter_is_one_five_thirty_minutes_two_and_six_hours_then_failed_after_six_attempts()
    {
        var delays = Enumerable.Range(1, 5).Select(n => RetryPolicy.NextDelay(n, Schedule, 0m, 0.5m)).ToList();
        Assert.Equal(Schedule, delays);
        Assert.Equal(TimeSpan.FromHours(6), RetryPolicy.NextDelay(9, Schedule, 0m, 0.5m)); // the last value repeats
        Assert.False(RetryPolicy.IsFinal(5, 6));
        Assert.True(RetryPolicy.IsFinal(6, 6));
        Assert.True(RetryPolicy.IsFinal(7, 6));
    }

    [Theory]
    [InlineData(0, 48)]   // −20 %
    [InlineData(1, 72)]   // +20 %
    [InlineData(0.5, 60)]
    public void Jitter_stays_within_the_ratio(double random, int expectedSeconds)
    {
        var delay = RetryPolicy.NextDelay(1, Schedule, 0.2m, (decimal)random);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }

    [Fact]
    public void Retry_after_longer_than_the_backoff_wins()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), RetryPolicy.NextDelay(1, Schedule, 0m, 0.5m, TimeSpan.FromMinutes(10)));
        Assert.Equal(TimeSpan.FromMinutes(1), RetryPolicy.NextDelay(1, Schedule, 0m, 0.5m, TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void Backoff_schedule_is_parsed_from_configuration_with_a_safe_fallback()
    {
        Assert.Equal(Schedule, new NotificationOptions().BackoffSchedule());
        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)], new NotificationOptions { BackoffSeconds = " 5, x, -3, 10 " }.BackoffSchedule());
        Assert.Equal(Schedule, new NotificationOptions { BackoffSeconds = "garbage" }.BackoffSchedule());
    }

    [Fact]
    public void Every_booking_type_has_a_telegram_template_in_every_language()
    {
        foreach (var type in BookingNotificationTypes.All)
        {
            foreach (var lang in NotificationTemplates.Languages)
            {
                var text = NotificationTemplates.Telegram(type, lang, Model());
                Assert.False(string.IsNullOrWhiteSpace(text), $"{type}/{lang}");
                Assert.Contains("Tish tozalash", text, StringComparison.Ordinal);
            }
        }

        Assert.Null(NotificationTemplates.Telegram("payment.recorded.patient", "uz", Model()));
    }

    [Fact]
    public void Sms_texts_exist_only_for_the_od11_types_and_are_short()
    {
        foreach (var type in BookingNotificationTypes.All)
        {
            foreach (var lang in NotificationTemplates.Languages)
            {
                var sms = NotificationTemplates.Sms(type, lang, Model());
                if (BookingNotificationTypes.SmsFallback.Contains(type))
                {
                    Assert.NotNull(sms);
                    if (lang != "ru")
                    {
                        Assert.True(sms.Length <= 160, $"{type}/{lang}: {sms.Length} chars");
                    }
                }
                else
                {
                    Assert.Null(sms);
                }
            }
        }

        Assert.DoesNotContain(BookingNotificationTypes.ReminderPatient, BookingNotificationTypes.SmsFallback);
        Assert.Equal(
            [BookingNotificationTypes.CancelledByClinicPatient, BookingNotificationTypes.ConfirmedPatient, BookingNotificationTypes.RejectedPatient, BookingNotificationTypes.RescheduledPatient],
            BookingNotificationTypes.SmsFallback.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Times_are_shown_in_the_branch_time_zone()
    {
        var text = NotificationTemplates.Telegram(BookingNotificationTypes.ConfirmedPatient, "uz", Model())!;
        Assert.Contains("20.10.2026 10:00", text, StringComparison.Ordinal); // 05:00Z = 10:00 Asia/Tashkent
        Assert.Contains("Bunyodkor ko'chasi 1", text, StringComparison.Ordinal);
        Assert.Contains("Dr. Aziza", text, StringComparison.Ordinal);
        var rescheduled = NotificationTemplates.Telegram(BookingNotificationTypes.RescheduledPatient, "en", Model())!;
        Assert.Contains("21.10.2026 11:30", rescheduled, StringComparison.Ordinal);
        Assert.Contains("no doctor available", NotificationTemplates.Telegram(BookingNotificationTypes.RejectedPatient, "en", Model())!, StringComparison.Ordinal);
    }

    [Fact]
    public void Staff_texts_show_the_request_number_and_no_patient_data()
    {
        foreach (var type in new[] { BookingNotificationTypes.RequestedStaff, BookingNotificationTypes.CancelledByPatientStaff, BookingNotificationTypes.ExpiredStaff })
        {
            foreach (var lang in NotificationTemplates.Languages)
            {
                var text = NotificationTemplates.Telegram(type, lang, Model())!;
                Assert.Contains("#AB12CD34", text, StringComparison.Ordinal);
                Assert.DoesNotContain("+998", text, StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData(null, "uz")]
    [InlineData("", "uz")]
    [InlineData("uz", "uz")]
    [InlineData("ru", "ru")]
    [InlineData("RU", "ru")]
    [InlineData("en-US", "en")]
    [InlineData("en_GB", "en")]
    [InlineData("de", "uz")]
    [InlineData("kk", "uz")]
    public void Telegram_language_code_maps_to_uz_ru_en_with_uz_fallback(string? code, string expected) =>
        Assert.Equal(expected, TelegramChatLink.NormalizeLanguage(code));

    [Theory]
    [InlineData("/start", "/start", null)]
    [InlineData("/start abcDEF_-123", "/start", "abcDEF_-123")]
    [InlineData("/START@OqTishBot  tok  ", "/start", "tok")]
    [InlineData("/appointments", "/appointments", null)]
    [InlineData("/stop@oqtish_bot", "/stop", null)]
    [InlineData("hello", null, null)]
    public void Bot_commands_are_parsed(string text, string? command, string? argument)
    {
        var (c, a) = TelegramBotService.ParseCommand(text);
        Assert.Equal(command, c);
        Assert.Equal(argument, a);
    }

    [Fact]
    public void Telegram_options_expose_the_bot_id_and_a_stable_secret_path()
    {
        Assert.Equal(123456L, new TelegramOptions { BotToken = "123456:AAH-secret" }.BotId);
        Assert.Null(new TelegramOptions { BotToken = "not-a-token" }.BotId);
        Assert.Null(new TelegramOptions().BotId);

        var a = new TelegramOptions { WebhookSecret = new string('a', 40) };
        var b = new TelegramOptions { WebhookSecret = new string('b', 40) };
        Assert.Equal(32, a.WebhookPathSegment.Length);
        Assert.Equal(a.WebhookPathSegment, new TelegramOptions { WebhookSecret = new string('a', 40) }.WebhookPathSegment);
        Assert.NotEqual(a.WebhookPathSegment, b.WebhookPathSegment);
        Assert.DoesNotContain("aaaa", a.WebhookPathSegment, StringComparison.Ordinal);
        Assert.Equal(string.Empty, new TelegramOptions().WebhookPathSegment);
    }

    [Fact]
    public void Button_urls_must_be_public_https()
    {
        Assert.Equal("https://oqtish.uz/tg", OutboxDispatcher.PublicHttpsUrl("https://oqtish.uz/tg/"));
        Assert.Null(OutboxDispatcher.PublicHttpsUrl("http://oqtish.uz/tg"));
        Assert.Null(OutboxDispatcher.PublicHttpsUrl("https://localhost:5173/tg"));
        Assert.Null(OutboxDispatcher.PublicHttpsUrl("https://127.0.0.1/tg"));
        Assert.Null(OutboxDispatcher.PublicHttpsUrl(""));
        Assert.Null(OutboxDispatcher.PublicHttpsUrl("not a url"));
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    [Fact]
    public void Fake_telegram_client_is_the_development_default_and_refused_in_production()
    {
        Assert.Equal(TelegramClients.Fake, StartupGuards.ValidateTelegram(Config(), new TestHostEnvironment("Development")));
        Assert.Equal(TelegramClients.Real, StartupGuards.ValidateTelegram(Config(), new TestHostEnvironment("Production")));
        Assert.Equal(TelegramClients.Fake, StartupGuards.ValidateTelegram(Config(("Telegram:Client", "fake")), new TestHostEnvironment("Staging")));
        var ex = Assert.Throws<InvalidOperationException>(() => StartupGuards.ValidateTelegram(Config(("Telegram:Client", "Fake")), new TestHostEnvironment("Production")));
        Assert.Contains("Fake", ex.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => StartupGuards.ValidateTelegram(Config(("Telegram:Client", "Mock")), new TestHostEnvironment("Development")));
        Assert.Throws<InvalidOperationException>(() => StartupGuards.ValidateTelegram(Config(("Telegram:Mode", "Push")), new TestHostEnvironment("Development")));
    }

    [Fact]
    public void Webhook_mode_requires_a_strong_secret_and_an_https_base_url_when_enabled()
    {
        var env = new TestHostEnvironment("Production");
        var secret = new string('s', 40);
        Assert.Throws<InvalidOperationException>(() => StartupGuards.ValidateTelegram(Config(("Telegram:Enabled", "true"), ("Telegram:Mode", "Webhook"),
            ("Telegram:WebhookBaseUrl", "https://oqtish.uz")), env));
        Assert.Throws<InvalidOperationException>(() => StartupGuards.ValidateTelegram(Config(("Telegram:Enabled", "true"), ("Telegram:Mode", "Webhook"),
            ("Telegram:WebhookSecret", "short"), ("Telegram:WebhookBaseUrl", "https://oqtish.uz")), env));
        Assert.Throws<InvalidOperationException>(() => StartupGuards.ValidateTelegram(Config(("Telegram:Enabled", "true"), ("Telegram:Mode", "Webhook"),
            ("Telegram:WebhookSecret", new string('x', 39) + "!"), ("Telegram:WebhookBaseUrl", "https://oqtish.uz")), env));
        Assert.Throws<InvalidOperationException>(() => StartupGuards.ValidateTelegram(Config(("Telegram:Enabled", "true"), ("Telegram:Mode", "Webhook"),
            ("Telegram:WebhookSecret", secret), ("Telegram:WebhookBaseUrl", "http://oqtish.uz")), env));
        Assert.Equal(TelegramClients.Real, StartupGuards.ValidateTelegram(Config(("Telegram:Enabled", "true"), ("Telegram:Mode", "Webhook"),
            ("Telegram:WebhookSecret", secret), ("Telegram:WebhookBaseUrl", "https://oqtish.uz")), env));
        // Disabled or polling: no webhook settings needed.
        Assert.Equal(TelegramClients.Real, StartupGuards.ValidateTelegram(Config(("Telegram:Mode", "Webhook")), env));
        Assert.Equal(TelegramClients.Real, StartupGuards.ValidateTelegram(Config(("Telegram:Enabled", "true"), ("Telegram:Mode", "Polling")), env));
    }
}
