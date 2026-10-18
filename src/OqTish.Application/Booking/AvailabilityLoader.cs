using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Domain.Booking;
using OqTish.Domain.Scheduling;

namespace OqTish.Application.Booking;

/// <summary>
/// Loads, in a constant number of queries, everything the <see cref="AvailabilityEngine"/> needs for a set of
/// branches over a range of local dates: opening hours, active offers, eligible doctors with their working
/// hours and service counts, and active busy blocks (overdue holds excluded). Read-only; callers that write
/// (booking) run it inside their locked transaction.
/// </summary>
public sealed class AvailabilityLoader(IOqTishDb db)
{
    public async Task<AvailabilitySnapshot> LoadAsync(
        IReadOnlyCollection<Guid> branchIds,
        IReadOnlyCollection<Guid>? serviceIds,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var ids = branchIds.Distinct().ToList();
        var branches = await db.Branches.AsNoTracking().Where(b => ids.Contains(b.Id)).Select(b => new { b.Id, b.TimeZone }).ToListAsync(ct);
        var opening = await db.BranchOpeningHours.AsNoTracking().Where(h => ids.Contains(h.BranchId))
            .Select(h => new { h.BranchId, h.Weekday, h.Opens, h.Closes }).ToListAsync(ct);

        var offerQuery =
            from o in db.BranchServices.AsNoTracking()
            join s in db.Services.AsNoTracking() on o.ServiceId equals s.Id
            where ids.Contains(o.BranchId) && o.IsActive && s.IsActive
            select new { o.BranchId, o.ServiceId, o.DurationMinutes };
        if (serviceIds is { Count: > 0 })
        {
            var sids = serviceIds.ToList();
            offerQuery = offerQuery.Where(o => sids.Contains(o.ServiceId));
        }

        var offers = await offerQuery.ToListAsync(ct);
        var links = await (
                from l in db.DoctorBranches.AsNoTracking()
                join d in db.Doctors.AsNoTracking() on l.DoctorId equals d.Id
                where ids.Contains(l.BranchId) && d.IsActive
                select new { l.BranchId, l.DoctorId, d.DisplayName })
            .ToListAsync(ct);
        var doctorIds = links.Select(l => l.DoctorId).Distinct().ToList();
        var provided = await db.DoctorServices.AsNoTracking().Where(x => doctorIds.Contains(x.DoctorId))
            .Select(x => new { x.DoctorId, x.ServiceId }).ToListAsync(ct);
        var hours = await db.WorkingHours.AsNoTracking().Where(w => doctorIds.Contains(w.DoctorId) && ids.Contains(w.BranchId))
            .Select(w => new { w.DoctorId, w.BranchId, w.Weekday, w.StartLocal, w.EndLocal }).ToListAsync(ct);
        var busy = await BusyQuery(doctorIds, rangeStart, rangeEnd, now).ToListAsync(ct);

        return new AvailabilitySnapshot(
            branches.ToDictionary(b => b.Id, b => b.TimeZone),
            [.. opening.Select(o => (o.BranchId, o.Weekday, new LocalInterval(o.Opens.ToTimeSpan(), o.Closes.ToTimeSpan())))],
            offers.ToDictionary(o => (o.BranchId, o.ServiceId), o => o.DurationMinutes),
            [.. links.Select(l => (l.BranchId, l.DoctorId, l.DisplayName))],
            [.. provided.Select(p => (p.DoctorId, p.ServiceId))],
            [.. hours.Select(h => (h.DoctorId, h.BranchId, h.Weekday, new LocalInterval(h.StartLocal.ToTimeSpan(), h.EndLocal.ToTimeSpan())))],
            [.. busy.Select(b => (b.DoctorId, new BusyInterval(b.StartsAt, b.EndsAt, b.Kind == TimeBlockKind.TimeOff)))]);
    }

