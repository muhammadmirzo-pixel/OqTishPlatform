using System.Globalization;
using OqTish.Application.Booking;

namespace OqTish.Application.Notifications;

/// <summary>Display data of one notification (already resolved; no patient phone or name for staff messages, OD-10).</summary>
public sealed record NoticeModel(
    string ClinicName,
    string BranchName,
    string? Address,
    string ServiceName,
    string? DoctorName,
    string TimeZone,
    DateTimeOffset StartsAt,
    DateTimeOffset? PreviousStartsAt,
    DateTimeOffset? ExpiresAt,
    string? ReasonCode,
    string RequestNumber,
    string? ReminderKind);

/// <summary>
/// Message templates (uz Latin / ru / en) for every outbox type (docs/SERVICES.md §5.4). Telegram texts are plain text;
/// SMS texts are short (≤ 160 Latin characters where the clinic name allows) and exist only for the OD-11 fallback
/// types. No medical data, no patient phone/name in staff texts. Times are shown in the branch time zone
/// (<c>dd.MM.yyyy HH:mm</c>).
/// </summary>
public static class NotificationTemplates
{
    public static readonly IReadOnlyList<string> Languages = ["uz", "ru", "en"];

    public static string Lang(string? language) => language is "ru" or "en" ? language : "uz";

