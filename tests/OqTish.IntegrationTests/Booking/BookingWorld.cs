using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OqTish.Application.Booking;
using OqTish.Application.Common;
using OqTish.Domain.Catalog;
using OqTish.Domain.Common;
using OqTish.Domain.Identity;
using OqTish.Domain.Scheduling;
using OqTish.Domain.Staff;
using OqTish.Domain.Tenancy;
using OqTish.Infrastructure.Persistence;

namespace OqTish.IntegrationTests.Booking;

/// <summary>
/// A fresh, isolated booking fixture inside an existing database: an Approved clinic with one Asia/Tashkent
/// branch, a 30-minute and a 60-minute service, doctors providing both and working 08:00–20:00 every day, a
/// receptionist (Clinic scope) and patients. <see cref="Date"/> is the branch-local date three days ahead.
/// Cheap enough to create per concurrency iteration.
/// </summary>
public sealed record BookingWorld(
    Guid ClinicId,
    Guid BranchId,
    Guid Service30,
    Guid Service60,
    IReadOnlyList<Guid> Doctors,
    Guid StaffUserId,
    IReadOnlyList<Guid> Patients,
    DateOnly Date)
{
    public static readonly TimeZoneInfo Zone = BookingTime.Zone("Asia/Tashkent");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The UTC instant of <paramref name="hour"/>:<paramref name="minute"/> branch time on <see cref="Date"/> (+ <paramref name="days"/>).</summary>
    public DateTimeOffset At(int hour, int minute = 0, int days = 0) => BookingTime.ToUtc(Date.AddDays(days), new TimeSpan(hour, minute, 0), Zone)!.Value;

    public static async Task<BookingWorld> CreateAsync(OqTishDbContext db, int doctors = 1, int patients = 1, int daysAhead = 3, TimeOnly? hoursStart = null, TimeOnly? hoursEnd = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        var now = DateTimeOffset.UtcNow;
        var clinic = Clinic.Register("Booking test " + Guid.NewGuid().ToString("N")[..8], null, null, null, null, now);
        clinic.Approve(now);
        db.Clinics.Add(clinic);
        var branch = Branch.Create(clinic.Id, "Filial", "Yunusobod", "Test 1", null, null, null, "Asia/Tashkent", now);
        db.Branches.Add(branch);
        var s30 = Service.Create(clinic.Id, "Ko'rik", "diagnostics", true, now);
        var s60 = Service.Create(clinic.Id, "Plomba", "therapy", true, now);
        db.Services.AddRange(s30, s60);
        db.BranchServices.Add(BranchService.Create(clinic.Id, branch.Id, s30.Id, Money.Uzs(100000), PriceKind.Fixed, 30, true, now));
        db.BranchServices.Add(BranchService.Create(clinic.Id, branch.Id, s60.Id, Money.Uzs(300000), PriceKind.From, 60, true, now));
        var doctorIds = new List<Guid>();
        for (var i = 0; i < doctors; i++)
        {
            var d = Doctor.Create(clinic.Id, $"Dr. {(char)('A' + i)}", true, null, now);
            db.Doctors.Add(d);
            doctorIds.Add(d.Id);
        }

        var staff = User.Create("Qabulxona", now);
        db.Users.Add(staff);
        var patientIds = new List<Guid>();
        for (var i = 0; i < patients; i++)
        {
            var p = User.Create("Bemor " + i, now);
            db.Users.Add(p);
            patientIds.Add(p.Id);
        }

        await db.SaveChangesAsync(Ct);
        foreach (var d in doctorIds)
        {
            db.DoctorBranches.Add(DoctorBranch.Create(clinic.Id, d, branch.Id));
            db.DoctorServices.Add(DoctorService.Create(clinic.Id, d, s30.Id));
            db.DoctorServices.Add(DoctorService.Create(clinic.Id, d, s60.Id));
        }

        db.ClinicMemberships.Add(ClinicMembership.Create(clinic.Id, staff.Id, StaffRole.Receptionist, MembershipScope.Clinic, now));
        await db.SaveChangesAsync(Ct);
        foreach (var d in doctorIds)
        {
            for (short day = 1; day <= 7; day++)
            {
                db.WorkingHours.Add(WorkingHours.Create(clinic.Id, d, branch.Id, day, hoursStart ?? new TimeOnly(8, 0), hoursEnd ?? new TimeOnly(20, 0)));
            }
        }

        await db.SaveChangesAsync(Ct);
        var date = BookingTime.LocalDate(DateTimeOffset.UtcNow, Zone).AddDays(daysAhead);
        return new BookingWorld(clinic.Id, branch.Id, s30.Id, s60.Id, doctorIds, staff.Id, patientIds, date);
    }

    public CreateAppointmentCommand Request(DateTimeOffset startsAt, Guid? serviceId = null) =>
        new(BranchId, serviceId ?? Service30, startsAt, "Test Bemor", "+998901234567");
}

