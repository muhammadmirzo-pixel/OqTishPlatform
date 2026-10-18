// LINQ string calls below are translated to SQL lower(); .NET culture rules never apply.
#pragma warning disable CA1304, CA1311, CA1862
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Domain.Tenancy;

namespace OqTish.Application.Booking;

/// <summary>
/// GET /api/public/branches/{branchId}/availability?serviceId&amp;date (anonymous). The branch must be active,
/// its clinic Approved and the offer (and service) active, otherwise 404. <c>date</c> is a branch-local
/// <c>YYYY-MM-DD</c> within today … today + <c>Booking:HorizonDays</c> (else 400). Slots follow
/// <see cref="AvailabilityEngine"/> with "now" = database time and the 60-minute lead. The list is advisory:
/// booking recomputes everything under the branch lock.
/// </summary>
public sealed class AvailabilityQuery(IDbClock clock, AvailabilityLoader loader, BookingRules rules, IOptions<BookingOptions> options)
{
    public async Task<Result<AvailabilityDto>> HandleAsync(Guid branchId, Guid? serviceId, string? date, CancellationToken ct)
    {
        var o = options.Value;
        var errors = new FieldErrors();
        if (serviceId is null)
        {
            errors.Add("serviceId", "Required.");
        }

        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            errors.Add("date", "Use the local date format YYYY-MM-DD.");
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var offer = await rules.OfferAsync(branchId, serviceId!.Value, ct);
        if (offer is null || offer.ClinicStatus != ClinicVerificationStatus.Approved || !offer.IsBookable)
        {
            return CommonErrors.NotFound;
        }

        var zone = BookingTime.Zone(offer.TimeZone);
        var now = await clock.NowAsync(ct);
        var today = BookingTime.LocalDate(now, zone);
        if (day < today || day > today.AddDays(o.HorizonDays))
        {
            return Error.InvalidField("date", $"Must be between today and {o.HorizonDays} days ahead (branch time).");
        }

        var (start, end) = BookingTime.DayRange(day, zone);
        var snapshot = await loader.LoadAsync([branchId], [offer.ServiceId], start, end, now, ct);
        var input = snapshot.Day(branchId, offer.ServiceId, day, now.AddMinutes(o.MinLeadMinutes), o.SlotStepMinutes)!;
        var slots = AvailabilityEngine.FreeStarts(input);
        return new AvailabilityDto(LocalFormats.Date(day), offer.TimeZone, offer.DurationMinutes,
            [.. slots.Select(s => new AvailabilitySlotDto(s, BookingTime.LocalTime(s, zone)))]);
    }
}

/// <summary>
/// Availability for public search: the earliest free slot per clinic over the next N branch-local days
/// (today … today + N − 1), restricted to the matching branches (district) and offers (service id /
/// category). Constant number of queries for the whole clinic set, then in-memory evaluation with an early
/// exit per clinic. Used for <c>availableWithinDays</c> (filter, before paging) and <c>nextFreeSlot</c>.
/// </summary>
public sealed class PublicAvailability(IOqTishDb db, IDbClock clock, AvailabilityLoader loader, IOptions<BookingOptions> options)
{
    public async Task<IReadOnlyDictionary<Guid, DateTimeOffset>> NextFreeSlotsAsync(
        IReadOnlyCollection<Guid> clinicIds, Guid? serviceId, string? category, string? district, int days, CancellationToken ct)
    {
        var result = new Dictionary<Guid, DateTimeOffset>();
        if (clinicIds.Count == 0 || days < 1)
        {
            return result;
        }

        var o = options.Value;
        var ids = clinicIds.ToList();
        var branchQuery = db.Branches.AsNoTracking().Where(b => ids.Contains(b.ClinicId) && b.IsActive);
        if (!string.IsNullOrWhiteSpace(district))
        {
            var d = district.Trim().ToLower();
            branchQuery = branchQuery.Where(b => b.District.ToLower() == d);
        }

        var branches = await branchQuery.Select(b => new { b.Id, b.ClinicId }).ToListAsync(ct);
        if (branches.Count == 0)
        {
            return result;
        }

        List<Guid>? serviceIds = null;
        if (serviceId is not null || !string.IsNullOrEmpty(category))
        {
            serviceIds = await db.Services.AsNoTracking()
                .Where(s => ids.Contains(s.ClinicId) && (serviceId == null || s.Id == serviceId) && (string.IsNullOrEmpty(category) || s.Category == category))
                .Select(s => s.Id).ToListAsync(ct);
            if (serviceIds.Count == 0)
            {
                return result;
            }
        }

        var now = await clock.NowAsync(ct);
        var earliest = now.AddMinutes(o.MinLeadMinutes);
        var snapshot = await loader.LoadAsync([.. branches.Select(b => b.Id)], serviceIds, now, now.AddDays(days + 1), now, ct);
        foreach (var clinic in branches.GroupBy(b => b.ClinicId))
        {
            for (var i = 0; i < days && !result.ContainsKey(clinic.Key); i++)
            {
                DateTimeOffset? best = null;
                foreach (var branch in clinic)
                {
                    var zone = BookingTime.Zone(snapshot.TimeZoneOf(branch.Id));
                    var date = BookingTime.LocalDate(now, zone).AddDays(i);
                    foreach (var offer in snapshot.Offers.Keys.Where(k => k.BranchId == branch.Id))
                    {
                        var input = snapshot.Day(branch.Id, offer.ServiceId, date, earliest, o.SlotStepMinutes);
                        var first = input is null ? [] : AvailabilityEngine.FreeStarts(input);
                        if (first.Count > 0 && (best is null || first[0] < best))
                        {
                            best = first[0];
                        }
                    }
                }

                if (best is { } b)
                {
                    result[clinic.Key] = b;
                }
            }
        }

        return result;
    }
}
