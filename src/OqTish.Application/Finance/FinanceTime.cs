using System.Globalization;
using OqTish.Application.Booking;
using OqTish.Domain.Tenancy;

namespace OqTish.Application.Finance;

/// <summary>
/// Reporting-day rules (FINANCIAL_RULES §5): the reporting day of an event is its <b>branch-local date</b> in the
/// branch's IANA zone (default Asia/Tashkent); clinic-level postings and settlement periods use Asia/Tashkent.
/// 2026-03-31T18:30Z = 23:30 local → 2026-03-31; 19:30Z = 00:30 local → 2026-04-01 (F8).
/// </summary>
public static class FinanceTime
{
    public static DateOnly LocalDate(DateTimeOffset instant, string? timeZone) => BookingTime.LocalDate(instant, BookingTime.Zone(timeZone));

    public static DateOnly MonthStart(DateOnly date) => new(date.Year, date.Month, 1);

    /// <summary>[start, end) UTC of a branch-local calendar month.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) MonthRange(int year, int month, string? timeZone = Branch.DefaultTimeZone)
    {
        var zone = BookingTime.Zone(timeZone);
        var first = new DateOnly(year, month, 1);
        return (BookingTime.DayRange(first, zone).Start, BookingTime.DayRange(first.AddMonths(1), zone).Start);
    }

    /// <summary>Strict <c>YYYY-MM-DD</c>.</summary>
    public static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>Wire form of a commission rate: invariant, exactly 4 decimals ("0.0750").</summary>
    public static string FormatRate(decimal rate) => rate.ToString("0.0000", CultureInfo.InvariantCulture);

    /// <summary>Parses "0.075" / "0.0750": digits and one decimal point only, no sign, exponent or spaces.</summary>
    public static bool TryParseRate(string? value, out decimal rate)
    {
        rate = 0m;
        return !string.IsNullOrEmpty(value)
            && value.All(c => char.IsAsciiDigit(c) || c == '.')
            && decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out rate);
    }
}
