using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OqTish.Application.Booking;
using OqTish.Application.Common;
using OqTish.Application.Identity.Handlers;
using OqTish.Application.Notifications;
using OqTish.Application.Notifications.Telegram;
using OqTish.Domain.Notifications;
using OqTish.Infrastructure.Sms;
using OqTish.Infrastructure.Telegram;
using OqTish.IntegrationTests.Booking;
using OqTish.IntegrationTests.Infrastructure;

namespace OqTish.IntegrationTests.Notifications;

/// <summary>
/// BE4 test helpers on top of <see cref="BookingHost"/> (real DI graph, Development → Fake Telegram client and Fake SMS):
/// Telegram enabled with a fake token, no jitter (exact backoff), and shortcuts for linking chats, running the
/// dispatcher, forcing messages due and reading outbox state.
/// </summary>
public static class NotificationKit
{
    public const string ContactPhone = "+998901234567";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static Dictionary<string, string?> Settings(IDictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Telegram:Enabled"] = "true",
            ["Telegram:BotToken"] = "123456:TEST-bot-token-not-real",
            ["Telegram:BotUsername"] = "oqtish_test_bot",
            ["Telegram:MiniAppUrl"] = "https://example.test/tg",
            ["Notifications:JitterRatio"] = "0",
            ["Notifications:PublicBaseUrl"] = "https://example.test",
        };
        foreach (var (k, v) in extra ?? new Dictionary<string, string?>())
        {
            settings[k] = v;
        }

