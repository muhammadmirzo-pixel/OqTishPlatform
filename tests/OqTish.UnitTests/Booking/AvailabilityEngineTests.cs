using OqTish.Application.Admin;
using OqTish.Application.Booking;
using OqTish.Application.Reviews;

namespace OqTish.UnitTests.Booking;

public class AvailabilityEngineTests
{
    private static readonly TimeZoneInfo Tashkent = BookingTime.Zone("Asia/Tashkent");
    private static readonly DateOnly Day = new(2026, 10, 20);

    private static DateTimeOffset At(int h, int m = 0) => BookingTime.ToUtc(Day, new TimeSpan(h, m, 0), Tashkent)!.Value;

    private static LocalInterval Local(int fromH, int toH) => new(TimeSpan.FromHours(fromH), TimeSpan.FromHours(toH));

    private static DoctorDay Doctor(int services = 1, LocalInterval[]? hours = null, params BusyInterval[] busy) =>
        new(Guid.NewGuid(), "D", services, hours ?? [Local(9, 13), Local(14, 18)], busy);

    private static AvailabilityDay Input(IReadOnlyList<DoctorDay> doctors, int duration = 30, LocalInterval[]? opening = null, DateTimeOffset? earliest = null) =>
        new(Day, Tashkent, 15, duration, earliest ?? DateTimeOffset.MinValue, opening, doctors);

    [Fact]
    public void Slots_follow_working_hours_and_never_cross_a_shift_end()
    {
        var slots = AvailabilityEngine.FreeStarts(Input([Doctor()]));
        Assert.Equal(At(9), slots[0]);
        Assert.Contains(At(12, 30), slots);
        Assert.DoesNotContain(At(12, 45), slots); // 12:45–13:15 crosses the lunch break
        Assert.Contains(At(14), slots);
        Assert.Equal(At(17, 30), slots[^1]);
        Assert.Equal(15 + 15, slots.Count);
    }

    [Fact]
    public void Busy_blocks_time_off_lead_time_and_opening_hours_remove_slots()
    {
        var d = Doctor(1, null, new BusyInterval(At(10), At(10, 30), false), new BusyInterval(At(15), At(16), true));
        var slots = AvailabilityEngine.FreeStarts(Input([d], earliest: At(9, 30), opening: [Local(9, 17)]));
        Assert.DoesNotContain(At(9, 15), slots);  // lead time
        Assert.Contains(At(9, 30), slots);
        Assert.DoesNotContain(At(9, 45), slots);  // overlaps the 10:00 hold
        Assert.DoesNotContain(At(10), slots);
        Assert.Contains(At(10, 30), slots);       // adjacent is fine ([) intervals)
        Assert.DoesNotContain(At(15, 30), slots); // time off
        Assert.Contains(At(16), slots);
        Assert.DoesNotContain(At(16, 45), slots); // branch closes at 17:00
        Assert.Empty(AvailabilityEngine.FreeStarts(Input([d], opening: [])));   // configured but closed that weekday
        Assert.Empty(AvailabilityEngine.FreeStarts(Input([])));
    }

    [Fact]
    public void One_free_doctor_is_enough_and_candidates_are_least_flexible_first()
    {
        var busy = Doctor(1, null, new BusyInterval(At(10), At(11), false));
        var free = Doctor(3);
        Assert.Contains(At(10), AvailabilityEngine.FreeStarts(Input([busy, free])));

        var flexible = Doctor(4);
        var specialist = Doctor(1);
        var loaded = Doctor(1, null, new BusyInterval(At(15), At(16), false));
        var order = AvailabilityEngine.Candidates([flexible, loaded, specialist, busy], At(10), At(10, 30));
        Assert.Equal([specialist.DoctorId, loaded.DoctorId, flexible.DoctorId], order.Select(o => o.DoctorId));
    }

    [Fact]
    public void Local_dates_grid_and_weekdays()
    {
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 19, 15, 0, TimeSpan.Zero), BookingTime.ToUtc(Day, new TimeSpan(0, 15, 0), Tashkent));
        Assert.Equal(Day, BookingTime.LocalDate(new DateTimeOffset(2026, 10, 19, 19, 0, 0, TimeSpan.Zero), Tashkent));
        Assert.Equal(Day.AddDays(-1), BookingTime.LocalDate(new DateTimeOffset(2026, 10, 19, 18, 59, 0, TimeSpan.Zero), Tashkent));
        Assert.True(BookingTime.IsOnGrid(At(10, 45), Tashkent, 15));
        Assert.False(BookingTime.IsOnGrid(At(10, 50), Tashkent, 15));
        Assert.False(BookingTime.IsOnGrid(At(10).AddSeconds(1), Tashkent, 15));
        Assert.Equal(2, BookingTime.IsoWeekday(Day));                 // 2026-10-20 is a Tuesday
        Assert.Equal(7, BookingTime.IsoWeekday(new DateOnly(2026, 10, 25)));
        var (start, end) = BookingTime.DayRange(Day, Tashkent);
        Assert.Equal((TimeSpan.FromHours(24), 19), (end - start, start.UtcDateTime.Hour));
        Assert.Equal("10:45", BookingTime.LocalTime(At(10, 45), Tashkent));
        Assert.Equal(TimeSpan.FromHours(5), BookingTime.Zone("Nowhere/Unknown").BaseUtcOffset);
    }

    [Fact]
    public void Break_glass_reason_is_percent_decoded_and_length_checked()
    {
        const string reason = "Bemor shikoyati bo‘yicha tekshiruv №12";
        Assert.Equal(reason, TenantDataQueries.DecodeReason(Uri.EscapeDataString(reason)));
        Assert.Equal(reason, TenantDataQueries.DecodeReason("  " + Uri.EscapeDataString(reason) + " "));
        Assert.Null(TenantDataQueries.DecodeReason(null));
        Assert.Null(TenantDataQueries.DecodeReason(""));
        Assert.Null(TenantDataQueries.DecodeReason(Uri.EscapeDataString("too short")));
        Assert.Null(TenantDataQueries.DecodeReason(Uri.EscapeDataString(new string('x', 301))));
        Assert.Null(TenantDataQueries.DecodeReason(Uri.EscapeDataString("line one is long\nline two")));
    }

    [Fact]
    public void Reason_codes_accept_the_brief_and_the_ui_lists()
    {
        foreach (var code in new[] { "NoDoctorAvailable", "TimeUnavailable", "ServiceUnavailable", "PatientUnreachable", "Other" })
        {
            Assert.True(ReasonCodes.IsValid(ReasonCodes.Reject, code), code);
        }

        foreach (var code in new[] { "DoctorUnavailable", "ClinicClosed", "PatientRequest", "ClinicEmergency", "Other" })
        {
            Assert.True(ReasonCodes.IsValid(ReasonCodes.ClinicCancel, code), code);
        }

        Assert.False(ReasonCodes.IsValid(ReasonCodes.Reject, "ClinicClosed"));
        Assert.False(ReasonCodes.IsValid(ReasonCodes.ClinicCancel, null));
        Assert.Equal("Aziza K.", ReviewAuthor.Display("Aziza karimova"));
        Assert.Equal("Aziza", ReviewAuthor.Display(" Aziza "));
        Assert.Equal(ReviewAuthor.Unknown, ReviewAuthor.Display("—"));
        Assert.Equal(ReviewAuthor.Unknown, ReviewAuthor.Display(null));
    }
}
