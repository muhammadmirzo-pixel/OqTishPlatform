using OqTish.Application.Common;
using OqTish.Application.Identity;

namespace OqTish.Application.Tenancy;

/// <summary>Clinic/branch error codes (docs/API.md §9).</summary>
public static class TenancyErrors
{
    public static readonly Error RegistrationLimit = Error.Rule("clinic.registration_limit", "Too many clinics are waiting for verification for this account.");
}

/// <summary>Validation shared by clinic and branch handlers.</summary>
internal static class TenancyRules
{
    /// <summary>Pending clinics one user may have registered at the same time.</summary>
    public const int MaxPendingClinicsPerUser = 3;

    /// <summary>Uzbekistan IANA zones (v1 is Uzbekistan-only, A1).</summary>
    public static readonly IReadOnlyList<string> TimeZones = ["Asia/Tashkent", "Asia/Samarkand"];

    public static string? OptionalPhone(this FieldErrors errors, string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (PhoneNumber.TryNormalize(value, out var e164))
        {
            return e164;
        }

        errors.Add(field, "Enter an Uzbekistan phone number (+998 and 9 digits).");
        return null;
    }

    public static (string Name, string? LegalName, string? Description, string? Phone)? ValidateProfile(ClinicProfileCommand c, FieldErrors errors)
    {
        var name = errors.Text(c.Name, "name", 200, required: true);
        var legal = errors.Text(c.LegalName, "legalName", 300, required: false);
        var description = errors.Text(c.Description, "description", 4000, required: false);
        var phone = errors.OptionalPhone(c.Phone, "phone");
        return errors.HasErrors ? null : (name!, legal, description, phone);
    }

    public static ValidBranch? ValidateBranch(BranchCommand c, FieldErrors errors)
    {
        var name = errors.Text(c.Name, "name", 200, required: true);
        var district = errors.Text(c.District, "district", 100, required: true);
        var address = errors.Text(c.Address, "address", 500, required: true);
        var phone = errors.OptionalPhone(c.Phone, "phone");
        if ((c.Latitude is null) != (c.Longitude is null))
        {
            errors.Add("latitude", "Latitude and longitude go together.");
        }

        if (c.Latitude is { } lat && (lat < -90 || lat > 90 || decimal.Round(lat, 6) != lat))
        {
            errors.Add("latitude", "Must be within -90..90 with at most 6 decimals.");
        }

        if (c.Longitude is { } lng && (lng < -180 || lng > 180 || decimal.Round(lng, 6) != lng))
        {
            errors.Add("longitude", "Must be within -180..180 with at most 6 decimals.");
        }

        var timeZone = string.IsNullOrWhiteSpace(c.TimeZone) ? TimeZones[0] : c.TimeZone.Trim();
        if (!TimeZones.Contains(timeZone, StringComparer.Ordinal))
        {
            errors.Add("timeZone", "Unsupported time zone (Asia/Tashkent or Asia/Samarkand).");
        }

        return errors.HasErrors ? null : new ValidBranch(name!, district!, address!, c.Latitude, c.Longitude, phone, timeZone, c.IsActive ?? true);
    }

    /// <summary>Weekly intervals: weekday 1..7, HH:mm, open &lt; close, no overlap on the same weekday.</summary>
    public static List<(short Weekday, TimeOnly Open, TimeOnly Close)>? ValidateHours(IReadOnlyList<OpeningHoursDto>? hours, FieldErrors errors)
    {
        if (hours is null)
        {
            errors.Add("hours", "A JSON array is required.");
            return null;
        }

        if (hours.Count > 28)
        {
            errors.Add("hours", "At most 28 intervals.");
            return null;
        }

        var result = new List<(short, TimeOnly, TimeOnly)>();
        for (var i = 0; i < hours.Count; i++)
        {
            var h = hours[i];
            if (h is null)
            {
                errors.Add($"hours[{i}]", "Required.");
                continue;
            }

            var open = errors.Time(h.Open, $"hours[{i}].open");
            var close = errors.Time(h.Close, $"hours[{i}].close");
            if (h.Weekday is < 1 or > 7)
            {
                errors.Add($"hours[{i}].weekday", "ISO weekday 1..7 (1 = Monday).");
            }
            else if (open is { } o && close is { } c)
            {
                if (o >= c)
                {
                    errors.Add($"hours[{i}]", "Opening time must be before closing time.");
                }
                else
                {
                    result.Add(((short)h.Weekday, o, c));
                }
            }
        }

        foreach (var day in result.GroupBy(r => r.Item1))
        {
            var ordered = day.OrderBy(r => r.Item2).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].Item2 < ordered[i - 1].Item3)
                {
                    errors.Add("hours", $"Intervals overlap on weekday {day.Key}.");
                    break;
                }
            }
        }

        return errors.HasErrors ? null : result;
    }

    /// <summary>"Today" for a clinic: the branch's zone, Asia/Tashkent by default.</summary>
    public static DateOnly LocalToday(DateTimeOffset utcNow, string? timeZone)
    {
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone ?? TimeZones[0]);
        }
        catch (TimeZoneNotFoundException)
        {
            zone = TimeZoneInfo.CreateCustomTimeZone("UZT", TimeSpan.FromHours(5), "UZT", "UZT");
        }

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, zone).DateTime);
    }
}

internal sealed record ValidBranch(string Name, string District, string Address, decimal? Latitude, decimal? Longitude, string? Phone, string TimeZone, bool IsActive);
