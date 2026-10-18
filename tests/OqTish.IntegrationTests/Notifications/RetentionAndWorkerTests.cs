extern alias WorkerHost;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OqTish.Application.Notifications;
using OqTish.Domain.Audit;
using OqTish.Domain.Identity;
using OqTish.Domain.Notifications;
using OqTish.IntegrationTests.Booking;
using OqTish.IntegrationTests.Infrastructure;
using WorkerHost::OqTish.Worker;

namespace OqTish.IntegrationTests.Notifications;

/// <summary>Retention deletes only eligible rows (OD-18); the Worker's dispatcher service delivers on its timer.</summary>
public class RetentionAndWorkerTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Retention_deletes_only_eligible_rows_and_never_touches_audit()
    {
        await using var host = await NotificationKit.HostAsync(postgres);
        await using var db = host.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var user = User.Create("Retention", now);
        db.Users.Add(user);
        await db.SaveChangesAsync(Ct);

        // OTP challenges: 2 days old (deleted) vs 1 hour old (kept).
        db.OtpChallenges.Add(OtpChallenge.Create(Guid.NewGuid(), OtpPurpose.SignIn, "old", null, null, "code", null, now.AddDays(-2), TimeSpan.FromMinutes(5), 5));
        var freshOtp = OtpChallenge.Create(Guid.NewGuid(), OtpPurpose.SignIn, "fresh", null, null, "code", null, now.AddHours(-1), TimeSpan.FromMinutes(5), 5);
        db.OtpChallenges.Add(freshOtp);

        // Sessions: expired 40 days ago and revoked 31 days ago (deleted); active and revoked yesterday (kept).
        db.Sessions.Add(Session.Create(user.Id, "h1", user.SecurityStamp, "Otp", now.AddDays(-70), TimeSpan.FromDays(30), null, null));
        var revokedLongAgo = Session.Create(user.Id, "h2", user.SecurityStamp, "Otp", now.AddDays(-31), TimeSpan.FromDays(30), null, null);
        revokedLongAgo.Revoke("logout", now.AddDays(-31));
        db.Sessions.Add(revokedLongAgo);
        var active = Session.Create(user.Id, "h3", user.SecurityStamp, "Otp", now, TimeSpan.FromDays(30), null, null);
        var revokedRecently = Session.Create(user.Id, "h4", user.SecurityStamp, "Otp", now.AddDays(-1), TimeSpan.FromDays(30), null, null);
        revokedRecently.Revoke("logout", now.AddDays(-1));
        db.Sessions.AddRange(active, revokedRecently);

        // Link tokens: expired 3 days ago (deleted) vs fresh (kept).
        db.TelegramLinkTokens.Add(TelegramLinkToken.Create(user.Id, new string('a', 64), now.AddDays(-3), TimeSpan.FromMinutes(10)));
        var freshToken = TelegramLinkToken.Create(user.Id, new string('b', 64), now, TimeSpan.FromMinutes(10));
        db.TelegramLinkTokens.Add(freshToken);

        // Outbox: by status and age.
        OutboxMessage Message(string key, int daysOld)
        {
            var m = OutboxMessage.Create("appointment.confirmed.patient", key, "{}", now.AddDays(-daysOld), null);
            db.OutboxMessages.Add(m);
            return m;
        }

        var oldSent = Message("r:sent:old", 31);
        Message("r:skipped:old", 40);
        Message("r:cancelled:old", 40);
        var newSent = Message("r:sent:new", 5);
        var oldPending = Message("r:pending:old", 60);
        var oldProcessing = Message("r:processing:old", 60);
        var failed40 = Message("r:failed:40", 40);
        Message("r:failed:100", 100);
        await db.SaveChangesAsync(Ct);
        db.DeliveryAttempts.Add(DeliveryAttempt.Create(oldSent.Id, 1, "user:x", DeliveryChannel.Telegram, DeliveryOutcome.Sent, "200", null, null, now.AddDays(-31)));
        db.DeliveryAttempts.Add(DeliveryAttempt.Create(newSent.Id, 1, "user:x", DeliveryChannel.Telegram, DeliveryOutcome.Sent, "200", null, null, now.AddDays(-5)));
        db.AuditLog.Add(AuditLogEntry.Create(now.AddYears(-3), AuditActorKind.System, null, "test.retention", null, null, null, null, null, null, null));
        await db.SaveChangesAsync(Ct);
        await host.ExecAsync("""
            UPDATE notify.outbox_messages SET status = CASE
              WHEN dedup_key LIKE 'r:sent:%' THEN 'Sent' WHEN dedup_key LIKE 'r:skipped:%' THEN 'Skipped'
              WHEN dedup_key LIKE 'r:cancelled:%' THEN 'Cancelled' WHEN dedup_key LIKE 'r:failed:%' THEN 'Failed'
              WHEN dedup_key LIKE 'r:processing:%' THEN 'Processing' ELSE status END
            """);
        var auditBefore = await db.AuditLog.CountAsync(Ct);

        var result = await host.RunAsync<RetentionResult>(null, sp => sp.GetRequiredService<RetentionHandler>().RunAsync(Ct));

        Assert.Equal(new RetentionResult(OtpChallenges: 1, Sessions: 2, OutboxMessages: 4, LinkTokens: 1, DevMessages: 0), result);
        await using var check = host.CreateDbContext();
        Assert.Equal([freshOtp.Id], await check.OtpChallenges.Select(o => o.Id).ToListAsync(Ct));
        Assert.Equal([active.Id, revokedRecently.Id], (await check.Sessions.Select(s => s.Id).ToListAsync(Ct)).Order());
        Assert.Equal([freshToken.Id], await check.TelegramLinkTokens.Select(t => t.Id).ToListAsync(Ct));
        Assert.Equal(
            new[] { newSent.Id, oldPending.Id, oldProcessing.Id, failed40.Id }.Order(),
            (await check.OutboxMessages.Select(m => m.Id).ToListAsync(Ct)).Order());
        Assert.Equal([newSent.Id], await check.DeliveryAttempts.Select(a => a.OutboxMessageId).ToListAsync(Ct)); // cascade
        Assert.Equal(auditBefore, await check.AuditLog.CountAsync(Ct));
    }

    [Fact]
    public async Task Worker_dispatcher_service_delivers_due_messages_on_its_timer()
    {
        await using var host = await NotificationKit.HostAsync(postgres);
        await using var db = host.CreateDbContext();
        var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 1);
        await host.LinkChatAsync(w.StaffUserId, 4001);
        var booked = (await host.BookAsync(w, w.Patients[0], w.At(10))).Value;

        using var service = new OutboxDispatcherService(
            host.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new NotificationOptions { DispatchIntervalSeconds = 1 }),
            NullLogger<OutboxDispatcherService>.Instance,
            TimeProvider.System);
        await service.StartAsync(Ct);
        try
        {
            for (var i = 0; i < 100 && host.Telegram().For(4001).Count == 0; i++)
            {
                await Task.Delay(100, Ct);
            }
        }
        finally
        {
            await service.StopAsync(Ct);
        }

        Assert.Single(host.Telegram().For(4001));
        Assert.Equal(OutboxStatus.Sent, (await host.MessageAsync(BookingNotificationTypes.RequestedStaff, booked.Id)).Status);
        Assert.Equal(AppointmentStatusPending, (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == booked.Id, Ct)).Status);
    }

    private const OqTish.Domain.Booking.AppointmentStatus AppointmentStatusPending = OqTish.Domain.Booking.AppointmentStatus.Pending;
}
