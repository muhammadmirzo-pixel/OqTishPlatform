using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Application.Common.Seeding;
using OqTish.Application.Identity;
using OqTish.Application.Tenancy;
using OqTish.Domain.Booking;
using OqTish.Domain.Identity;
using OqTish.Domain.Reviews;
using OqTish.Domain.Scheduling;

namespace OqTish.Application.Booking;

/// <summary>
/// Demo appointments for the first demo clinic (Development/Staging seed, order 400): one of every status
/// for the demo patient (+998900000001) and a walk-in, at "Demo Chilonzor filiali", on the next weekday
/// (future ones) and the previous weekday (past ones), plus one Published review (clinic rating), one
/// review waiting for moderation and one open abuse report. Idempotent: skipped when the clinic already has
/// appointments. The Pending request expires 30 minutes after seeding (create new ones through the UI).
/// </summary>
public sealed class BookingDemoSeedContributor(IOqTishDb db, ITransactionRunner transactions, IDbClock clock, IOptions<SeedOptions> seed) : ISeedContributor
{
    public int Order => 400;

    public string Name => "booking-demo";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var result = await transactions.RunAsync(async ct =>
        {
            var clinicName = DemoData.Clinics[0].Name;
            var clinic = await db.Clinics.FirstOrDefaultAsync(c => c.Name == clinicName, ct);
            if (clinic is null || await db.Appointments.AnyAsync(a => a.ClinicId == clinic.Id, ct))
            {
                return Result.Success;
            }

            PhoneNumber.TryNormalize(seed.Value.DemoPatientPhone, out var phone);
            var patientId = await db.AuthIdentities.Where(i => i.Kind == AuthIdentityKind.Phone && i.NormalizedIdentifier == phone).Select(i => (Guid?)i.UserId).FirstOrDefaultAsync(ct);
            var branch = await db.Branches.AsNoTracking().Where(b => b.ClinicId == clinic.Id).OrderBy(b => b.Name).FirstOrDefaultAsync(ct);
            if (patientId is not { } patient || branch is null)
            {
                return Result.Success;
            }

            var doctors = await db.Doctors.AsNoTracking().Where(d => d.ClinicId == clinic.Id && db.DoctorBranches.Any(x => x.DoctorId == d.Id && x.BranchId == branch.Id))
                .OrderBy(d => d.CreatedAt).ThenBy(d => d.Id).Select(d => d.Id).ToListAsync(ct);
            var provided = await db.DoctorServices.AsNoTracking().Where(x => doctors.Contains(x.DoctorId)).Select(x => new { x.DoctorId, x.ServiceId }).ToListAsync(ct);
            var offers = await (
                    from o in db.BranchServices.AsNoTracking()
                    join s in db.Services.AsNoTracking() on o.ServiceId equals s.Id
                    where o.BranchId == branch.Id && o.IsActive && s.IsActive
                    orderby o.DurationMinutes, s.Name
                    select o)
                .ToListAsync(ct);
            var shared = offers.FirstOrDefault(o => doctors.Take(2).All(d => provided.Contains(new { DoctorId = d, o.ServiceId })));
            var staffUser = await db.ClinicMemberships.Where(m => m.ClinicId == clinic.Id).OrderBy(m => m.CreatedAt).Select(m => (Guid?)m.UserId).FirstOrDefaultAsync(ct) ?? patient;
            if (doctors.Count < 2 || shared is null)
            {
                return Result.Success;
            }

            var now = await clock.NowAsync(ct);
            var zone = BookingTime.Zone(branch.TimeZone);
            var today = BookingTime.LocalDate(now, zone);
            var next = NextWeekday(today, 1);
            var past = NextWeekday(today, -1);
            DateTimeOffset At(DateOnly d, int hour) => BookingTime.ToUtc(d, TimeSpan.FromHours(hour), zone)!.Value;
            var minutes = shared.DurationMinutes;
            var (aziza, other) = (doctors[0], doctors[1]);

            Appointment Pending(DateOnly d, int hour)
            {
                var start = At(d, hour);
                var a = Appointment.CreatePending(clinic.Id, branch.Id, shared.ServiceId, patient, start, start.AddMinutes(minutes), shared.PriceMoney, shared.PriceKind,
                    "Demo Bemor", phone, now, now.AddMinutes(30));
                db.Appointments.Add(a);
                db.AppointmentStatusHistory.Add(AppointmentStatusHistory.Create(clinic.Id, a.Id, null, AppointmentStatus.Pending, HistoryActorKind.Patient, patient, null, now));
                return a;
            }

            var pending = Pending(next, 10);
            var confirmed = Pending(next, 11);
            var cancelled = Pending(next, 12);
            var rejected = Pending(next, 16);
            var clinicCancelled = Pending(next, 17);
            var completedReviewed = Pending(past, 10);
            var completedWaiting = Pending(past, 11);
            var noShow = Pending(past, 14);
            var expired = Pending(past, 15);
            var walkInStart = At(next, 15);
            var walkIn = Appointment.CreateWalkIn(clinic.Id, branch.Id, shared.ServiceId, other, walkInStart, walkInStart.AddMinutes(minutes), shared.PriceMoney, shared.PriceKind,
                "Demo Mijoz (telefon)", "+998900000099", staffUser, now);
            db.Appointments.Add(walkIn);
            db.AppointmentStatusHistory.Add(AppointmentStatusHistory.Create(clinic.Id, walkIn.Id, null, AppointmentStatus.Confirmed, HistoryActorKind.Staff, staffUser, null, now));
            await db.SaveChangesAsync(ct);

            async Task MoveAsync(Appointment a, AppointmentStatus to, Guid? doctor, string? reason, HistoryActorKind actor, decimal? charged = null)
            {
                await db.Appointments.Where(x => x.Id == a.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, to)
                    .SetProperty(x => x.DoctorId, doctor)
                    .SetProperty(x => x.ReasonCode, reason)
                    .SetProperty(x => x.ChargedAmount, charged), ct);
                if (to is AppointmentStatus.Completed or AppointmentStatus.NoShow or AppointmentStatus.CancelledByClinic)
                {
                    db.AppointmentStatusHistory.Add(AppointmentStatusHistory.Create(clinic.Id, a.Id, AppointmentStatus.Pending, AppointmentStatus.Confirmed, HistoryActorKind.Staff, null, null, now));
                    db.AppointmentStatusHistory.Add(AppointmentStatusHistory.Create(clinic.Id, a.Id, AppointmentStatus.Confirmed, to, HistoryActorKind.Staff, null, reason, now));
                }
                else
                {
                    db.AppointmentStatusHistory.Add(AppointmentStatusHistory.Create(clinic.Id, a.Id, AppointmentStatus.Pending, to, actor, actor == HistoryActorKind.Patient ? patient : null, reason, now));
                }
            }

            await MoveAsync(confirmed, AppointmentStatus.Confirmed, aziza, null, HistoryActorKind.Staff);
            await MoveAsync(cancelled, AppointmentStatus.CancelledByPatient, null, ReasonCodes.PatientCancelled, HistoryActorKind.Patient);
            await MoveAsync(rejected, AppointmentStatus.Rejected, null, "NoDoctorAvailable", HistoryActorKind.Staff);
            await MoveAsync(clinicCancelled, AppointmentStatus.CancelledByClinic, other, "DoctorUnavailable", HistoryActorKind.Staff);
            await MoveAsync(completedReviewed, AppointmentStatus.Completed, aziza, null, HistoryActorKind.Staff, shared.Price);
            await MoveAsync(completedWaiting, AppointmentStatus.Completed, other, null, HistoryActorKind.Staff, shared.Price);
            await MoveAsync(noShow, AppointmentStatus.NoShow, aziza, null, HistoryActorKind.Staff);
            await MoveAsync(expired, AppointmentStatus.Expired, null, null, HistoryActorKind.System);

            db.DoctorTimeBlocks.Add(DoctorTimeBlock.ForAppointment(clinic.Id, branch.Id, other, pending.Id, pending.StartsAt, pending.EndsAt, TimeBlockKind.Hold, now));
            db.DoctorTimeBlocks.Add(DoctorTimeBlock.ForAppointment(clinic.Id, branch.Id, aziza, confirmed.Id, confirmed.StartsAt, confirmed.EndsAt, TimeBlockKind.Appointment, now));
            db.DoctorTimeBlocks.Add(DoctorTimeBlock.ForAppointment(clinic.Id, branch.Id, other, walkIn.Id, walkIn.StartsAt, walkIn.EndsAt, TimeBlockKind.Appointment, now));

            var published = Review.Create(clinic.Id, branch.Id, completedReviewed.Id, patient, 5, "Demo sharh: shifokor juda e'tiborli, navbat kutilmadi.", now);
            published.Publish(patient, now);
            db.Reviews.Add(published);
            db.Reviews.Add(Review.Create(clinic.Id, branch.Id, completedWaiting.Id, patient, 4, "Demo sharh (moderatsiyada): hammasi yaxshi.", now));
            db.AbuseReports.Add(AbuseReport.Create(ReportTargetType.Clinic, clinic.Id, clinic.Id, patient, "Demo shikoyat: telefon raqami ishlamayapti.", now));
            clinic.SetRating(5m, 1, now);
            await db.SaveChangesAsync(ct);
            return Result.Success;
        }, cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Booking demo seed failed: {result.Error.Code}");
        }
    }

    private static DateOnly NextWeekday(DateOnly from, int direction)
    {
        var d = from.AddDays(direction);
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            d = d.AddDays(direction);
        }

        return d;
    }
}