        return settings;
    }

    public static Task<BookingHost> HostAsync(PostgresFixture postgres, IDictionary<string, string?>? extra = null) =>
        BookingHost.CreateAsync(postgres, Settings(extra));

    public static FakeTelegramMailbox Telegram(this BookingHost host) => host.Services.GetRequiredService<FakeTelegramMailbox>();

    public static FakeSmsMailbox Sms(this BookingHost host) => host.Services.GetRequiredService<FakeSmsMailbox>();

    public static Task<int> DispatchAsync(this BookingHost host) =>
        host.RunAsync<int>(null, sp => sp.GetRequiredService<OutboxDispatcher>().RunOnceAsync(Ct));

    /// <summary>Dispatches until nothing due is left (max 10 rounds).</summary>
    public static async Task DispatchAllAsync(this BookingHost host)
    {
        for (var i = 0; i < 10 && await host.DispatchAsync() > 0; i++)
        {
        }
    }

    public static async Task LinkChatAsync(this BookingHost host, Guid userId, long chatId, string language = "uz")
    {
        await using var db = host.CreateDbContext();
        db.TelegramChatLinks.Add(TelegramChatLink.Create(userId, chatId, chatId, DateTimeOffset.UtcNow, language));
        await db.SaveChangesAsync(Ct);
    }

    public static async Task<OutboxMessage> MessageAsync(this BookingHost host, string type, Guid appointmentId)
    {
        await using var db = host.CreateDbContext();
        var key = appointmentId.ToString("N");
        return await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Type == type && m.DedupKey.Contains(key), Ct);
    }

    public static async Task<List<DeliveryAttempt>> AttemptsAsync(this BookingHost host, Guid messageId)
    {
        await using var db = host.CreateDbContext();
        return await db.DeliveryAttempts.AsNoTracking().Where(a => a.OutboxMessageId == messageId).OrderBy(a => a.CreatedAt).ToListAsync(Ct);
    }

    public static Task ExecAsync(this BookingHost host, string sql, params (string Name, object Value)[] parameters) => ExecAsync(host.ConnectionString, sql, parameters);

    public static async Task ExecAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        await using var c = new NpgsqlConnection(connectionString);
        await c.OpenAsync(Ct);
        await using var cmd = new NpgsqlCommand(sql, c);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        await cmd.ExecuteNonQueryAsync(Ct);
    }

    /// <summary>Makes every Pending message (or one) due now (skips the backoff wait / not_before).</summary>
    public static Task MakeDueAsync(this BookingHost host, Guid? messageId = null) => messageId is { } id
        ? host.ExecAsync("UPDATE notify.outbox_messages SET next_attempt_at = clock_timestamp() - interval '1 second' WHERE id = @id AND status = 'Pending'", ("id", id))
        : host.ExecAsync("UPDATE notify.outbox_messages SET next_attempt_at = clock_timestamp() - interval '1 second' WHERE status = 'Pending'");

    /// <summary>Marks every message except <paramref name="keepTypes"/> as Sent so a test only dispatches what it looks at.</summary>
    public static Task OnlyKeepAsync(this BookingHost host, params string[] keepTypes) =>
        host.ExecAsync("UPDATE notify.outbox_messages SET status = 'Sent' WHERE status = 'Pending' AND NOT (type = ANY(@types))", ("types", keepTypes));

    public static Task<Result<AppointmentDto>> RejectAsync(this BookingHost host, BookingWorld w, Guid appointmentId, string reason = "NoDoctorAvailable") =>
        host.RunAsync(w.StaffUserId, sp => sp.GetRequiredService<StaffAppointmentHandlers>().RejectAsync(w.ClinicId, appointmentId, new ReasonCommand(reason), Ct));

    public static Task<Result<AppointmentDto>> ClinicCancelAsync(this BookingHost host, BookingWorld w, Guid appointmentId, string reason = "ClinicClosed") =>
        host.RunAsync(w.StaffUserId, sp => sp.GetRequiredService<StaffAppointmentHandlers>().CancelAsync(w.ClinicId, appointmentId, new ReasonCommand(reason), Ct));

    public static Task<Result<AppointmentDto>> RescheduleAsync(this BookingHost host, BookingWorld w, Guid appointmentId, DateTimeOffset start, Guid doctorId) =>
        host.RunAsync(w.StaffUserId, sp => sp.GetRequiredService<StaffAppointmentHandlers>().RescheduleAsync(w.ClinicId, appointmentId, new RescheduleCommand(start, doctorId), Ct));

    public static Task<Result<AppointmentDto>> PatientCancelAsync(this BookingHost host, Guid patient, Guid appointmentId) =>
        host.RunAsync(patient, sp => sp.GetRequiredService<PatientAppointmentHandlers>().CancelAsync(appointmentId, Ct));

    public static Task<Result<AppointmentDto>> WalkInAsync(this BookingHost host, BookingWorld w, DateTimeOffset start, string phone = "+998901112233") =>
        host.RunAsync(w.StaffUserId, sp => sp.GetRequiredService<WalkInAppointmentHandler>().HandleAsync(
            w.ClinicId, new WalkInCommand(w.BranchId, w.Service30, start, w.Doctors[0], "Ali Valiyev", phone), null, Ct));

    /// <summary>A Pending request confirmed with doctor 0; returns its id.</summary>
    public static async Task<Guid> ConfirmedAppointmentAsync(this BookingHost host, BookingWorld w, Guid patient, DateTimeOffset start)
    {
        var booked = await host.BookAsync(w, patient, start);
        Assert.True(booked.IsSuccess, booked.IsSuccess ? null : booked.Error.Code);
        var confirmed = await host.ConfirmAsync(w, booked.Value.Id, w.Doctors[0]);
        Assert.True(confirmed.IsSuccess, confirmed.IsSuccess ? null : confirmed.Error.Code);
        return booked.Value.Id;
    }

    /// <summary>A private-chat text message from <paramref name="telegramUserId"/>.</summary>
    public static TelegramUpdate Text(long telegramUserId, string text, string? language = "uz", string chatType = "private", long updateId = 1) =>
        new(updateId, new TelegramIncomingMessage(updateId, telegramUserId, chatType, telegramUserId, false, language, text));

    public static Task<BotOutcome> BotAsync(this BookingHost host, TelegramUpdate update) =>
        host.RunAsync(null, sp => sp.GetRequiredService<TelegramBotService>().HandleAsync(update, Ct));

    /// <summary>Creates a deep-link token for <paramref name="userId"/> through the real service; returns the raw token.</summary>
    public static async Task<string> LinkTokenAsync(this BookingHost host, Guid userId)
    {
        var link = await host.RunAsync(userId, sp => sp.GetRequiredService<TelegramLinkService>().CreateLinkAsync(Ct));
        Assert.True(link.IsSuccess, link.IsSuccess ? null : link.Error.Code);
        return link.Value.DeepLink[(link.Value.DeepLink.IndexOf("?start=", StringComparison.Ordinal) + 7)..];
    }
}
