using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Identity;
using OqTish.Domain.Booking;
using OqTish.Domain.Catalog;
using OqTish.Domain.Common;
using OqTish.Domain.Tenancy;

namespace OqTish.Application.Booking;

/// <summary>Branch + clinic + offer facts needed to validate a booking.</summary>
public sealed record BookableOffer(
    Guid ClinicId,
    ClinicVerificationStatus ClinicStatus,
    Guid BranchId,
    bool BranchActive,
    string TimeZone,
    Guid ServiceId,
    bool ServiceActive,
    bool OfferActive,
    Money Price,
    PriceKind PriceKind,
    int DurationMinutes)
{
    public bool IsBookable => BranchActive && ServiceActive && OfferActive;
}

/// <summary>Shared validation and lookups for booking handlers.</summary>
public sealed class BookingRules(IOqTishDb db)
{
    /// <summary>The branch's offer of the service, or null when the branch or the offer does not exist.</summary>
    public async Task<BookableOffer?> OfferAsync(Guid branchId, Guid serviceId, CancellationToken ct)
    {
        var row = await (
                from b in db.Branches.AsNoTracking()
                join c in db.Clinics.AsNoTracking() on b.ClinicId equals c.Id
                join o in db.BranchServices.AsNoTracking() on new { BranchId = b.Id, b.ClinicId } equals new { o.BranchId, o.ClinicId }
                join s in db.Services.AsNoTracking() on o.ServiceId equals s.Id
                where b.Id == branchId && o.ServiceId == serviceId
                select new { b.ClinicId, c.VerificationStatus, b.IsActive, b.TimeZone, s.Id, ServiceActive = s.IsActive, OfferActive = o.IsActive, o.Price, o.Currency, o.PriceKind, o.DurationMinutes })
            .FirstOrDefaultAsync(ct);
        return row is null
            ? null
            : new BookableOffer(row.ClinicId, row.VerificationStatus, branchId, row.IsActive, row.TimeZone, row.Id, row.ServiceActive, row.OfferActive,
                new Money(row.Price, row.Currency), row.PriceKind, row.DurationMinutes);
    }

    /// <summary>The doctor if active, of the clinic, linked to the branch and providing the service; otherwise null.</summary>
    public Task<EligibleDoctor?> EligibleDoctorAsync(Guid clinicId, Guid branchId, Guid serviceId, Guid doctorId, CancellationToken ct) =>
        db.Doctors.AsNoTracking()
            .Where(d => d.Id == doctorId && d.ClinicId == clinicId && d.IsActive
                && db.DoctorBranches.Any(x => x.DoctorId == d.Id && x.BranchId == branchId)
                && db.DoctorServices.Any(x => x.DoctorId == d.Id && x.ServiceId == serviceId))
            .Select(d => new EligibleDoctor(d.Id, d.DisplayName))
            .FirstOrDefaultAsync(ct);

    /// <summary>Ids of all doctors eligible for (branch, service) — used to scope the sweep-on-write.</summary>
    public Task<List<Guid>> EligibleDoctorIdsAsync(Guid branchId, Guid serviceId, CancellationToken ct) =>
        db.Doctors.AsNoTracking()
            .Where(d => d.IsActive
                && db.DoctorBranches.Any(x => x.DoctorId == d.Id && x.BranchId == branchId)
                && db.DoctorServices.Any(x => x.DoctorId == d.Id && x.ServiceId == serviceId))
            .Select(d => d.Id)
            .ToListAsync(ct);

    /// <summary>Contact name 1..100 and an Uzbekistan phone (normalised to E.164).</summary>
    public static (string Name, string Phone)? Contact(FieldErrors errors, string? name, string? phone)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var n = errors.Text(name, "contactName", Appointment.MaxContactNameLength, required: true);
        string? p = null;
        if (string.IsNullOrWhiteSpace(phone))
        {
            errors.Add("contactPhone", "Required.");
        }
        else if (PhoneNumber.TryNormalize(phone, out var e164))
        {
            p = e164;
        }
        else
        {
            errors.Add("contactPhone", "Enter an Uzbekistan phone number (+998 and 9 digits).");
        }

        return n is null || p is null ? null : (n, p);
    }

    /// <summary>Latest bookable instant: the end of the branch-local day today + horizon.</summary>
    public static DateTimeOffset HorizonEnd(DateTimeOffset now, TimeZoneInfo zone, int horizonDays) =>
        BookingTime.DayRange(BookingTime.LocalDate(now, zone).AddDays(horizonDays), zone).End;
}

public sealed record EligibleDoctor(Guid Id, string Name);