    /// <summary>Telegram text for a type, or null when the type has no template.</summary>
    public static string? Telegram(string type, string? language, NoticeModel m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var l = Lang(language);
        var time = Format(m.StartsAt, m.TimeZone);
        var place = $"{m.ClinicName}, {m.BranchName}";
        var address = string.IsNullOrWhiteSpace(m.Address) ? null : m.Address;
        var doctor = string.IsNullOrWhiteSpace(m.DoctorName) ? null : m.DoctorName;
        var deadline = m.ExpiresAt is { } e ? Format(e, m.TimeZone) : null;
        var previous = m.PreviousStartsAt is { } p ? Format(p, m.TimeZone) : null;
        var reason = Reason(m.ReasonCode, l);

        string Lines(params string?[] lines) => string.Join('\n', lines.Where(x => x is not null));

        return (type, l) switch
        {
            (BookingNotificationTypes.RequestedPatient, "uz") => Lines("So'rovingiz qabul qilindi.", place, $"Xizmat: {m.ServiceName}", $"Vaqt: {time}",
                deadline is null ? null : $"Klinika {deadline} gacha tasdiqlashi kerak. Natijasini shu yerda xabar qilamiz."),
            (BookingNotificationTypes.RequestedPatient, "ru") => Lines("Ваша заявка принята.", place, $"Услуга: {m.ServiceName}", $"Время: {time}",
                deadline is null ? null : $"Клиника должна подтвердить её до {deadline}. Мы сообщим результат здесь."),
            (BookingNotificationTypes.RequestedPatient, _) => Lines("Your request has been received.", place, $"Service: {m.ServiceName}", $"Time: {time}",
                deadline is null ? null : $"The clinic has to confirm it by {deadline}. We will let you know here."),

            (BookingNotificationTypes.ConfirmedPatient, "uz") => Lines("Qabulingiz tasdiqlandi.", place, address is null ? null : $"Manzil: {address}",
                $"Xizmat: {m.ServiceName}", doctor is null ? null : $"Shifokor: {doctor}", $"Vaqt: {time}"),
            (BookingNotificationTypes.ConfirmedPatient, "ru") => Lines("Ваша запись подтверждена.", place, address is null ? null : $"Адрес: {address}",
                $"Услуга: {m.ServiceName}", doctor is null ? null : $"Врач: {doctor}", $"Время: {time}"),
            (BookingNotificationTypes.ConfirmedPatient, _) => Lines("Your appointment is confirmed.", place, address is null ? null : $"Address: {address}",
                $"Service: {m.ServiceName}", doctor is null ? null : $"Doctor: {doctor}", $"Time: {time}"),

            (BookingNotificationTypes.RejectedPatient, "uz") => Lines("Afsuski, so'rovingiz rad etildi.", place, $"Xizmat: {m.ServiceName}", $"Vaqt: {time}",
                $"Sabab: {reason}", "Boshqa vaqtni tanlab, qayta yozilishingiz mumkin."),
            (BookingNotificationTypes.RejectedPatient, "ru") => Lines("К сожалению, ваша заявка отклонена.", place, $"Услуга: {m.ServiceName}", $"Время: {time}",
                $"Причина: {reason}", "Вы можете выбрать другое время и записаться снова."),
            (BookingNotificationTypes.RejectedPatient, _) => Lines("Unfortunately, your request was declined.", place, $"Service: {m.ServiceName}", $"Time: {time}",
                $"Reason: {reason}", "You can choose another time and book again."),

            (BookingNotificationTypes.CancelledByClinicPatient, "uz") => Lines("Klinika qabulingizni bekor qildi.", place, $"Xizmat: {m.ServiceName}", $"Vaqt: {time}", $"Sabab: {reason}"),
            (BookingNotificationTypes.CancelledByClinicPatient, "ru") => Lines("Клиника отменила вашу запись.", place, $"Услуга: {m.ServiceName}", $"Время: {time}", $"Причина: {reason}"),
            (BookingNotificationTypes.CancelledByClinicPatient, _) => Lines("The clinic cancelled your appointment.", place, $"Service: {m.ServiceName}", $"Time: {time}", $"Reason: {reason}"),

            (BookingNotificationTypes.RescheduledPatient, "uz") => Lines("Qabulingiz vaqti o'zgartirildi.", place, address is null ? null : $"Manzil: {address}",
                $"Xizmat: {m.ServiceName}", $"Yangi vaqt: {time}", previous is null ? null : $"Avvalgi vaqt: {previous}", doctor is null ? null : $"Shifokor: {doctor}"),
            (BookingNotificationTypes.RescheduledPatient, "ru") => Lines("Время вашей записи изменено.", place, address is null ? null : $"Адрес: {address}",
                $"Услуга: {m.ServiceName}", $"Новое время: {time}", previous is null ? null : $"Прежнее время: {previous}", doctor is null ? null : $"Врач: {doctor}"),
            (BookingNotificationTypes.RescheduledPatient, _) => Lines("Your appointment was rescheduled.", place, address is null ? null : $"Address: {address}",
                $"Service: {m.ServiceName}", $"New time: {time}", previous is null ? null : $"Previous time: {previous}", doctor is null ? null : $"Doctor: {doctor}"),

            (BookingNotificationTypes.ExpiredPatient, "uz") => Lines("Klinika so'rovingizni o'z vaqtida tasdiqlamadi, so'rov bekor bo'ldi.", place,
                $"Xizmat: {m.ServiceName}", $"Vaqt: {time}", "Iltimos, boshqa vaqt yoki klinikani tanlang."),
            (BookingNotificationTypes.ExpiredPatient, "ru") => Lines("Клиника не подтвердила заявку вовремя, заявка отменена.", place,
                $"Услуга: {m.ServiceName}", $"Время: {time}", "Пожалуйста, выберите другое время или клинику."),
            (BookingNotificationTypes.ExpiredPatient, _) => Lines("The clinic did not confirm your request in time, so it expired.", place,
                $"Service: {m.ServiceName}", $"Time: {time}", "Please choose another time or clinic."),

            (BookingNotificationTypes.CompletedPatient, "uz") => Lines("Tashrifingiz uchun rahmat!", place, "Fikringizni \"Qabullarim\" bo'limida qoldirishingiz mumkin."),
            (BookingNotificationTypes.CompletedPatient, "ru") => Lines("Спасибо за визит!", place, "Вы можете оставить отзыв в разделе «Мои записи»."),
            (BookingNotificationTypes.CompletedPatient, _) => Lines("Thank you for your visit!", place, "You can leave a review under \"My appointments\"."),

            (BookingNotificationTypes.ReminderPatient, "uz") => Lines(m.ReminderKind == "2h" ? "Eslatma: qabulingizga 2 soat qoldi." : "Eslatma: ertaga qabulingiz bor.",
                place, address is null ? null : $"Manzil: {address}", $"Xizmat: {m.ServiceName}", $"Vaqt: {time}"),
            (BookingNotificationTypes.ReminderPatient, "ru") => Lines(m.ReminderKind == "2h" ? "Напоминание: до вашей записи 2 часа." : "Напоминание: завтра у вас запись.",
                place, address is null ? null : $"Адрес: {address}", $"Услуга: {m.ServiceName}", $"Время: {time}"),
            (BookingNotificationTypes.ReminderPatient, _) => Lines(m.ReminderKind == "2h" ? "Reminder: your appointment is in 2 hours." : "Reminder: you have an appointment tomorrow.",
                place, address is null ? null : $"Address: {address}", $"Service: {m.ServiceName}", $"Time: {time}"),

            // Staff (OD-10): clinic/branch/service/time and request number only — never the patient's phone or name.
            (BookingNotificationTypes.RequestedStaff, "uz") => Lines($"Yangi so'rov #{m.RequestNumber}", place, $"Xizmat: {m.ServiceName}", $"Vaqt: {time}",
                deadline is null ? null : $"Tasdiqlash muddati: {deadline}"),
            (BookingNotificationTypes.RequestedStaff, "ru") => Lines($"Новая заявка #{m.RequestNumber}", place, $"Услуга: {m.ServiceName}", $"Время: {time}",
                deadline is null ? null : $"Подтвердить до: {deadline}"),
            (BookingNotificationTypes.RequestedStaff, _) => Lines($"New request #{m.RequestNumber}", place, $"Service: {m.ServiceName}", $"Time: {time}",
                deadline is null ? null : $"Confirm by: {deadline}"),

            (BookingNotificationTypes.CancelledByPatientStaff, "uz") => Lines($"Bemor qabulni bekor qildi #{m.RequestNumber}", place, $"Xizmat: {m.ServiceName}", $"Vaqt: {time}"),
            (BookingNotificationTypes.CancelledByPatientStaff, "ru") => Lines($"Пациент отменил запись #{m.RequestNumber}", place, $"Услуга: {m.ServiceName}", $"Время: {time}"),
            (BookingNotificationTypes.CancelledByPatientStaff, _) => Lines($"The patient cancelled #{m.RequestNumber}", place, $"Service: {m.ServiceName}", $"Time: {time}"),

            (BookingNotificationTypes.ExpiredStaff, "uz") => Lines($"So'rov #{m.RequestNumber} muddati o'tdi (tasdiqlanmadi).", place, $"Xizmat: {m.ServiceName}", $"Vaqt: {time}"),
            (BookingNotificationTypes.ExpiredStaff, "ru") => Lines($"Заявка #{m.RequestNumber} просрочена (не подтверждена).", place, $"Услуга: {m.ServiceName}", $"Время: {time}"),
            (BookingNotificationTypes.ExpiredStaff, _) => Lines($"Request #{m.RequestNumber} expired (not confirmed).", place, $"Service: {m.ServiceName}", $"Time: {time}"),

            _ => null,
        };
    }

