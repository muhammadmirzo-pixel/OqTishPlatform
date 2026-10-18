using OqTish.Application.Common;
using OqTish.Domain.Tenancy;

namespace OqTish.Application.Booking;

/// <summary>
/// Branch-local ↔ UTC conversions (IANA zone, DST-safe) and the slot grid. Working hours and slots are
/// branch-local wall-clock times; everything stored is UTC.
/// </summary>
public static class BookingTime
{
    private static readonly TimeZoneInfo Fallback = TimeZoneInfo.CreateCustomTimeZone("UZT", TimeSpan.FromHours(5), "UZT", "UZT");

    public static TimeZoneInfo Zone(string? timeZone)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(timeZone) ? Branch.DefaultTimeZone : timeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            return Fallback;
        }
        catch (InvalidTimeZoneException)
        {
            return Fallback;
        }
    }

    /// <summary>The UTC instant of a branch-local wall-clock time; null when it does not exist (DST gap).</summary>
    public static DateTimeOffset? ToUtc(DateOnly date, TimeSpan timeOfDay, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified).Add(timeOfDay);
        if (zone.IsInvalidTime(local))
        {
            return null;
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    public static DateTime ToLocal(DateTimeOffset instant, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return TimeZoneInfo.ConvertTime(instant, zone).DateTime;
    }

    public static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) => DateOnly.FromDateTime(ToLocal(instant, zone));

    /// <summary>[start, end) in UTC of one branch-local calendar day.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) DayRange(DateOnly date, TimeZoneInfo zone)
    {
        var start = ToUtc(date, TimeSpan.Zero, zone) ?? ToUtc(date, TimeSpan.FromHours(1), zone)!.Value;
        var end = ToUtc(date.AddDays(1), TimeSpan.Zero, zone) ?? ToUtc(date.AddDays(1), TimeSpan.FromHours(1), zone)!.Value;
        return (start, end);
    }

    /// <summary>True when the instant is a whole-minute local time on the grid (minutes from local midnight divisible by the step).</summary>
    public static bool IsOnGrid(DateTimeOffset instant, TimeZoneInfo zone, int stepMinutes)
    {
        var local = ToLocal(instant, zone);
        return local.Second == 0 && local.Millisecond == 0 && local.Microsecond == 0 && (local.Hour * 60 + local.Minute) % stepMinutes == 0;
    }

    /// <summary>Weekday 1 = Monday … 7 = Sunday (ISO, as stored in working/opening hours).</summary>
    public static short IsoWeekday(DateOnly date) => (short)(date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek);

    public static string LocalTime(DateTimeOffset instant, TimeZoneInfo zone) => LocalFormats.Time(TimeOnly.FromDateTime(ToLocal(instant, zone)));
}