    /// <summary>Active blocks of the doctors overlapping [start, end), without holds of overdue Pending requests.</summary>
    public IQueryable<BusyRow> BusyQuery(IReadOnlyCollection<Guid> doctorIds, DateTimeOffset start, DateTimeOffset end, DateTimeOffset now, Guid? excludeAppointmentId = null)
    {
        var ids = doctorIds.ToList();
        return from b in db.DoctorTimeBlocks.AsNoTracking()
               where b.IsActive && ids.Contains(b.DoctorId) && b.StartsAt < end && b.EndsAt > start
               where excludeAppointmentId == null || b.AppointmentId == null || b.AppointmentId != excludeAppointmentId
               where !db.Appointments.Any(a => a.Id == b.AppointmentId && a.Status == AppointmentStatus.Pending && a.ExpiresAt <= now)
               select new BusyRow(b.DoctorId, b.StartsAt, b.EndsAt, b.Kind, b.AppointmentId);
    }
}

public sealed record BusyRow(Guid DoctorId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, TimeBlockKind Kind, Guid? AppointmentId);

/// <summary>Loaded availability data; <see cref="Day"/> builds the engine input for one branch/service/date.</summary>
public sealed class AvailabilitySnapshot(
    IReadOnlyDictionary<Guid, string> branchTimeZones,
    IReadOnlyList<(Guid BranchId, short Weekday, LocalInterval Interval)> opening,
    IReadOnlyDictionary<(Guid BranchId, Guid ServiceId), int> offers,
    IReadOnlyList<(Guid BranchId, Guid DoctorId, string Name)> links,
    IReadOnlyList<(Guid DoctorId, Guid ServiceId)> provided,
    IReadOnlyList<(Guid DoctorId, Guid BranchId, short Weekday, LocalInterval Interval)> hours,
    IReadOnlyList<(Guid DoctorId, BusyInterval Interval)> busy)
{
    public IReadOnlyDictionary<(Guid BranchId, Guid ServiceId), int> Offers => offers;

    public string TimeZoneOf(Guid branchId) => branchTimeZones.TryGetValue(branchId, out var tz) ? tz : Domain.Tenancy.Branch.DefaultTimeZone;

    /// <summary>Opening intervals of the weekday, or null when the branch has no opening hours configured (unrestricted).</summary>
    public IReadOnlyList<LocalInterval>? OpeningHours(Guid branchId, short weekday)
    {
        var all = opening.Where(o => o.BranchId == branchId).ToList();
        return all.Count == 0 ? null : [.. all.Where(o => o.Weekday == weekday).Select(o => o.Interval)];
    }

    /// <summary>Eligible doctors (active, linked to the branch, providing the service) with that day's hours and busy blocks.</summary>
    public IReadOnlyList<DoctorDay> Doctors(Guid branchId, Guid serviceId, DateOnly date)
    {
        var weekday = BookingTime.IsoWeekday(date);
        var zone = BookingTime.Zone(TimeZoneOf(branchId));
        var (dayStart, dayEnd) = BookingTime.DayRange(date, zone);
        return [.. links.Where(l => l.BranchId == branchId && provided.Contains((l.DoctorId, serviceId)))
            .Select(l => new DoctorDay(
                l.DoctorId,
                l.Name,
                provided.Count(p => p.DoctorId == l.DoctorId),
                [.. hours.Where(h => h.DoctorId == l.DoctorId && h.BranchId == branchId && h.Weekday == weekday).Select(h => h.Interval)],
                [.. busy.Where(b => b.DoctorId == l.DoctorId && b.Interval.Start < dayEnd && b.Interval.End > dayStart).Select(b => b.Interval)]))];
    }

    public AvailabilityDay? Day(Guid branchId, Guid serviceId, DateOnly date, DateTimeOffset earliestStart, int stepMinutes)
    {
        if (!offers.TryGetValue((branchId, serviceId), out var duration))
        {
            return null;
        }

        var zone = BookingTime.Zone(TimeZoneOf(branchId));
        return new AvailabilityDay(date, zone, stepMinutes, duration, earliestStart, OpeningHours(branchId, BookingTime.IsoWeekday(date)), Doctors(branchId, serviceId, date));
    }
}
