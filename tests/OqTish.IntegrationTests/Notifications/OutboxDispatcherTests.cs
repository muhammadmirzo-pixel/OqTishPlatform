using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OqTish.Application.Notifications;
using OqTish.Application.Notifications.Telegram;
using OqTish.Domain.Booking;
using OqTish.Domain.Identity;
using OqTish.Domain.Notifications;
using OqTish.Domain.Staff;
using OqTish.Domain.Tenancy;
using OqTish.IntegrationTests.Booking;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.Application.Notifications.BookingNotificationTypes;

namespace OqTish.IntegrationTests.Notifications;

/// <summary>
/// The outbox dispatcher against real PostgreSQL with the Fake Telegram client and Fake SMS (TESTING.md §5, N1–N5).
/// Written in BE4; executed in the final test stage.
/// </summary>
public class OutboxDispatcherTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TelegramError Http500 => new(TelegramFailureKind.Transient, "telegram.http_500", "500", "Internal Server Error");

    [Fact]
    public async Task N1_telegram_500_keeps_the_appointment_retries_with_backoff_and_fails_after_six_attempts()
    {
        await using var host = await NotificationKit.HostAsync(postgres);
        await using var db = host.CreateDbContext();
        var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 1);
        await host.LinkChatAsync(w.Patients[0], 5001);
        var booked = (await host.BookAsync(w, w.Patients[0], w.At(10))).Value;
        await host.OnlyKeepAsync(RequestedPatient);
        host.Telegram().SendBehavior = _ => Http500;

        var expected = new[] { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(6) };
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            var before = DateTimeOffset.UtcNow;
            Assert.Equal(1, await host.DispatchAsync());
            var m = await host.MessageAsync(RequestedPatient, booked.Id);
            Assert.Equal(attempt, m.Attempts);
            Assert.Equal("telegram.http_500", m.LastErrorCode);
            Assert.Null(m.ClaimToken);
            if (attempt < 6)
            {
                Assert.Equal(OutboxStatus.Pending, m.Status);
                var delay = m.NextAttemptAt - before;
                Assert.InRange(delay, expected[attempt - 1] - TimeSpan.FromSeconds(30), expected[attempt - 1] + TimeSpan.FromSeconds(30));
                Assert.Equal(0, await host.DispatchAsync()); // not due yet
                await host.MakeDueAsync(m.Id);
            }
            else
            {
                Assert.Equal(OutboxStatus.Failed, m.Status);
            }
        }

        var attempts = await host.AttemptsAsync((await host.MessageAsync(RequestedPatient, booked.Id)).Id);
        Assert.Equal(6, attempts.Count);
        Assert.All(attempts, a => Assert.Equal((DeliveryChannel.Telegram, DeliveryOutcome.Retry, "500"), (a.Channel, a.Outcome, a.ProviderStatus)));
        Assert.Equal([1, 2, 3, 4, 5, 6], attempts.Select(a => a.AttemptNumber));

        // The business data never moved.
        var appointment = await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == booked.Id, Ct);
        Assert.Equal(AppointmentStatus.Pending, appointment.Status);
        await BookingInvariants.AssertAsync(host.ConnectionString);
    }

    [Fact]
    public async Task N2_crash_after_send_does_not_resend_to_a_recipient_with_a_recorded_outcome_and_the_dedup_key_bounds_duplicates()
    {
        await using var host = await NotificationKit.HostAsync(postgres);
        await using var db = host.CreateDbContext();
        var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 1);
        await host.LinkChatAsync(w.StaffUserId, 6001);
        var booked = (await host.BookAsync(w, w.Patients[0], w.At(10))).Value;
        await host.OnlyKeepAsync(RequestedStaff);

        // A second enqueue of the same event (replayed change) adds nothing: one message per dedup key.
        Assert.Equal(1, await db.OutboxMessages.CountAsync(m => m.Type == RequestedStaff, Ct));

        await host.DispatchAsync();
        var sent = await host.MessageAsync(RequestedStaff, booked.Id);
        Assert.Equal(OutboxStatus.Sent, sent.Status);
        Assert.Single(host.Telegram().For(6001));

        // Simulate a crash after the provider call and the attempt row but before the final status write:
        // the row is stuck Processing with an expired lease.
        await host.ExecAsync("UPDATE notify.outbox_messages SET status = 'Processing', claim_token = gen_random_uuid(), locked_until = clock_timestamp() - interval '1 minute' WHERE id = @id", ("id", sent.Id));
        Assert.Equal(1, await host.DispatchAsync()); // reclaimed
        var after = await host.MessageAsync(RequestedStaff, booked.Id);
        Assert.Equal(OutboxStatus.Sent, after.Status);
        Assert.Equal(2, after.Attempts);
        Assert.Single(host.Telegram().For(6001)); // the recipient already had a Sent attempt → not sent again

        // Crash before the attempt row (documented at-least-once): the recipient gets the message once more, never more.
        await host.ExecAsync("DELETE FROM notify.delivery_attempts WHERE outbox_message_id = @id", ("id", sent.Id));
        await host.ExecAsync("UPDATE notify.outbox_messages SET status = 'Processing', claim_token = gen_random_uuid(), locked_until = clock_timestamp() - interval '1 minute' WHERE id = @id", ("id", sent.Id));
        await host.DispatchAsync();
        Assert.Equal(2, host.Telegram().For(6001).Count);
    }

    [Fact]
    public async Task Lease_reclaim_takes_only_expired_processing_rows_and_concurrent_claims_are_disjoint()
    {
        await using var host = await NotificationKit.HostAsync(postgres);
        await using var db = host.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 40; i++)
        {
            db.OutboxMessages.Add(OutboxMessage.Create("test.claim", $"test:claim:{i}", "{}", now.AddMinutes(-1), null));
        }

        await db.SaveChangesAsync(Ct);
        var leased = await db.OutboxMessages.Where(m => m.DedupKey == "test:claim:0").Select(m => m.Id).SingleAsync(Ct);
        var expired = await db.OutboxMessages.Where(m => m.DedupKey == "test:claim:1").Select(m => m.Id).SingleAsync(Ct);
        await host.ExecAsync("UPDATE notify.outbox_messages SET status = 'Processing', locked_until = clock_timestamp() + interval '5 minutes' WHERE id = @id", ("id", leased));
        await host.ExecAsync("UPDATE notify.outbox_messages SET status = 'Processing', locked_until = clock_timestamp() - interval '1 second', attempts = 1 WHERE id = @id", ("id", expired));

        using var gate = new Barrier(2);
        async Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimAsync()
        {
            await using var scope = host.Scope(null);
            var claimer = scope.ServiceProvider.GetRequiredService<IOutboxClaimer>();
            gate.SignalAndWait(Ct);
            return await claimer.ClaimDueAsync(25, TimeSpan.FromMinutes(5), Ct);
        }

        var results = await Task.WhenAll(Task.Run(ClaimAsync, Ct), Task.Run(ClaimAsync, Ct));
        var all = results.SelectMany(r => r.Select(c => c.Id)).ToList();
        Assert.Equal(all.Count, all.Distinct().Count()); // SKIP LOCKED: no row claimed twice
        Assert.Equal(39, all.Count);                      // 38 pending + the expired lease, not the live lease
        Assert.DoesNotContain(leased, all);
        Assert.Contains(expired, all);
        Assert.Equal(2, results.SelectMany(r => r).Single(c => c.Id == expired).Attempt);
        Assert.Equal(39, await db.OutboxMessages.CountAsync(m => m.Status == OutboxStatus.Processing && m.ClaimToken != null, Ct));
    }

    [Fact]
    public async Task Unknown_types_fail_permanently_without_a_send()
    {
        await using var host = await NotificationKit.HostAsync(postgres);
        await using var db = host.CreateDbContext();
        db.OutboxMessages.Add(OutboxMessage.Create("test.unknown", "test:unknown", "{}", DateTimeOffset.UtcNow.AddMinutes(-1), null));
        await db.SaveChangesAsync(Ct);
        await host.DispatchAsync();
        var m = await db.OutboxMessages.AsNoTracking().SingleAsync(x => x.DedupKey == "test:unknown", Ct);
        Assert.Equal((OutboxStatus.Failed, "dispatch.unknown_type"), (m.Status, m.LastErrorCode));
        Assert.Equal(0, host.Telegram().SendCalls);
    }

    [Fact]
    public async Task N3_without_a_telegram_link_confirmed_falls_back_to_sms_and_other_types_are_skipped()
    {
        await using var host = await NotificationKit.HostAsync(postgres);
        await using var db = host.CreateDbContext();
        var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 1);
        var id = await host.ConfirmedAppointmentAsync(w, w.Patients[0], w.At(10));
        await host.OnlyKeepAsync(RequestedPatient, ConfirmedPatient, ReminderPatient);
        await host.MakeDueAsync(); // reminders too
        await host.DispatchAllAsync();

        Assert.Equal((OutboxStatus.Sent, "Sms"), Pair(await host.MessageAsync(ConfirmedPatient, id)));
        var sms = Assert.Single(host.Sms().For(NotificationKit.ContactPhone));
        Assert.StartsWith("OqTish: qabulingiz tasdiqlandi.", sms.Text, StringComparison.Ordinal);

        var requested = await host.MessageAsync(RequestedPatient, id);
        Assert.Equal((OutboxStatus.Skipped, OutboxDispatcher.NoChannel), (requested.Status, requested.LastErrorCode));
        await using var check = host.CreateDbContext();
        var reminders = await check.OutboxMessages.AsNoTracking().Where(m => m.Type == ReminderPatient).ToListAsync(Ct);
        Assert.Equal(2, reminders.Count);
        Assert.All(reminders, r => Assert.Equal(OutboxStatus.Skipped, r.Status)); // reminders are Telegram only (OD-11)
        Assert.Equal(0, host.Telegram().SendCalls);
    }

    [Fact]
    public async Task Linked_patient_gets_telegram_in_their_language_with_a_mini_app_button_and_no_sms()
    {
        await using var host = await NotificationKit.HostAsync(postgres);
        await using var db = host.CreateDbContext();
        var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 1);
        await host.LinkChatAsync(w.Patients[0], 5002, "ru");
        var id = await host.ConfirmedAppointmentAsync(w, w.Patients[0], w.At(10));
        await host.OnlyKeepAsync(ConfirmedPatient);
        await host.DispatchAsync();

        Assert.Equal((OutboxStatus.Sent, "Telegram"), Pair(await host.MessageAsync(ConfirmedPatient, id)));
        var message = Assert.Single(host.Telegram().For(5002));
        Assert.StartsWith("Ваша запись подтверждена.", message.Text, StringComparison.Ordinal);
        Assert.Contains("Dr. A", message.Text, StringComparison.Ordinal);
        Assert.Contains("Test 1", message.Text, StringComparison.Ordinal); // branch address
        Assert.Equal($"https://example.test/tg/appointments/{id}", message.Buttons![0][0].WebAppUrl);
        Assert.Empty(host.Sms().For(NotificationKit.ContactPhone));
    }

    [Fact]
    public async Task Sms_fallback_is_used_only_for_rejected_confirmed_cancelled_by_clinic_and_rescheduled()
    {
        await using var host = await NotificationKit.HostAsync(postgres);
        await using var db = host.CreateDbContext();
        var w = await BookingWorld.CreateAsync(db, doctors: 2, patients: 3);

        var rejected = (await host.BookAsync(w, w.Patients[0], w.At(9))).Value.Id;
        Assert.True((await host.RejectAsync(w, rejected)).IsSuccess);
        var cancelled = await host.ConfirmedAppointmentAsync(w, w.Patients[1], w.At(11));
        Assert.True((await host.ClinicCancelAsync(w, cancelled)).IsSuccess);
        var moved = await host.ConfirmedAppointmentAsync(w, w.Patients[2], w.At(13));
        Assert.True((await host.RescheduleAsync(w, moved, w.At(15), w.Doctors[1])).IsSuccess);
        var expired = (await host.BookAsync(w, w.Patients[0], w.At(17))).Value.Id;
        await host.SetDeadlineAsync(expired, TimeSpan.FromSeconds(-1));
        Assert.Equal(1, await host.SweepAsync());
        await host.ExecAsync("UPDATE notify.outbox_messages SET status = 'Sent' WHERE status = 'Pending' AND (type LIKE '%.staff' OR type = @t)", ("t", ReminderPatient));
        await host.DispatchAllAsync();

        foreach (var (type, appointment) in new[] { (RejectedPatient, rejected), (ConfirmedPatient, cancelled), (CancelledByClinicPatient, cancelled), (RescheduledPatient, moved) })
        {
            Assert.Equal((OutboxStatus.Sent, "Sms"), Pair(await host.MessageAsync(type, appointment)));
        }

        foreach (var (type, appointment) in new[] { (RequestedPatient, rejected), (ExpiredPatient, expired) })
        {
            Assert.Equal(OutboxStatus.Skipped, (await host.MessageAsync(type, appointment)).Status);
        }

        var texts = host.Sms().For(NotificationKit.ContactPhone).Select(s => s.Text).ToList();
        Assert.Contains(texts, t => t.Contains("rad etildi", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.Contains("bekor qildi", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.Contains("ko'chirildi", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t.Contains("muddati", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Walk_ins_get_sms_to_the_contact_phone_for_clinic_cancel_and_reschedule_only()
    {
        await using var host = await NotificationKit.HostAsync(postgres);
        await using var db = host.CreateDbContext();
        var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 0);
        var walkIn = await host.WalkInAsync(w, w.At(10));
        Assert.True(walkIn.IsSuccess, walkIn.IsSuccess ? null : walkIn.Error.Code);
        Assert.Equal(0, await db.OutboxMessages.CountAsync(Ct)); // creating a walk-in sends nothing

        Assert.True((await host.RescheduleAsync(w, walkIn.Value.Id, w.At(12), w.Doctors[0])).IsSuccess);
        Assert.True((await host.ClinicCancelAsync(w, walkIn.Value.Id)).IsSuccess);
        var types = await db.OutboxMessages.AsNoTracking().Select(m => m.Type).OrderBy(t => t).ToListAsync(Ct);
        Assert.Equal([CancelledByClinicPatient, RescheduledPatient], types); // no reminders, no other patient types
        Assert.DoesNotContain(await db.OutboxMessages.Select(m => m.Payload).ToListAsync(Ct), p => p.Contains("901112233", StringComparison.Ordinal) || p.Contains("Ali", StringComparison.Ordinal));

        await host.DispatchAllAsync();
        var sms = host.Sms().For("+998901112233");
        Assert.Equal(2, sms.Count);
        Assert.All(await db.OutboxMessages.AsNoTracking().ToListAsync(Ct), m => Assert.Equal((OutboxStatus.Sent, "Sms"), (m.Status, m.Channel)));
        Assert.All(await db.DeliveryAttempts.AsNoTracking().ToListAsync(Ct), a => Assert.Equal("contact", a.RecipientKey));
    }

    [Fact]
    public async Task N4_staff_messages_go_to_linked_triage_staff_of_the_branch_and_never_contain_the_patient_phone_or_name()
    {
        await using var host = await NotificationKit.HostAsync(postgres);
        await using var db = host.CreateDbContext();
        var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 1);
        var now = DateTimeOffset.UtcNow;
        var otherBranch = Branch.Create(w.ClinicId, "Boshqa filial", "Chilonzor", "Test 2", null, null, null, "Asia/Tashkent", now);
        db.Branches.Add(otherBranch);
        var finance = User.Create("Moliya", now);
        var elsewhere = User.Create("Boshqa filial qabulxonasi", now);
        db.Users.AddRange(finance, elsewhere);
        await db.SaveChangesAsync(Ct);
        db.ClinicMemberships.Add(ClinicMembership.Create(w.ClinicId, finance.Id, StaffRole.FinanceManager, MembershipScope.Clinic, now));
        var scoped = ClinicMembership.Create(w.ClinicId, elsewhere.Id, StaffRole.Receptionist, MembershipScope.Branches, now);
        db.ClinicMemberships.Add(scoped);
        await db.SaveChangesAsync(Ct);
        db.BranchAccess.Add(BranchAccess.Create(w.ClinicId, scoped.Id, otherBranch.Id));
        await db.SaveChangesAsync(Ct);
        await host.LinkChatAsync(w.StaffUserId, 7001, "en");
        await host.LinkChatAsync(finance.Id, 7002);
        await host.LinkChatAsync(elsewhere.Id, 7003);

        var booked = (await host.BookAsync(w, w.Patients[0], w.At(10))).Value;
        Assert.True((await host.PatientCancelAsync(w.Patients[0], booked.Id)).IsSuccess);
        await host.OnlyKeepAsync(RequestedStaff, CancelledByPatientStaff);
        await host.DispatchAllAsync();

        var messages = host.Telegram().For(7001);
        Assert.Equal(2, messages.Count);
        Assert.Empty(host.Telegram().For(7002)); // FinanceManager has no appointment.triage
        Assert.Empty(host.Telegram().For(7003)); // Branches-scoped to another branch
        var number = booked.Id.ToString("N")[^8..].ToUpperInvariant();
        Assert.All(messages, m =>
        {
            Assert.Contains($"#{number}", m.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("901234567", m.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Test Bemor", m.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Bemor 0", m.Text, StringComparison.Ordinal);
        });
        Assert.Contains(messages, m => m.Text.StartsWith("New request", StringComparison.Ordinal)
            && m.Buttons![0][0].Url == $"https://example.test/clinic/{w.ClinicId}/requests");
        Assert.DoesNotContain(await db.OutboxMessages.Select(m => m.Payload).ToListAsync(Ct),
            p => p.Contains("901234567", StringComparison.Ordinal) || p.Contains("Test Bemor", StringComparison.Ordinal));
    }

    [Fact]
    public async Task N5_no_reminder_after_cancellation_and_the_dispatcher_rechecks_stale_reminders()
    {
        await using var host = await NotificationKit.HostAsync(postgres);
        await using var db = host.CreateDbContext();
        var w = await BookingWorld.CreateAsync(db, doctors: 2, patients: 2);
        await host.LinkChatAsync(w.Patients[0], 8001);
        await host.LinkChatAsync(w.Patients[1], 8002);

        // Cancelled by the clinic: Booking cancels the reminders; even forced back to Pending the dispatcher refuses them.
        var cancelled = await host.ConfirmedAppointmentAsync(w, w.Patients[0], w.At(10));
        Assert.True((await host.ClinicCancelAsync(w, cancelled)).IsSuccess);
        Assert.All(await db.OutboxMessages.AsNoTracking().Where(m => m.Type == ReminderPatient).ToListAsync(Ct), m => Assert.Equal(OutboxStatus.Cancelled, m.Status));
        await host.ExecAsync("UPDATE notify.outbox_messages SET status = 'Sent' WHERE type <> @t", ("t", ReminderPatient));
        await host.ExecAsync("UPDATE notify.outbox_messages SET status = 'Pending', next_attempt_at = clock_timestamp() - interval '1 second' WHERE type = @t", ("t", ReminderPatient));

        // Moved in time behind Booking's back (stale start): also refused.
        var moved = await host.ConfirmedAppointmentAsync(w, w.Patients[1], w.At(12));
        await host.ExecAsync("UPDATE notify.outbox_messages SET status = 'Sent' WHERE type <> @t AND status = 'Pending'", ("t", ReminderPatient));
        await BookingCalls.ShiftAsync(host.ConnectionString, moved, w.At(14));
        await host.MakeDueAsync();
        await host.DispatchAllAsync();

        var reminders = await db.OutboxMessages.AsNoTracking().Where(m => m.Type == ReminderPatient).ToListAsync(Ct);
        Assert.Equal(4, reminders.Count);
        Assert.All(reminders, r => Assert.Equal((OutboxStatus.Cancelled, OutboxDispatcher.ReminderStale), (r.Status, r.LastErrorCode)));
        Assert.Empty(host.Telegram().For(8001));
        Assert.Empty(host.Telegram().For(8002));
    }

    [Fact]
    public async Task A_due_reminder_for_a_confirmed_appointment_is_sent_by_telegram()
    {
        await using var host = await NotificationKit.HostAsync(postgres);
        await using var db = host.CreateDbContext();
        var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 1);
        await host.LinkChatAsync(w.Patients[0], 8101);
        var id = await host.ConfirmedAppointmentAsync(w, w.Patients[0], w.At(10));
        await host.OnlyKeepAsync(ReminderPatient);
        await host.MakeDueAsync();
        await host.DispatchAllAsync();

        var texts = host.Telegram().For(8101).Select(m => m.Text).ToList();
        Assert.Equal(2, texts.Count);
        Assert.Contains(texts, t => t.StartsWith("Eslatma: ertaga", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.StartsWith("Eslatma: qabulingizga 2 soat", StringComparison.Ordinal));
        Assert.All(await db.OutboxMessages.AsNoTracking().Where(m => m.Type == ReminderPatient && m.DedupKey.Contains(id.ToString("N"))).ToListAsync(Ct),
            r => Assert.Equal(OutboxStatus.Sent, r.Status));
    }

    [Fact]
    public async Task Blocked_bot_deactivates_the_chat_link_and_falls_back_to_sms_where_allowed()
    {
        await using var host = await NotificationKit.HostAsync(postgres);
        await using var db = host.CreateDbContext();
        var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 1);
        await host.LinkChatAsync(w.Patients[0], 9001);
        var id = await host.ConfirmedAppointmentAsync(w, w.Patients[0], w.At(10));
        await host.OnlyKeepAsync(RequestedPatient, ConfirmedPatient);
        host.Telegram().SendBehavior = _ => new TelegramError(TelegramFailureKind.ChatUnavailable, "telegram.blocked", "403", "Forbidden: bot was blocked by the user");
        await host.DispatchAllAsync();

        var link = await db.TelegramChatLinks.AsNoTracking().SingleAsync(l => l.UserId == w.Patients[0], Ct);
        Assert.False(link.IsActive);
        Assert.Equal("blocked", link.DeactivationReason);
        var confirmed = await host.MessageAsync(ConfirmedPatient, id);
        var requested = await host.MessageAsync(RequestedPatient, id);
        Assert.Equal(OutboxStatus.Sent, confirmed.Status);
        Assert.Equal(OutboxStatus.Skipped, requested.Status);
        Assert.Single(host.Sms().For(NotificationKit.ContactPhone));

        // Later messages skip Telegram entirely while the link is inactive.
        host.Telegram().SendBehavior = null;
        var calls = host.Telegram().SendCalls;
        Assert.True((await host.ClinicCancelAsync(w, id)).IsSuccess);
        await host.OnlyKeepAsync(CancelledByClinicPatient);
        await host.DispatchAllAsync();
        Assert.Equal(calls, host.Telegram().SendCalls);
        Assert.Equal(2, host.Sms().For(NotificationKit.ContactPhone).Count);
    }

    [Fact]
    public async Task Telegram_disabled_means_sms_for_od11_types_even_with_a_link()
    {
        await using var host = await NotificationKit.HostAsync(postgres, new Dictionary<string, string?> { ["Telegram:Enabled"] = "false" });
        await using var db = host.CreateDbContext();
        var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 1);
        await host.LinkChatAsync(w.Patients[0], 9101);
        var id = await host.ConfirmedAppointmentAsync(w, w.Patients[0], w.At(10));
        await host.OnlyKeepAsync(ConfirmedPatient, RequestedStaff);
        await host.DispatchAllAsync();

        Assert.Equal((OutboxStatus.Sent, "Sms"), Pair(await host.MessageAsync(ConfirmedPatient, id)));
        Assert.Equal(0, host.Telegram().SendCalls);
    }

    [Fact]
    public async Task Sms_failure_is_retried_and_never_touches_the_appointment()
    {
        await using var host = await NotificationKit.HostAsync(postgres);
        await using var db = host.CreateDbContext();
        var w = await BookingWorld.CreateAsync(db, doctors: 1, patients: 1);
        var id = await host.ConfirmedAppointmentAsync(w, w.Patients[0], w.At(10));
        await host.OnlyKeepAsync(ConfirmedPatient);
        host.Sms().FailSends = true;
        await host.DispatchAsync();
        var m = await host.MessageAsync(ConfirmedPatient, id);
        Assert.Equal((OutboxStatus.Pending, "sms.failed", 1), (m.Status, m.LastErrorCode, m.Attempts));
        host.Sms().FailSends = false;
        await host.MakeDueAsync(m.Id);
        await host.DispatchAsync();
        Assert.Equal(OutboxStatus.Sent, (await host.MessageAsync(ConfirmedPatient, id)).Status);
        Assert.Equal(AppointmentStatus.Confirmed, (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == id, Ct)).Status);
    }

    private static (OutboxStatus, string?) Pair(OutboxMessage m) => (m.Status, m.Channel);
}