/// <summary>Shortcuts over <see cref="BookingHost"/> for the booking handlers.</summary>
public static class BookingCalls
{
    public static Task<Result<AppointmentDto>> BookAsync(this BookingHost host, BookingWorld w, Guid patient, DateTimeOffset start, Guid? serviceId = null, string? key = null) =>
        host.RunAsync(patient, sp => sp.GetRequiredService<CreateAppointmentHandler>().HandleAsync(w.Request(start, serviceId), key, TestContext.Current.CancellationToken));

    public static Task<Result<AppointmentDto>> ConfirmAsync(this BookingHost host, BookingWorld w, Guid appointmentId, Guid doctorId, Guid? staff = null) =>
        host.RunAsync(staff ?? w.StaffUserId, sp => sp.GetRequiredService<StaffAppointmentHandlers>().ConfirmAsync(w.ClinicId, appointmentId, new ConfirmCommand(doctorId), TestContext.Current.CancellationToken));

    public static Task<int> SweepAsync(this BookingHost host) =>
        host.RunAsync<int>(null, sp => sp.GetRequiredService<ExpirePendingAppointmentsHandler>().RunAsync(TestContext.Current.CancellationToken));

    /// <summary>Makes a Pending request overdue (or due in <paramref name="offset"/>) relative to database time.</summary>
    public static async Task SetDeadlineAsync(this BookingHost host, Guid appointmentId, TimeSpan offset)
    {
        await using var c = new NpgsqlConnection(host.ConnectionString);
        await c.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand("UPDATE booking.appointments SET expires_at = clock_timestamp() + @o WHERE id = @id", c);
        cmd.Parameters.AddWithValue("o", offset);
        cmd.Parameters.AddWithValue("id", appointmentId);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Moves an appointment (and its block) in time, for outcome tests that need a past start.</summary>
    public static async Task ShiftAsync(string connectionString, Guid appointmentId, DateTimeOffset newStart)
    {
        await using var c = new NpgsqlConnection(connectionString);
        await c.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand("""
            UPDATE booking.appointments SET ends_at = @s + (ends_at - starts_at), starts_at = @s WHERE id = @id;
            UPDATE scheduling.doctor_time_blocks SET ends_at = @s + (ends_at - starts_at), starts_at = @s WHERE appointment_id = @id;
            """, c);
        cmd.Parameters.AddWithValue("s", newStart.UtcDateTime);
        cmd.Parameters.AddWithValue("id", appointmentId);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public static async Task<Guid?> HeldDoctorAsync(this BookingHost host, Guid appointmentId)
    {
        await using var db = host.CreateDbContext();
        return await db.DoctorTimeBlocks.Where(b => b.AppointmentId == appointmentId && b.IsActive).Select(b => (Guid?)b.DoctorId).FirstOrDefaultAsync(TestContext.Current.CancellationToken);
    }
}
