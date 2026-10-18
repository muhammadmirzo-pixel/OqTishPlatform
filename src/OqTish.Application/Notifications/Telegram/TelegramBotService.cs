using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Application.Identity.Handlers;
using OqTish.Application.Identity.Telegram;
using OqTish.Domain.Booking;
using OqTish.Domain.Identity;
using OqTish.Domain.Notifications;

namespace OqTish.Application.Notifications.Telegram;

/// <summary>What the bot did with an update (for logs and tests).</summary>
public enum BotOutcome
{
    Ignored = 0,
    RateLimited = 1,
    Linked = 2,
    LinkInvalid = 3,
    LinkConflict = 4,
    Welcome = 5,
    Reactivated = 6,
    Appointments = 7,
    Stopped = 8,
    NotLinked = 9,
    Help = 10,
}

/// <summary>
/// Handles one bot update (Worker long polling or the API webhook; docs/SERVICES.md §5.5). Only private chats with
/// a human sender and text are handled; everything else (groups, channels, bots, stickers) is ignored (OD-10).
/// Per-chat flood protection via <see cref="TelegramChatRateLimiter"/>. Language = the sender's
/// <c>language_code</c> (uz/ru/en, fallback uz). Commands:
/// <list type="bullet">
/// <item><c>/start &lt;token&gt;</c> — <see cref="TelegramLinkService.ConsumeTelegramLinkTokenAsync"/> (one-time,
/// 10 min): links the chat to the account, stores the language. Invalid/used/expired → explanation; Telegram id owned
/// by another account → refusal (no merge).</item>
/// <item><c>/start</c> — re-activates an existing chat link, or links the chat of a user who already signed in with
/// this Telegram account (Mini App / Login Widget: the Telegram id is a verified identity); otherwise a welcome text
/// with the "OqTish'ni ochish" Mini App button and linking instructions.</item>
/// <item><c>/appointments</c> — the linked patient's own upcoming Pending/Confirmed appointments (max 5).</item>
/// <item><c>/stop</c> — deactivates the chat link (no more messages); the Telegram sign-in identity stays.</item>
/// <item><c>/help</c> — command list.</item>
/// </list>
/// Replies are best effort (a failed reply is logged by code only). Never logs message text, chat ids or tokens.
/// </summary>
public sealed partial class TelegramBotService(
    IOqTishDb db,
    ITransactionRunner transactions,
    IRowLocks locks,
    IAuditLog audit,
    TelegramLinkService links,
    ITelegramBotClient telegram,
    TelegramChatRateLimiter limiter,
    IDbClock clock,
    IOptions<TelegramOptions> options,
    ILogger<TelegramBotService> logger)
{
    private const int MaxListed = 5;

    public async Task<BotOutcome> HandleAsync(TelegramUpdate update, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(update);
        var message = update.Message;
        if (message is null || !string.Equals(message.ChatType, "private", StringComparison.Ordinal) || message.FromIsBot
            || message.FromId is not { } fromId || string.IsNullOrWhiteSpace(message.Text))
        {
            return BotOutcome.Ignored;
        }

        if (!limiter.TryAcquire(message.ChatId))
        {
            return BotOutcome.RateLimited;
        }

        var lang = TelegramChatLink.NormalizeLanguage(message.LanguageCode);
        var (command, argument) = ParseCommand(message.Text);
        var outcome = command switch
        {
            "/start" when argument is not null => await StartWithTokenAsync(argument, fromId, message.ChatId, lang, ct),
            "/start" => await StartAsync(fromId, message.ChatId, lang, ct),
            "/appointments" => await AppointmentsAsync(fromId, message.ChatId, lang, ct),
            "/stop" => await StopAsync(fromId, message.ChatId, lang, ct),
            _ => await ReplyAsync(message.ChatId, BotTexts.Help(lang), null, BotOutcome.Help, ct),
        };
        LogHandled(logger, command ?? "(text)", outcome);
        return outcome;
    }

    /// <summary><c>/start@MyBot payload</c> → (<c>/start</c>, <c>payload</c>); plain text → (null, null).</summary>
    public static (string? Command, string? Argument) ParseCommand(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();
        if (!trimmed.StartsWith('/'))
        {
            return (null, null);
        }

        var space = trimmed.IndexOfAny([' ', '\n', '\t']);
        var head = space < 0 ? trimmed : trimmed[..space];
        var rest = space < 0 ? null : trimmed[(space + 1)..].Trim();
        var at = head.IndexOf('@', StringComparison.Ordinal);
        if (at > 0)
        {
            head = head[..at];
        }

        return (head.ToLowerInvariant(), string.IsNullOrEmpty(rest) ? null : rest);
    }

    private async Task<BotOutcome> StartWithTokenAsync(string token, long fromId, long chatId, string lang, CancellationToken ct)
    {
        var result = await links.ConsumeTelegramLinkTokenAsync(token, fromId, chatId, ct);
        if (result.IsSuccess)
        {
            var userId = result.Value.UserId;
            await db.TelegramChatLinks.Where(l => l.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(l => l.LanguageCode, lang), ct);
            return await ReplyAsync(chatId, BotTexts.Linked(lang), MiniAppButton(lang), BotOutcome.Linked, ct);
        }

        return result.Error.Code == "auth.identity_in_use"
            ? await ReplyAsync(chatId, BotTexts.LinkConflict(lang), null, BotOutcome.LinkConflict, ct)
            : await ReplyAsync(chatId, BotTexts.LinkInvalid(lang), MiniAppButton(lang), BotOutcome.LinkInvalid, ct);
    }

    private async Task<BotOutcome> StartAsync(long fromId, long chatId, string lang, CancellationToken ct)
    {
        var telegramId = fromId.ToString(CultureInfo.InvariantCulture);
        var outcome = await transactions.RunAsync<BotOutcome>(async t =>
        {
            await locks.AdvisoryLockAsync("identity:telegram:" + telegramId, t);
            var now = await clock.NowAsync(t);
            var link = await db.TelegramChatLinks.FirstOrDefaultAsync(l => l.TelegramUserId == fromId, t);
            if (link is not null)
            {
                var wasActive = link.IsActive && link.ChatId == chatId;
                link.Relink(fromId, chatId, now);
                link.SetLanguage(lang);
                if (!wasActive)
                {
                    audit.Record(new AuditEvent("notify.telegram_chat_activated", TargetType: "user", TargetId: link.UserId.ToString(), ActorUserIdOverride: link.UserId));
                }

                await db.SaveChangesAsync(t);
                return BotOutcome.Reactivated;
            }

            // A user who signed in with this Telegram account (Mini App / Login Widget) already owns it as a verified identity.
            var owner = await (
                    from i in db.AuthIdentities
                    join u in db.Users on i.UserId equals u.Id
                    where i.Kind == AuthIdentityKind.Telegram && i.NormalizedIdentifier == telegramId && u.Status == UserStatus.Active
                    select i.UserId)
                .FirstOrDefaultAsync(t);
            if (owner == Guid.Empty)
            {
                return BotOutcome.Welcome;
            }

            var existing = await db.TelegramChatLinks.FirstOrDefaultAsync(l => l.UserId == owner, t);
            if (existing is null)
            {
                db.TelegramChatLinks.Add(TelegramChatLink.Create(owner, fromId, chatId, now, lang));
            }
            else
            {
                existing.Relink(fromId, chatId, now);
                existing.SetLanguage(lang);
            }

            audit.Record(new AuditEvent("notify.telegram_chat_linked", TargetType: "user", TargetId: owner.ToString(), ActorUserIdOverride: owner));
            await db.SaveChangesAsync(t);
            return BotOutcome.Linked;
        }, ct);

        if (!outcome.IsSuccess)
        {
            return BotOutcome.Ignored;
        }

        return outcome.Value switch
        {
            BotOutcome.Reactivated => await ReplyAsync(chatId, BotTexts.Reactivated(lang), MiniAppButton(lang), BotOutcome.Reactivated, ct),
            BotOutcome.Linked => await ReplyAsync(chatId, BotTexts.Linked(lang), MiniAppButton(lang), BotOutcome.Linked, ct),
            _ => await ReplyAsync(chatId, BotTexts.Welcome(lang), MiniAppButton(lang), BotOutcome.Welcome, ct),
        };
    }

    private async Task<BotOutcome> AppointmentsAsync(long fromId, long chatId, string lang, CancellationToken ct)
    {
        var userId = await db.TelegramChatLinks.AsNoTracking().Where(l => l.TelegramUserId == fromId && l.IsActive).Select(l => (Guid?)l.UserId).FirstOrDefaultAsync(ct);
        if (userId is null)
        {
            return await ReplyAsync(chatId, BotTexts.NotLinked(lang), MiniAppButton(lang), BotOutcome.NotLinked, ct);
        }

        var now = await clock.NowAsync(ct);
        var rows = await (
                from a in db.Appointments.AsNoTracking()
                join c in db.Clinics.AsNoTracking() on a.ClinicId equals c.Id
                join b in db.Branches.AsNoTracking() on a.BranchId equals b.Id
                join s in db.Services.AsNoTracking() on a.ServiceId equals s.Id
                where a.PatientUserId == userId && a.StartsAt > now
                    && (a.Status == AppointmentStatus.Confirmed || (a.Status == AppointmentStatus.Pending && a.ExpiresAt > now))
                orderby a.StartsAt
                select new { a.StartsAt, a.Status, ClinicName = c.Name, BranchName = b.Name, b.TimeZone, ServiceName = s.Name })
            .Take(MaxListed)
            .ToListAsync(ct);
        if (rows.Count == 0)
        {
            return await ReplyAsync(chatId, BotTexts.NoAppointments(lang), MiniAppButton(lang), BotOutcome.Appointments, ct);
        }

        var text = new StringBuilder(BotTexts.AppointmentsHeader(lang));
        foreach (var r in rows)
        {
            text.Append('\n').Append('\n')
                .Append(NotificationTemplates.Format(r.StartsAt, r.TimeZone)).Append(" — ").Append(BotTexts.Status(r.Status, lang)).Append('\n')
                .Append(r.ServiceName).Append('\n')
                .Append(r.ClinicName).Append(", ").Append(r.BranchName);
        }

        return await ReplyAsync(chatId, text.ToString(), MiniAppButton(lang, "/appointments"), BotOutcome.Appointments, ct);
    }

    private async Task<BotOutcome> StopAsync(long fromId, long chatId, string lang, CancellationToken ct)
    {
        var outcome = await transactions.RunAsync<bool>(async t =>
        {
            var now = await clock.NowAsync(t);
            var link = await db.TelegramChatLinks.FirstOrDefaultAsync(l => l.TelegramUserId == fromId && l.IsActive, t);
            if (link is null)
            {
                return false;
            }

            link.Deactivate("stopped", now);
            audit.Record(new AuditEvent("notify.telegram_stopped", TargetType: "user", TargetId: link.UserId.ToString(), ActorUserIdOverride: link.UserId));
            await db.SaveChangesAsync(t);
            return true;
        }, ct);

        return outcome is { IsSuccess: true, Value: true }
            ? await ReplyAsync(chatId, BotTexts.Stopped(lang), null, BotOutcome.Stopped, ct)
            : await ReplyAsync(chatId, BotTexts.NotLinked(lang), MiniAppButton(lang), BotOutcome.NotLinked, ct);
    }

    private IReadOnlyList<IReadOnlyList<TelegramButton>>? MiniAppButton(string lang, string path = "")
    {
        var url = OutboxDispatcher.PublicHttpsUrl(options.Value.MiniAppUrl);
        return url is null ? null : [[new TelegramButton(BotTexts.OpenApp(lang), WebAppUrl: url + path)]];
    }

    private async Task<BotOutcome> ReplyAsync(long chatId, string text, IReadOnlyList<IReadOnlyList<TelegramButton>>? buttons, BotOutcome outcome, CancellationToken ct)
    {
        var sent = await telegram.SendMessageAsync(new TelegramOutgoingMessage(chatId, text, buttons), ct);
        if (!sent.IsSuccess)
        {
            LogReplyFailed(logger, sent.Error.Code);
        }

        return outcome;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Telegram bot handled {Command}: {Outcome}")]
    private static partial void LogHandled(ILogger logger, string command, BotOutcome outcome);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram bot reply failed ({Code})")]
    private static partial void LogReplyFailed(ILogger logger, string code);
}

/// <summary>Bot reply texts (uz Latin / ru / en).</summary>
public static class BotTexts
{
    public static string OpenApp(string lang) => lang switch
    {
        "ru" => "Открыть OqTish",
        "en" => "Open OqTish",
        _ => "OqTish'ni ochish",
    };

    public static string Welcome(string lang) => lang switch
    {
        "ru" => "Здравствуйте! Это бот OqTish — онлайн-запись в стоматологические клиники.\n\n"
            + "Чтобы получать уведомления, откройте на сайте «Профиль» → «Привязать Telegram» и перейдите по ссылке.\n\n" + Commands(lang),
        "en" => "Hello! This is the OqTish bot — online booking at dental clinics.\n\n"
            + "To receive notifications, open \"Profile\" → \"Link Telegram\" on the website and follow the link.\n\n" + Commands(lang),
        _ => "Assalomu alaykum! Bu OqTish boti — stomatologiya klinikalariga onlayn yozilish.\n\n"
            + "Xabarnomalarni olish uchun saytda \"Profil\" → \"Telegramni bog'lash\" tugmasini bosing va havolani oching.\n\n" + Commands(lang),
    };

    public static string Linked(string lang) => lang switch
    {
        "ru" => "Готово! Telegram привязан к вашему аккаунту OqTish. Уведомления о записях будут приходить сюда.\n\n" + Commands(lang),
        "en" => "Done! Telegram is linked to your OqTish account. Appointment notifications will arrive here.\n\n" + Commands(lang),
        _ => "Tayyor! Telegram OqTish akkauntingizga bog'landi. Qabul haqidagi xabarlar shu yerga keladi.\n\n" + Commands(lang),
    };

    public static string Reactivated(string lang) => lang switch
    {
        "ru" => "Уведомления снова включены.\n\n" + Commands(lang),
        "en" => "Notifications are on again.\n\n" + Commands(lang),
        _ => "Xabarnomalar yana yoqildi.\n\n" + Commands(lang),
    };

    public static string LinkInvalid(string lang) => lang switch
    {
        "ru" => "Ссылка недействительна, уже использована или устарела (она действует 10 минут). Получите новую в «Профиле» на сайте.",
        "en" => "This link is invalid, already used or expired (it is valid for 10 minutes). Get a new one under \"Profile\" on the website.",
        _ => "Havola yaroqsiz, ishlatilgan yoki muddati o'tgan (10 daqiqa amal qiladi). Saytdagi \"Profil\" bo'limidan yangisini oling.",
    };

    public static string LinkConflict(string lang) => lang switch
    {
        "ru" => "Этот Telegram уже привязан к другому аккаунту OqTish. Войдите в тот аккаунт или обратитесь в поддержку.",
        "en" => "This Telegram account is already linked to another OqTish account. Sign in to that account or contact support.",
        _ => "Bu Telegram boshqa OqTish akkauntiga bog'langan. O'sha akkauntga kiring yoki qo'llab-quvvatlash xizmatiga murojaat qiling.",
    };

    public static string NotLinked(string lang) => lang switch
    {
        "ru" => "Этот чат ещё не привязан к аккаунту OqTish. Откройте на сайте «Профиль» → «Привязать Telegram».",
        "en" => "This chat is not linked to an OqTish account yet. Open \"Profile\" → \"Link Telegram\" on the website.",
        _ => "Bu chat hali OqTish akkauntiga bog'lanmagan. Saytda \"Profil\" → \"Telegramni bog'lash\" ni oching.",
    };

    public static string Stopped(string lang) => lang switch
    {
        "ru" => "Уведомления в Telegram отключены. Важные сообщения могут приходить по SMS. Чтобы включить снова, отправьте /start.",
        "en" => "Telegram notifications are off. Important messages may still arrive by SMS. Send /start to turn them on again.",
        _ => "Telegram xabarnomalari o'chirildi. Muhim xabarlar SMS orqali kelishi mumkin. Qayta yoqish uchun /start yuboring.",
    };

    public static string Help(string lang) => Commands(lang);

    public static string AppointmentsHeader(string lang) => lang switch
    {
        "ru" => "Ваши ближайшие записи:",
        "en" => "Your upcoming appointments:",
        _ => "Yaqin qabullaringiz:",
    };

    public static string NoAppointments(string lang) => lang switch
    {
        "ru" => "У вас нет предстоящих записей.",
        "en" => "You have no upcoming appointments.",
        _ => "Sizda yaqin qabullar yo'q.",
    };

    public static string Status(AppointmentStatus status, string lang) => (status, lang) switch
    {
        (AppointmentStatus.Confirmed, "ru") => "подтверждена",
        (AppointmentStatus.Confirmed, "en") => "confirmed",
        (AppointmentStatus.Confirmed, _) => "tasdiqlangan",
        (_, "ru") => "ожидает подтверждения",
        (_, "en") => "awaiting confirmation",
        _ => "tasdiqlash kutilmoqda",
    };

    private static string Commands(string lang) => lang switch
    {
        "ru" => "Команды:\n/appointments — мои ближайшие записи\n/stop — отключить уведомления\n/help — помощь",
        "en" => "Commands:\n/appointments — my upcoming appointments\n/stop — turn notifications off\n/help — help",
        _ => "Buyruqlar:\n/appointments — yaqin qabullarim\n/stop — xabarnomalarni o'chirish\n/help — yordam",
    };
}
