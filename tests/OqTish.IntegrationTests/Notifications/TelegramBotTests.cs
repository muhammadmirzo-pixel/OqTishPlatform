using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OqTish.Application.Notifications;
using OqTish.Application.Notifications.Telegram;
using OqTish.Domain.Identity;
using OqTish.Domain.Notifications;
using OqTish.IntegrationTests.Booking;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Notifications.NotificationKit;

namespace OqTish.IntegrationTests.Notifications;

/// <summary>Bot commands through the real <see cref="TelegramBotService"/> (Fake Telegram client, real PostgreSQL).</summary>
public class TelegramBotTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Start_with_a_valid_token_links_the_chat_in_the_users_language_and_the_token_works_once()
    {
        await using var host = await HostAsync(postgres);
        await using var db = host.CreateDbContext();
        var user = User.Create("Bemor", DateTimeOffset.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync(Ct);
        var token = await host.LinkTokenAsync(user.Id);

        Assert.Equal(BotOutcome.Linked, await host.BotAsync(Text(4242, $"/start {token}", "ru")));
        var link = await db.TelegramChatLinks.AsNoTracking().SingleAsync(l => l.UserId == user.Id, Ct);
        Assert.Equal((4242L, 4242L, true, "ru"), (link.TelegramUserId, link.ChatId, link.IsActive, link.LanguageCode));
        Assert.True(await db.AuthIdentities.AnyAsync(i => i.UserId == user.Id && i.Kind == AuthIdentityKind.Telegram && i.NormalizedIdentifier == "4242", Ct));
        var reply = Assert.Single(host.Telegram().For(4242));
        Assert.StartsWith("Готово!", reply.Text, StringComparison.Ordinal);
        Assert.Equal("https://example.test/tg", reply.Buttons![0][0].WebAppUrl);

        // Reused token → invalid, nothing changes.
        Assert.Equal(BotOutcome.LinkInvalid, await host.BotAsync(Text(4343, $"/start {token}", "en")));
        Assert.False(await db.TelegramChatLinks.AnyAsync(l => l.TelegramUserId == 4343, Ct));
        Assert.Contains("invalid", host.Telegram().For(4343)[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_with_an_expired_or_unknown_token_is_refused()
    {
        await using var host = await HostAsync(postgres);
        await using var db = host.CreateDbContext();
        var user = User.Create("Bemor", DateTimeOffset.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync(Ct);
        var token = await host.LinkTokenAsync(user.Id);
        await host.ExecAsync("UPDATE notify.telegram_link_tokens SET expires_at = clock_timestamp() - interval '1 second' WHERE user_id = @u", ("u", user.Id));

        Assert.Equal(BotOutcome.LinkInvalid, await host.BotAsync(Text(5151, $"/start {token}")));
        Assert.Equal(BotOutcome.LinkInvalid, await host.BotAsync(Text(5151, "/start not-a-real-token")));
        Assert.False(await db.TelegramChatLinks.AnyAsync(Ct));
        Assert.All(host.Telegram().For(5151), m => Assert.StartsWith("Havola yaroqsiz", m.Text, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Start_token_for_a_telegram_account_owned_by_another_user_is_a_conflict_without_merge()
    {
        await using var host = await HostAsync(postgres);
        await using var db = host.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var owner = User.Create("Egasi", now);
        var other = User.Create("Boshqa", now);
        db.Users.AddRange(owner, other);
        await db.SaveChangesAsync(Ct);
        db.AuthIdentities.Add(AuthIdentity.Create(owner.Id, AuthIdentityKind.Telegram, "6262", verified: true, now));
        await db.SaveChangesAsync(Ct);
        var token = await host.LinkTokenAsync(other.Id);

        Assert.Equal(BotOutcome.LinkConflict, await host.BotAsync(Text(6262, $"/start {token}")));
        Assert.False(await db.TelegramChatLinks.AnyAsync(Ct));
        Assert.False(await db.AuthIdentities.AnyAsync(i => i.UserId == other.Id && i.Kind == AuthIdentityKind.Telegram, Ct));
    }

    [Fact]
    public async Task Plain_start_links_a_known_telegram_identity_and_otherwise_welcomes_with_the_mini_app_button()
    {
        await using var host = await HostAsync(postgres);
        await using var db = host.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var user = User.Create("Mini App foydalanuvchisi", now);
        db.Users.Add(user);
        await db.SaveChangesAsync(Ct);
        db.AuthIdentities.Add(AuthIdentity.Create(user.Id, AuthIdentityKind.Telegram, "7373", verified: true, now));
        await db.SaveChangesAsync(Ct);

        Assert.Equal(BotOutcome.Linked, await host.BotAsync(Text(7373, "/start", "en")));
        Assert.True(await db.TelegramChatLinks.AnyAsync(l => l.UserId == user.Id && l.ChatId == 7373 && l.IsActive && l.LanguageCode == "en", Ct));

        Assert.Equal(BotOutcome.Welcome, await host.BotAsync(Text(7474, "/start", "uz")));
        var welcome = Assert.Single(host.Telegram().For(7474));
        Assert.StartsWith("Assalomu alaykum!", welcome.Text, StringComparison.Ordinal);
        Assert.Equal("OqTish'ni ochish", welcome.Buttons![0][0].Text);
        Assert.False(await db.TelegramChatLinks.AnyAsync(l => l.TelegramUserId == 7474, Ct));
    }

    [Fact]
    public async Task Group_chats_bots_and_non_text_updates_are_ignored()
    {
        await using var host = await HostAsync(postgres);
        Assert.Equal(BotOutcome.Ignored, await host.BotAsync(Text(-100123, "/start", chatType: "group")));
        Assert.Equal(BotOutcome.Ignored, await host.BotAsync(Text(-100124, "/appointments", chatType: "supergroup")));
        Assert.Equal(BotOutcome.Ignored, await host.BotAsync(new TelegramUpdate(1, new TelegramIncomingMessage(1, 8080, "private", 8080, true, "uz", "/start"))));
        Assert.Equal(BotOutcome.Ignored, await host.BotAsync(new TelegramUpdate(2, new TelegramIncomingMessage(2, 8080, "private", 8080, false, "uz", null))));
        Assert.Equal(BotOutcome.Ignored, await host.BotAsync(new TelegramUpdate(3, null)));
        Assert.Equal(0, host.Telegram().SendCalls);
    }

    [Fact]
    public async Task Stop_deactivates_the_link_and_start_reactivates_it()
    {
        await using var host = await HostAsync(postgres);
        await using var db = host.CreateDbContext();
        var user = User.Create("Bemor", DateTimeOffset.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync(Ct);
        await host.LinkChatAsync(user.Id, 8181);

        Assert.Equal(BotOutcome.Stopped, await host.BotAsync(Text(8181, "/stop")));
        var stopped = await db.TelegramChatLinks.AsNoTracking().SingleAsync(l => l.UserId == user.Id, Ct);
        Assert.Equal((false, "stopped"), (stopped.IsActive, stopped.DeactivationReason));
        Assert.Equal(BotOutcome.NotLinked, await host.BotAsync(Text(8181, "/stop")));
        Assert.Equal(BotOutcome.NotLinked, await host.BotAsync(Text(8181, "/appointments")));

        Assert.Equal(BotOutcome.Reactivated, await host.BotAsync(Text(8181, "/start", "ru")));
        var active = await db.TelegramChatLinks.AsNoTracking().SingleAsync(l => l.UserId == user.Id, Ct);
        Assert.Equal((true, null, "ru"), (active.IsActive, active.DeactivationReason, active.LanguageCode));
        Assert.Equal(1, await db.AuditLog.CountAsync(a => a.Action == "notify.telegram_stopped", Ct));
    }

    [Fact]
    public async Task Appointments_lists_only_the_linked_patients_own_upcoming_appointments()
    {
        await using var host = await HostAsync(postgres);
        await using var db = host.CreateDbContext();
        var w = await BookingWorld.CreateAsync(db, doctors: 2, patients: 2);
        await host.LinkChatAsync(w.Patients[0], 9191);
        var mine = (await host.BookAsync(w, w.Patients[0], w.At(10))).Value;
        await host.ConfirmedAppointmentAsync(w, w.Patients[0], w.At(12));
        await host.BookAsync(w, w.Patients[1], w.At(15));

        Assert.Equal(BotOutcome.Appointments, await host.BotAsync(Text(9191, "/appointments")));
        var reply = Assert.Single(host.Telegram().For(9191)).Text;
        string Local(DateTimeOffset t) => NotificationTemplates.Format(t, "Asia/Tashkent");
        Assert.StartsWith("Yaqin qabullaringiz:", reply, StringComparison.Ordinal);
        Assert.Contains(Local(mine.StartsAt) + " — tasdiqlash kutilmoqda", reply, StringComparison.Ordinal);
        Assert.Contains(Local(w.At(12)) + " — tasdiqlangan", reply, StringComparison.Ordinal);
        Assert.DoesNotContain(Local(w.At(15)), reply, StringComparison.Ordinal);
        Assert.DoesNotContain("901234567", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_chat_is_rate_limited()
    {
        await using var host = await HostAsync(postgres, new Dictionary<string, string?> { ["Telegram:ChatRateLimitPerMinute"] = "3" });
        var outcomes = new List<BotOutcome>();
        for (var i = 0; i < 10; i++)
        {
            outcomes.Add(await host.BotAsync(Text(9292, "/help")));
        }

        // At most 3 per one-minute window pass; even if a minute boundary falls inside the loop, one window gets ≥ 5 calls.
        Assert.Contains(BotOutcome.RateLimited, outcomes);
        Assert.True(outcomes.Count(o => o == BotOutcome.Help) <= 6);
        Assert.Equal(BotOutcome.Help, await host.BotAsync(Text(9393, "/help"))); // other chats are unaffected
    }

    [Fact]
    public async Task Poller_handles_queued_updates_once_and_persists_the_offset()
    {
        await using var host = await HostAsync(postgres);
        host.Telegram().EnqueueUpdate(Text(9494, "/help", updateId: 100));
        host.Telegram().EnqueueUpdate(Text(9494, "/start", updateId: 101));

        var first = await host.RunAsync(null, sp => sp.GetRequiredService<TelegramPoller>().PollOnceAsync(Ct));
        Assert.True(first.IsSuccess);
        Assert.Equal(2, first.Value);
        await using var db = host.CreateDbContext();
        Assert.Equal(102, await db.TelegramBotStates.Where(s => s.BotId == 123456).Select(s => s.UpdateOffset).SingleAsync(Ct));

        // An already handled update (redelivered) is skipped by the offset.
        host.Telegram().EnqueueUpdate(Text(9494, "/help", updateId: 101));
        var second = await host.RunAsync(null, sp => sp.GetRequiredService<TelegramPoller>().PollOnceAsync(Ct));
        Assert.Equal(0, second.Value);
        Assert.Equal(2, host.Telegram().For(9494).Count);
    }

    [Fact]
    public async Task A_blocked_reply_is_not_an_error()
    {
        await using var host = await HostAsync(postgres);
        host.Telegram().FailNext(new TelegramError(TelegramFailureKind.ChatUnavailable, "telegram.blocked", "403", null));
        Assert.Equal(BotOutcome.Help, await host.BotAsync(Text(9595, "/help")));
        Assert.Empty(host.Telegram().For(9595));
    }
}