    /// <summary>Short SMS text for the OD-11 fallback types; null for every other type (reminders are Telegram only).</summary>
    public static string? Sms(string type, string? language, NoticeModel m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var l = Lang(language);
        var clinic = m.ClinicName.Length > 40 ? m.ClinicName[..40] : m.ClinicName;
        var time = FormatShort(m.StartsAt, m.TimeZone);
        return (type, l) switch
        {
            (BookingNotificationTypes.ConfirmedPatient, "uz") => $"OqTish: qabulingiz tasdiqlandi. {clinic}, {time}.",
            (BookingNotificationTypes.ConfirmedPatient, "ru") => $"OqTish: запись подтверждена. {clinic}, {time}.",
            (BookingNotificationTypes.ConfirmedPatient, _) => $"OqTish: appointment confirmed. {clinic}, {time}.",
            (BookingNotificationTypes.RejectedPatient, "uz") => $"OqTish: {time} dagi so'rovingiz rad etildi ({clinic}). Boshqa vaqtni tanlang.",
            (BookingNotificationTypes.RejectedPatient, "ru") => $"OqTish: заявка на {time} отклонена ({clinic}).",
            (BookingNotificationTypes.RejectedPatient, _) => $"OqTish: your request for {time} was declined ({clinic}).",
            (BookingNotificationTypes.CancelledByClinicPatient, "uz") => $"OqTish: {clinic} {time} dagi qabulingizni bekor qildi.",
            (BookingNotificationTypes.CancelledByClinicPatient, "ru") => $"OqTish: {clinic} отменила запись на {time}.",
            (BookingNotificationTypes.CancelledByClinicPatient, _) => $"OqTish: {clinic} cancelled your appointment on {time}.",
            (BookingNotificationTypes.RescheduledPatient, "uz") => $"OqTish: qabulingiz {time} ga ko'chirildi ({clinic}).",
            (BookingNotificationTypes.RescheduledPatient, "ru") => $"OqTish: запись перенесена на {time} ({clinic}).",
            (BookingNotificationTypes.RescheduledPatient, _) => $"OqTish: your appointment was moved to {time} ({clinic}).",
            _ => null,
        };
    }

