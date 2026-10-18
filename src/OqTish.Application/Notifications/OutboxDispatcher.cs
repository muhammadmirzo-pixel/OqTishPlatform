using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Application.Common.Json;
using OqTish.Application.Identity.Handlers;
using OqTish.Application.Identity.Sms;
using OqTish.Application.Identity.Telegram;
using OqTish.Application.Notifications.Telegram;
using OqTish.Application.Staff;
using OqTish.Domain.Booking;
using OqTish.Domain.Identity;
using OqTish.Domain.Notifications;
using OqTish.Domain.Staff;
using OqTish.Domain.Tenancy;

namespace OqTish.Application.Notifications;

/// <summary>
/// Delivers outbox messages (Worker, every <c>Notifications:DispatchIntervalSeconds</c>; docs/SERVICES.md §5.1).
/// <list type="number">
/// <item>Claims a batch (<see cref="IOutboxClaimer"/>: SKIP LOCKED, lease, attempts + 1); a crashed run's rows are
/// reclaimed after the lease.</item>
/// <item>Per message: resolves the recipients — the patient account (or, for walk-ins, the appointment's contact
/// phone) or the branch staff holding <c>appointment.triage</c> — and the channel per recipient: Telegram when the
/// integration is enabled and an active chat link exists; otherwise SMS only for the OD-11 types
/// (<see cref="BookingNotificationTypes.SmsFallback"/>); otherwise Skipped (<c>notify.no_channel</c>). A Bot API 403 /
/// "chat not found" deactivates the chat link (and falls back to SMS where allowed).</item>
/// <item>Reminders are re-checked first: the appointment must still be Confirmed with the same start time and the
/// start must be in the future, otherwise the message is Cancelled (<c>reminder.stale</c> / <c>reminder.too_late</c>).</item>
/// <item>Every recipient try is recorded in <c>notify.delivery_attempts</c> right after the provider call; a retry skips
/// recipients that already have a final outcome. Transient failures retry with <see cref="RetryPolicy"/> backoff;
/// after <c>MaxAttempts</c> the message is Failed.</item>
/// </list>
/// Delivery is <b>at-least-once</b>: a crash between a provider call and its attempt row can repeat that one send
/// (bounded by the dedup key: one message per business event). A delivery failure never touches business data:
/// the dispatcher only reads appointments and only writes notify.* rows (plus deactivating chat links).
/// </summary>
public sealed partial class OutboxDispatcher(
    IOqTishDb db,
    IOutboxClaimer claimer,
    IDbClock clock,
    ITelegramBotClient telegram,
    ISmsSender sms,
    IOptions<NotificationOptions> options,
    IOptions<TelegramOptions> telegramOptions,
    ILogger<OutboxDispatcher> logger)
{
    public const string NoChannel = "notify.no_channel";
    public const string NoRecipient = "notify.no_recipient";
    public const string ReminderStale = "reminder.stale";
    public const string ReminderTooLate = "reminder.too_late";

    private readonly NotificationOptions _options = options.Value;

    /// <summary>Claims and processes one batch; returns the number of claimed messages.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var claimed = await claimer.ClaimDueAsync(Math.Clamp(_options.BatchSize, 1, 500), TimeSpan.FromSeconds(Math.Max(30, _options.LeaseSeconds)), ct);
        foreach (var message in claimed)
        {
            try
            {
                await ProcessAsync(message, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Bug or infrastructure failure (DB): only the exception type is logged; the row is retried with backoff.
                LogUnexpected(logger, message.Id, ex.GetType().Name);
                try
                {
                    var now = await clock.NowAsync(ct);
                    await FinishAsync(message, Retry(message, now, "dispatch.exception", ex.GetType().Name, null, null), ct);
                }
                catch (Exception inner) when (inner is not OperationCanceledException)
                {
                    LogUnexpected(logger, message.Id, inner.GetType().Name); // the lease expires and the row is reclaimed
                }
            }
        }

        return claimed.Count;
    }

    private async Task ProcessAsync(ClaimedOutboxMessage claimed, CancellationToken ct)
    {
        var message = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.Id == claimed.Id && m.ClaimToken == claimed.ClaimToken && m.Status == OutboxStatus.Processing)
            .Select(m => new { m.Type, m.Payload })
            .FirstOrDefaultAsync(ct);
        if (message is null)
        {
            return;
        }

        var now = await clock.NowAsync(ct);
        if (claimed.Attempt > Math.Max(1, _options.MaxAttempts))
        {
            await FinishAsync(claimed, Final(OutboxStatus.Failed, "dispatch.max_attempts", "Attempt budget exhausted (lease expired repeatedly).", null), ct);
            return;
        }

        var plan = await PlanAsync(message.Type, message.Payload, now, ct);
        if (plan.Final is { } final)
        {
            await FinishAsync(claimed, final, ct);
            return;
        }

        var prior = await db.DeliveryAttempts.AsNoTracking()
            .Where(a => a.OutboxMessageId == claimed.Id && a.Outcome != DeliveryOutcome.Retry)
            .Select(a => new { a.RecipientKey, a.Outcome })
            .ToListAsync(ct);
        var done = prior.Select(p => p.RecipientKey).ToHashSet(StringComparer.Ordinal);

        var results = new List<RecipientResult>();
        foreach (var recipient in plan.Recipients)
        {
            if (done.Contains(recipient.Key))
            {
                continue;
            }

            var result = await DeliverAsync(message.Type, plan, recipient, now, ct);
            results.Add(result);
            var attempt = DeliveryAttempt.Create(claimed.Id, claimed.Attempt, recipient.Key, result.Channel, result.Outcome,
                result.ProviderStatus, result.ErrorCode, result.Error, await clock.NowAsync(ct));
            db.DeliveryAttempts.Add(attempt);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch
            {
                db.DeliveryAttempts.Remove(attempt); // detach, so the next message of the batch does not re-insert it
                throw;
            }
        }

        var channels = string.Join(',', results.Where(r => r.Channel != DeliveryChannel.None).Select(r => r.Channel.ToString()).Distinct());
        var channel = channels.Length == 0 ? null : channels;
        var anySent = results.Any(r => r.Outcome == DeliveryOutcome.Sent) || prior.Any(p => p.Outcome == DeliveryOutcome.Sent);
        var retry = results.Where(r => r.Outcome == DeliveryOutcome.Retry).ToList();
        var rejected = results.FirstOrDefault(r => r.Outcome == DeliveryOutcome.Rejected);
        Completion completion;
        if (plan.Recipients.Count == 0)
        {
            completion = Final(OutboxStatus.Skipped, NoRecipient, null, null);
        }
        else if (retry.Count > 0)
        {
            var last = retry[^1];
            var retryAfter = retry.Max(r => r.RetryAfter);
            completion = Retry(claimed, now, last.ErrorCode ?? "notify.retry", last.Error, channel, retryAfter);
        }
        else if (anySent)
        {
            completion = new Completion(OutboxStatus.Sent, now, null, null, channel, now);
        }
        else if (rejected is not null || prior.Any(p => p.Outcome == DeliveryOutcome.Rejected))
        {
            completion = Final(OutboxStatus.Failed, rejected?.ErrorCode ?? "notify.rejected", rejected?.Error, channel);
        }
        else
        {
            completion = Final(OutboxStatus.Skipped, results.FirstOrDefault(r => r.Outcome == DeliveryOutcome.Skipped)?.ErrorCode ?? NoChannel, null, channel);
        }

        await FinishAsync(claimed, completion, ct);
    }

    private async Task<Plan> PlanAsync(string type, string payload, DateTimeOffset now, CancellationToken ct)
    {
        if (!BookingNotificationTypes.All.Contains(type, StringComparer.Ordinal))
        {
            return Plan.Done(Final(OutboxStatus.Failed, "dispatch.unknown_type", null, null));
        }

        Guid appointmentId;
        Guid? patientUserId;
        Guid clinicId;
        Guid branchId;
        NoticeModel model;
        try
        {
            if (type == BookingNotificationTypes.ReminderPatient)
            {
                var r = JsonSerializer.Deserialize<AppointmentReminder>(payload, JsonDefaults.Options)!;
                appointmentId = r.AppointmentId;
                patientUserId = r.PatientUserId;
                clinicId = r.ClinicId;
                branchId = r.BranchId;
                model = new NoticeModel(r.ClinicName, r.BranchName, null, r.ServiceName, r.DoctorName, r.TimeZone, r.StartsAt, null, null, null,
                    Booking.BookingNotifier.RequestNumber(r.AppointmentId), r.Kind);
            }
            else
            {
                var n = JsonSerializer.Deserialize<AppointmentNotice>(payload, JsonDefaults.Options)!;
                appointmentId = n.AppointmentId;
                patientUserId = n.PatientUserId;
                clinicId = n.ClinicId;
                branchId = n.BranchId;
                model = new NoticeModel(n.ClinicName, n.BranchName, null, n.ServiceName, n.DoctorName, n.TimeZone, n.StartsAt, n.PreviousStartsAt, n.ExpiresAt,
                    n.ReasonCode, n.RequestNumber, null);
            }
        }
        catch (JsonException)
        {
            return Plan.Done(Final(OutboxStatus.Failed, "dispatch.bad_payload", null, null));
        }

        var appointment = await db.Appointments.AsNoTracking()
            .Where(a => a.Id == appointmentId)
            .Select(a => new { a.Status, a.StartsAt, a.ContactPhone })
            .FirstOrDefaultAsync(ct);
        if (appointment is null)
        {
            return Plan.Done(Final(OutboxStatus.Cancelled, "notify.appointment_missing", null, null));
        }

        if (type == BookingNotificationTypes.ReminderPatient)
        {
            // BE2-11 contract: never remind about an appointment that left Confirmed or moved.
            if (appointment.Status != AppointmentStatus.Confirmed || appointment.StartsAt != model.StartsAt)
            {
                return Plan.Done(Final(OutboxStatus.Cancelled, ReminderStale, null, null));
            }

            if (appointment.StartsAt <= now)
            {
                return Plan.Done(Final(OutboxStatus.Cancelled, ReminderTooLate, null, null));
            }
        }

        var address = await db.Branches.AsNoTracking().Where(b => b.Id == branchId).Select(b => b.Address).FirstOrDefaultAsync(ct);
        model = model with { Address = address };
        var telegramEnabled = TelegramConfigQuery.IsEnabled(telegramOptions.Value);
        var recipients = new List<Recipient>();
        if (BookingNotificationTypes.IsStaff(type))
        {
            var triageRoles = Permissions.RoleMap.Where(kv => kv.Value.ContainsKey(Permissions.AppointmentTriage)).Select(kv => kv.Key).ToList();
            var staff = await (
                    from m in db.ClinicMemberships.AsNoTracking()
                    join u in db.Users.AsNoTracking() on m.UserId equals u.Id
                    join c in db.Clinics.AsNoTracking() on m.ClinicId equals c.Id
                    join l in db.TelegramChatLinks.AsNoTracking() on m.UserId equals l.UserId
                    where m.ClinicId == clinicId
                        && m.Status == MembershipStatus.Active
                        && u.Status == UserStatus.Active
                        && c.VerificationStatus != ClinicVerificationStatus.Suspended
                        && triageRoles.Contains(m.Role)
                        && l.IsActive
                        && (m.Scope == MembershipScope.Clinic || db.BranchAccess.Any(a => a.MembershipId == m.Id && a.BranchId == branchId))
                    orderby m.UserId
                    select new { m.UserId, l.ChatId, l.LanguageCode })
                .ToListAsync(ct);
            recipients.AddRange(staff.DistinctBy(s => s.UserId).Select(s => new Recipient($"user:{s.UserId:N}", s.UserId, new Chat(s.ChatId, s.LanguageCode), null, false)));
        }
        else
        {
            var smsAllowed = BookingNotificationTypes.SmsFallback.Contains(type);
            if (patientUserId is { } userId)
            {
                var link = await db.TelegramChatLinks.AsNoTracking()
                    .Where(l => l.UserId == userId)
                    .Select(l => new { l.ChatId, l.LanguageCode, l.IsActive })
                    .FirstOrDefaultAsync(ct);
                recipients.Add(new Recipient($"user:{userId:N}", userId, link is { IsActive: true } ? new Chat(link.ChatId, link.LanguageCode) : null,
                    appointment.ContactPhone, smsAllowed, link?.LanguageCode));
            }
            else if (smsAllowed)
            {
                recipients.Add(new Recipient("contact", null, null, appointment.ContactPhone, true));
            }
        }

        return new Plan(null, recipients, model, appointmentId, clinicId, telegramEnabled);
    }

    private async Task<RecipientResult> DeliverAsync(string type, Plan plan, Recipient recipient, DateTimeOffset now, CancellationToken ct)
    {
        string? telegramProblem = null;
        if (plan.TelegramEnabled && recipient.Chat is { } chat)
        {
            var text = NotificationTemplates.Telegram(type, chat.Language, plan.Model);
            if (text is null)
            {
                return new RecipientResult(DeliveryChannel.Telegram, DeliveryOutcome.Rejected, null, "template.missing", null, null);
            }

            var sent = await telegram.SendMessageAsync(new TelegramOutgoingMessage(chat.ChatId, text, Buttons(type, chat.Language, plan)), ct);
            if (sent.IsSuccess)
            {
                return new RecipientResult(DeliveryChannel.Telegram, DeliveryOutcome.Sent, "200", null, null, null);
            }

            var error = sent.Error;
            telegramProblem = error.Code;
            if (error.Kind == TelegramFailureKind.ChatUnavailable && recipient.UserId is { } userId)
            {
                await db.TelegramChatLinks
                    .Where(l => l.UserId == userId && l.ChatId == chat.ChatId && l.IsActive)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(l => l.IsActive, false)
                        .SetProperty(l => l.DeactivatedAt, now)
                        .SetProperty(l => l.DeactivationReason, "blocked"), ct);
                LogChatDeactivated(logger, userId);
            }

            var canFallBack = recipient.SmsAllowed && recipient.Phone is not null;
            if (!canFallBack)
            {
                return error.Kind switch
                {
                    TelegramFailureKind.Transient or TelegramFailureKind.RateLimited =>
                        new RecipientResult(DeliveryChannel.Telegram, DeliveryOutcome.Retry, error.ProviderStatus, error.Code, error.Description, error.RetryAfter),
                    TelegramFailureKind.ChatUnavailable =>
                        new RecipientResult(DeliveryChannel.Telegram, DeliveryOutcome.Skipped, error.ProviderStatus, error.Code, error.Description, null),
                    _ => new RecipientResult(DeliveryChannel.Telegram, DeliveryOutcome.Rejected, error.ProviderStatus, error.Code, error.Description, null),
                };
            }
        }

        if (recipient.SmsAllowed && recipient.Phone is { } phone)
        {
            var text = NotificationTemplates.Sms(type, recipient.Chat?.Language ?? recipient.FallbackLanguage, plan.Model);
            if (text is null)
            {
                return new RecipientResult(DeliveryChannel.Sms, DeliveryOutcome.Rejected, null, "template.missing", null, null);
            }

            try
            {
                await sms.SendAsync(phone, text, ct);
                return new RecipientResult(DeliveryChannel.Sms, DeliveryOutcome.Sent, "ok", telegramProblem, telegramProblem is null ? null : "Telegram failed; delivered by SMS.", null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // The exception text of an SMS adapter may echo request data; only its type is kept.
                return new RecipientResult(DeliveryChannel.Sms, DeliveryOutcome.Retry, "error", ex is SmsDeliveryException ? "sms.failed" : "sms.error", ex.GetType().Name, null);
            }
        }

        return new RecipientResult(DeliveryChannel.None, DeliveryOutcome.Skipped, null, telegramProblem ?? NoChannel, null, null);
    }

    private IReadOnlyList<IReadOnlyList<TelegramButton>>? Buttons(string type, string language, Plan plan)
    {
        if (BookingNotificationTypes.IsStaff(type))
        {
            var console = PublicHttpsUrl(_options.PublicBaseUrl);
            if (console is null)
            {
                return null;
            }

            var path = type == BookingNotificationTypes.RequestedStaff
                ? $"/clinic/{plan.ClinicId}/requests"
                : $"/clinic/{plan.ClinicId}/appointments/{plan.AppointmentId}";
            return [[new TelegramButton(NotificationTemplates.OpenConsoleButton(language), Url: console + path)]];
        }

        var miniApp = PublicHttpsUrl(telegramOptions.Value.MiniAppUrl);
        return miniApp is null
            ? null
            : [[new TelegramButton(NotificationTemplates.OpenAppointmentsButton(language), WebAppUrl: $"{miniApp}/appointments/{plan.AppointmentId}")]];
    }

    /// <summary>Telegram rejects buttons with non-HTTPS or local URLs (400), which would fail the whole message: such URLs give no button.</summary>
    internal static string? PublicHttpsUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private Completion Retry(ClaimedOutboxMessage claimed, DateTimeOffset now, string code, string? error, string? channel, TimeSpan? retryAfter)
    {
        if (RetryPolicy.IsFinal(claimed.Attempt, _options.MaxAttempts))
        {
            return Final(OutboxStatus.Failed, code, error, channel);
        }

        var delay = RetryPolicy.NextDelay(claimed.Attempt, _options.BackoffSchedule(), _options.JitterRatio, (decimal)Random.Shared.Next(0, 10_001) / 10_000m, retryAfter);
        return new Completion(OutboxStatus.Pending, now + delay, code, error, channel, null);
    }

    private static Completion Final(OutboxStatus status, string? code, string? error, string? channel) => new(status, null, code, error, channel, null);

    private async Task FinishAsync(ClaimedOutboxMessage claimed, Completion c, CancellationToken ct)
    {
        var error = c.Error is { Length: > 500 } e ? e[..500] : c.Error;
        var code = c.Code is { Length: > 64 } k ? k[..64] : c.Code;
        var updated = c.NextAttemptAt is { } next
            ? await db.OutboxMessages
                .Where(m => m.Id == claimed.Id && m.ClaimToken == claimed.ClaimToken && m.Status == OutboxStatus.Processing)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Status, c.Status)
                    .SetProperty(m => m.NextAttemptAt, next)
                    .SetProperty(m => m.LastErrorCode, code)
                    .SetProperty(m => m.LastError, error)
                    .SetProperty(m => m.Channel, c.Channel)
                    .SetProperty(m => m.SentAt, c.SentAt)
                    .SetProperty(m => m.ClaimToken, (Guid?)null)
                    .SetProperty(m => m.LockedUntil, (DateTimeOffset?)null), ct)
            : await db.OutboxMessages
                .Where(m => m.Id == claimed.Id && m.ClaimToken == claimed.ClaimToken && m.Status == OutboxStatus.Processing)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Status, c.Status)
                    .SetProperty(m => m.LastErrorCode, code)
                    .SetProperty(m => m.LastError, error)
                    .SetProperty(m => m.Channel, c.Channel)
                    .SetProperty(m => m.SentAt, c.SentAt)
                    .SetProperty(m => m.ClaimToken, (Guid?)null)
                    .SetProperty(m => m.LockedUntil, (DateTimeOffset?)null), ct);
        if (updated == 0)
        {
            LogLostClaim(logger, claimed.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} failed unexpectedly ({ExceptionType}); it will be retried")]
    private static partial void LogUnexpected(ILogger logger, Guid messageId, string exceptionType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Telegram chat link of user {UserId} deactivated (bot blocked or chat unavailable)")]
    private static partial void LogChatDeactivated(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId}: claim lost (lease expired and reclaimed); result not written")]
    private static partial void LogLostClaim(ILogger logger, Guid messageId);

    private sealed record Chat(long ChatId, string Language);

    private sealed record Recipient(string Key, Guid? UserId, Chat? Chat, string? Phone, bool SmsAllowed, string? FallbackLanguage = null);

    private sealed record RecipientResult(DeliveryChannel Channel, DeliveryOutcome Outcome, string? ProviderStatus, string? ErrorCode, string? Error, TimeSpan? RetryAfter);

    private sealed record Completion(OutboxStatus Status, DateTimeOffset? NextAttemptAt, string? Code, string? Error, string? Channel, DateTimeOffset? SentAt);

    private sealed record Plan(Completion? Final, IReadOnlyList<Recipient> Recipients, NoticeModel Model, Guid AppointmentId, Guid ClinicId, bool TelegramEnabled)
    {
        public static Plan Done(Completion final) => new(final, [], null!, Guid.Empty, Guid.Empty, false);
    }
}
