using System.Net;
using System.Net.Http.Json;
using OqTish.Application.Booking;
using OqTish.Domain.Catalog;
using OqTish.Domain.Common;
using OqTish.Domain.Scheduling;
using OqTish.Domain.Staff;
using OqTish.IntegrationTests.Infrastructure;
using DoctorEntity = OqTish.Domain.Scheduling.Doctor;
using ServiceEntity = OqTish.Domain.Catalog.Service;

namespace OqTish.IntegrationTests.Booking;

/// <summary>
/// HTTP scenario for booking API tests (real sign-in through the Fake SMS mailbox): clinic A with branches B1
/// and B2, a 30-minute service offered at both, doctors D1 (B1, linked to a Doctor account), D2 (B1) and
/// D3 (B2), all working 08:00–20:00 daily; staff of A: owner, B1-scoped receptionist, doctor (D1), finance
/// manager; clinic B with its own owner; patients P1 and P2.
/// </summary>
public sealed class BookingScenario
{
    public required ApiFactory Factory { get; init; }

    public required Guid ClinicA { get; init; }

    public required Guid B1 { get; init; }

    public required Guid B2 { get; init; }

    public required Guid Service { get; init; }

    public required Guid D1 { get; init; }

    public required Guid D2 { get; init; }

    public required Guid D3 { get; init; }

    public required Guid ClinicB { get; init; }

    public required Guid ClinicBBranch { get; init; }

    public required SignedInClient Owner { get; init; }

    public required SignedInClient Reception { get; init; }

    public required SignedInClient Doctor { get; init; }

    public required SignedInClient Finance { get; init; }

    public required SignedInClient OwnerB { get; init; }

    public required SignedInClient P1 { get; init; }

    public required SignedInClient P2 { get; init; }

    public required DateOnly Date { get; init; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public string A => $"/api/clinics/{ClinicA}";

    public DateTimeOffset At(int hour, int minute = 0, int days = 0) => BookingTime.ToUtc(Date.AddDays(days), new TimeSpan(hour, minute, 0), BookingWorld.Zone)!.Value;

    public static async Task<BookingScenario> CreateAsync(PostgresFixture postgres)
    {
        var factory = await ApiFactory.CreateAsync(postgres);
        var a = await TestAuth.CreateClinicAsync(factory, branches: 2);
        var b = await TestAuth.CreateClinicAsync(factory, branches: 1);
        var now = DateTimeOffset.UtcNow;
        var doctorUser = await TestAuth.SignInAsStaffAsync(factory, a.ClinicId, StaffRole.Doctor);
        Guid service, d1, d2, d3;
        await using (var db = factory.CreateDbContext())
        {
            var s = ServiceEntity.Create(a.ClinicId, "Ko'rik", "diagnostics", true, now);
            var sb = ServiceEntity.Create(b.ClinicId, "Ko'rik B", "diagnostics", true, now);
            db.Services.AddRange(s, sb);
            var doctors = new[]
            {
                DoctorEntity.Create(a.ClinicId, "Dr. Bir", true, doctorUser.Me.Id, now),
                DoctorEntity.Create(a.ClinicId, "Dr. Ikki", true, null, now),
                DoctorEntity.Create(a.ClinicId, "Dr. Uch", true, null, now),
            };
            var doctorB = DoctorEntity.Create(b.ClinicId, "Dr. B", true, null, now);
            db.Doctors.AddRange(doctors);
            db.Doctors.Add(doctorB);
            await db.SaveChangesAsync(Ct);
            foreach (var br in a.BranchIds)
            {
                db.BranchServices.Add(BranchService.Create(a.ClinicId, br, s.Id, Money.Uzs(150000), PriceKind.Fixed, 30, true, now));
            }

            db.BranchServices.Add(BranchService.Create(b.ClinicId, b.BranchIds[0], sb.Id, Money.Uzs(90000), PriceKind.Fixed, 30, true, now));
            var links = new[] { (doctors[0], a.BranchIds[0]), (doctors[1], a.BranchIds[0]), (doctors[2], a.BranchIds[1]) };
            foreach (var (d, br) in links)
            {
                db.DoctorBranches.Add(DoctorBranch.Create(a.ClinicId, d.Id, br));
                db.DoctorServices.Add(DoctorService.Create(a.ClinicId, d.Id, s.Id));
            }

            db.DoctorBranches.Add(DoctorBranch.Create(b.ClinicId, doctorB.Id, b.BranchIds[0]));
            db.DoctorServices.Add(DoctorService.Create(b.ClinicId, doctorB.Id, sb.Id));
            await db.SaveChangesAsync(Ct);
            foreach (var (d, br) in links)
            {
                for (short day = 1; day <= 7; day++)
                {
                    db.WorkingHours.Add(WorkingHours.Create(a.ClinicId, d.Id, br, day, new TimeOnly(8, 0), new TimeOnly(20, 0)));
                }
            }

            await db.SaveChangesAsync(Ct);
            (service, d1, d2, d3) = (s.Id, doctors[0].Id, doctors[1].Id, doctors[2].Id);
        }

        return new BookingScenario
        {
            Factory = factory,
            ClinicA = a.ClinicId,
            B1 = a.BranchIds[0],
            B2 = a.BranchIds[1],
            Service = service,
            D1 = d1,
            D2 = d2,
            D3 = d3,
            ClinicB = b.ClinicId,
            ClinicBBranch = b.BranchIds[0],
            Owner = await TestAuth.SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicOwner),
            Reception = await TestAuth.SignInAsStaffAsync(factory, a.ClinicId, StaffRole.Receptionist, [a.BranchIds[0]]),
            Doctor = doctorUser,
            Finance = await TestAuth.SignInAsStaffAsync(factory, a.ClinicId, StaffRole.FinanceManager),
            OwnerB = await TestAuth.SignInAsStaffAsync(factory, b.ClinicId, StaffRole.ClinicOwner),
            P1 = await TestAuth.SignInAsPatientAsync(factory),
            P2 = await TestAuth.SignInAsPatientAsync(factory),
            Date = BookingTime.LocalDate(DateTimeOffset.UtcNow, BookingWorld.Zone).AddDays(3),
        };
    }

    public static object Body(Guid branch, Guid service, DateTimeOffset start) =>
        new { branchId = branch, serviceId = service, startsAt = start, contactName = "Aziza Karimova", contactPhone = "+998 90 123-45-67" };

    /// <summary>Patient request through the API → the created DTO.</summary>
    public async Task<AppointmentDto> BookAsync(SignedInClient patient, Guid branch, DateTimeOffset start, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/appointments") { Content = JsonContent.Create(Body(branch, Service, start), options: TestAuth.Json) };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return await (await patient.Client.SendAsync(request, Ct)).ReadAsync<AppointmentDto>(HttpStatusCode.Created);
    }

    public async Task<AppointmentDto> ConfirmedAsync(SignedInClient patient, Guid branch, DateTimeOffset start, Guid doctor)
    {
        var booked = await BookAsync(patient, branch, start);
        return await (await Owner.Client.PostAsync($"{A}/appointments/{booked.Id}/confirm", new { doctorId = doctor })).ReadAsync<AppointmentDto>();
    }
}