    /// <summary>Text of the "open" button under patient messages.</summary>
    public static string OpenAppointmentsButton(string? language) => Lang(language) switch
    {
        "ru" => "Мои записи",
        "en" => "My appointments",
        _ => "Qabullarim",
    };

    /// <summary>Text of the "open in console" button under staff messages.</summary>
    public static string OpenConsoleButton(string? language) => Lang(language) switch
    {
        "ru" => "Открыть в консоли",
        "en" => "Open in console",
        _ => "Konsolda ochish",
    };

    public static string Reason(string? code, string? language)
    {
        var l = Lang(language);
        return (code, l) switch
        {
            ("NoDoctorAvailable", "uz") => "bo'sh shifokor yo'q",
            ("NoDoctorAvailable", "ru") => "нет свободного врача",
            ("NoDoctorAvailable", _) => "no doctor available",
            ("TimeUnavailable", "uz") => "bu vaqt band",
            ("TimeUnavailable", "ru") => "это время недоступно",
            ("TimeUnavailable", _) => "the time is unavailable",
            ("ServiceUnavailable", "uz") => "xizmat hozircha ko'rsatilmaydi",
            ("ServiceUnavailable", "ru") => "услуга недоступна",
            ("ServiceUnavailable", _) => "the service is unavailable",
            ("PatientUnreachable", "uz") => "siz bilan bog'lanib bo'lmadi",
            ("PatientUnreachable", "ru") => "не удалось с вами связаться",
            ("PatientUnreachable", _) => "we could not reach you",
            ("DoctorUnavailable", "uz") => "shifokor bu vaqtda ishlay olmaydi",
            ("DoctorUnavailable", "ru") => "врач недоступен",
            ("DoctorUnavailable", _) => "the doctor is unavailable",
            ("ClinicClosed", "uz") => "klinika yopiq",
            ("ClinicClosed", "ru") => "клиника закрыта",
            ("ClinicClosed", _) => "the clinic is closed",
            ("PatientRequest", "uz") => "sizning iltimosingizga ko'ra",
            ("PatientRequest", "ru") => "по вашей просьбе",
            ("PatientRequest", _) => "at your request",
            ("ClinicEmergency", "uz") => "klinikada favqulodda holat",
            ("ClinicEmergency", "ru") => "чрезвычайная ситуация в клинике",
            ("ClinicEmergency", _) => "an emergency at the clinic",
            (_, "uz") => "boshqa sabab",
            (_, "ru") => "другая причина",
            _ => "other reason",
        };
    }

    public static string Format(DateTimeOffset instant, string timeZone) =>
        BookingTime.ToLocal(instant, BookingTime.Zone(timeZone)).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string FormatShort(DateTimeOffset instant, string timeZone) =>
        BookingTime.ToLocal(instant, BookingTime.Zone(timeZone)).ToString("dd.MM HH:mm", CultureInfo.InvariantCulture);
}
