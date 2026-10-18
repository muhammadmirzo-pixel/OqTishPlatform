using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Application.Common.Security;
using OqTish.Application.Common.Seeding;
using OqTish.Application.Identity;
using OqTish.Domain.Catalog;
using OqTish.Domain.Common;
using OqTish.Domain.Identity;
using OqTish.Domain.Scheduling;
using OqTish.Domain.Staff;
using OqTish.Domain.Tenancy;

namespace OqTish.Application.Tenancy;

/// <summary>
/// Fictional demo data (Development/Staging seed only; every name starts with "Demo"). Credentials are
/// documented in docs/SERVICES.md "Demo ma'lumotlar" and are created only when
/// <c>Seed:DemoStaffAccounts</c> is true (Development).
/// </summary>
public static class DemoData
{
    public sealed record DemoBranch(string Name, string District, string Address, decimal Latitude, decimal Longitude, string Phone);

    public sealed record DemoClinic(string Name, string LegalName, string Description, string Phone, ClinicVerificationStatus Status, decimal PriceFactor, IReadOnlyList<DemoBranch> Branches);

    public sealed record DemoStaff(string Email, string Password, string Phone, string DisplayName, StaffRole Role, bool FirstBranchOnly);

    public sealed record DemoService(string Name, string Category, decimal Price, PriceKind Kind, int Minutes);

    public static readonly IReadOnlyList<DemoClinic> Clinics =
    [
        new("Demo Tabassum Stomatologiya", "Demo Tabassum MChJ", "Demo klinika: oilaviy stomatologiya, Chilonzor.", "+998712000001", ClinicVerificationStatus.Approved, 1.00m,
        [
            new("Demo Chilonzor filiali", "Chilonzor", "Demo ko'chasi 1, Chilonzor-9", 41.275600m, 69.203400m, "+998712000011"),
            new("Demo Qatortol filiali", "Chilonzor", "Demo ko'chasi 25, Qatortol", 41.289000m, 69.219000m, "+998712000012"),
        ]),
        new("Demo Oq Marvarid Klinikasi", "Demo Oq Marvarid MChJ", "Demo klinika: ortodontiya va implantatsiya, Yunusobod.", "+998712000002", ClinicVerificationStatus.Approved, 1.20m,
        [
            new("Demo Yunusobod filiali", "Yunusobod", "Demo shoh ko'chasi 7, Yunusobod-4", 41.364000m, 69.287000m, "+998712000021"),
        ]),
        new("Demo Sog'lom Tish Markazi", "Demo Sog'lom Tish MChJ", "Demo klinika: terapiya va gigiena, Mirzo Ulug'bek.", "+998712000003", ClinicVerificationStatus.Approved, 0.90m,
        [
            new("Demo Mirzo Ulug'bek filiali", "Mirzo Ulug'bek", "Demo ko'chasi 12, Buyuk Ipak Yo'li", 41.326000m, 69.335000m, "+998712000031"),
        ]),
        new("Demo Yangi Klinika (tekshiruvda)", "Demo Yangi MChJ", "Demo klinika: platforma tekshiruvini kutmoqda.", "+998712000004", ClinicVerificationStatus.Pending, 1.00m,
        [
            new("Demo Sergeli filiali", "Sergeli", "Demo ko'chasi 3, Sergeli-6", 41.226000m, 69.219000m, "+998712000041"),
        ]),
    ];

    /// <summary>Staff of the first demo clinic (every role) — dev-only passwords.</summary>
    public static readonly IReadOnlyList<DemoStaff> FirstClinicStaff =
    [
        new("owner@demo.oqtish.local", "Demo123!owner", "+998900000011", "Demo Egasi", StaffRole.ClinicOwner, false),
        new("clinicadmin@demo.oqtish.local", "Demo123!admin", "+998900000012", "Demo Administrator", StaffRole.ClinicAdmin, false),
        new("reception@demo.oqtish.local", "Demo123!reception", "+998900000013", "Demo Qabulxona", StaffRole.Receptionist, true),
        new("doctor@demo.oqtish.local", "Demo123!doctor", "+998900000014", "Dr. Demo Aziza Karimova", StaffRole.Doctor, false),
        new("finance@demo.oqtish.local", "Demo123!finance", "+998900000015", "Demo Moliya", StaffRole.FinanceManager, false),
    ];

    /// <summary>Owner of the pending demo clinic.</summary>
    public static readonly DemoStaff PendingClinicOwner =
        new("pending@demo.oqtish.local", "Demo123!pending", "+998900000016", "Demo Yangi Egasi", StaffRole.ClinicOwner, false);

    public static readonly IReadOnlyList<DemoService> Services =
    [
        new("Demo Konsultatsiya va ko'rik", "diagnostics", 50_000m, PriceKind.Fixed, 20),
        new("Demo Professional tozalash", "hygiene", 250_000m, PriceKind.Fixed, 45),
        new("Demo Plomba qo'yish", "therapy", 300_000m, PriceKind.From, 60),
        new("Demo Tish olish", "surgery", 200_000m, PriceKind.From, 30),
        new("Demo Implant o'rnatish", "implants", 4_500_000m, PriceKind.From, 90),
        new("Demo Tish oqartirish", "whitening", 1_200_000m, PriceKind.Fixed, 60),
        new("Demo Breket tizimi", "orthodontics", 8_000_000m, PriceKind.From, 60),
    ];

    public static readonly IReadOnlyList<string> DoctorNames =
    [
        "Dr. Demo Aziza Karimova", "Dr. Demo Bobur Tursunov",
        "Dr. Demo Dilnoza Rahimova", "Dr. Demo Jasur Aliyev",
        "Dr. Demo Malika Usmonova", "Dr. Demo Sardor Qodirov",
        "Dr. Demo Nodira Xolmatova",
    ];
}

/// <summary>
/// Seed order 200: demo clinics, branches (coordinates, opening hours Mon–Fri 09–18, Sat 09–14), and — when
/// <c>Seed:DemoStaffAccounts</c> — staff accounts for every role in the first clinic plus the pending
/// clinic's owner. Idempotent: clinics are matched by name, accounts by e-mail.
/// </summary>
public sealed class TenancyDemoSeedContributor(
    IOqTishDb db,
    ITransactionRunner transactions,
    IPasswordHashService passwords,
    TimeProvider clock,
    IOptions<SeedOptions> options) : ISeedContributor
{
    public int Order => 200;

    public string Name => "tenancy-staff-demo";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var result = await transactions.RunAsync(async ct =>
        {
            var now = clock.GetUtcNow();
            foreach (var demo in DemoData.Clinics)
            {
                var clinic = await db.Clinics.FirstOrDefaultAsync(c => c.Name == demo.Name, ct);
                if (clinic is null)
                {
                    clinic = Clinic.Register(demo.Name, demo.LegalName, demo.Description, demo.Phone, null, now);
                    if (demo.Status == ClinicVerificationStatus.Approved)
                    {
                        clinic.Approve(now);
                    }

                    db.Clinics.Add(clinic);
                    foreach (var b in demo.Branches)
                    {
                        var branch = Branch.Create(clinic.Id, b.Name, b.District, b.Address, b.Latitude, b.Longitude, b.Phone, null, now);
                        db.Branches.Add(branch);
                        for (short day = 1; day <= 6; day++)
                        {
                            db.BranchOpeningHours.Add(BranchOpeningHours.Create(clinic.Id, branch.Id, day, new TimeOnly(9, 0), day == 6 ? new TimeOnly(14, 0) : new TimeOnly(18, 0)));
                        }
                    }

                    await db.SaveChangesAsync(ct);
                }
            }

            if (options.Value.DemoStaffAccounts)
            {
                var first = await db.Clinics.FirstAsync(c => c.Name == DemoData.Clinics[0].Name, ct);
                var firstBranch = await db.Branches.Where(b => b.ClinicId == first.Id).OrderBy(b => b.Name).Select(b => b.Id).FirstAsync(ct);
                foreach (var staff in DemoData.FirstClinicStaff)
                {
                    await EnsureStaffAsync(first.Id, staff, staff.FirstBranchOnly ? firstBranch : null, now, ct);
                }

                var pending = await db.Clinics.FirstAsync(c => c.Name == DemoData.Clinics[3].Name, ct);
                await EnsureStaffAsync(pending.Id, DemoData.PendingClinicOwner, null, now, ct);
            }

            return Result.Success;
        }, cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Tenancy demo seed failed: {result.Error.Code}");
        }
    }

    private async Task EnsureStaffAsync(Guid clinicId, DemoData.DemoStaff staff, Guid? branchId, DateTimeOffset now, CancellationToken ct)
    {
        var identity = await db.AuthIdentities.FirstOrDefaultAsync(i => i.Kind == AuthIdentityKind.EmailPassword && i.NormalizedIdentifier == staff.Email, ct);
        Guid userId;
        if (identity is null)
        {
            var user = User.Create(staff.DisplayName, now);
            db.Users.Add(user);
            db.AuthIdentities.Add(AuthIdentity.Create(user.Id, AuthIdentityKind.EmailPassword, staff.Email, verified: true, now));
            if (!await db.AuthIdentities.AnyAsync(i => i.Kind == AuthIdentityKind.Phone && i.NormalizedIdentifier == staff.Phone, ct))
            {
                db.AuthIdentities.Add(AuthIdentity.Create(user.Id, AuthIdentityKind.Phone, staff.Phone, verified: true, now));
            }

            db.PasswordCredentials.Add(PasswordCredential.Create(user.Id, passwords.Hash(staff.Password), now));
            userId = user.Id;
        }
        else
        {
            userId = identity.UserId;
        }

        if (!await db.ClinicMemberships.AnyAsync(m => m.ClinicId == clinicId && m.UserId == userId, ct))
        {
            var membership = ClinicMembership.Create(clinicId, userId, staff.Role, branchId is null ? MembershipScope.Clinic : MembershipScope.Branches, now);
            db.ClinicMemberships.Add(membership);
            if (branchId is { } b)
            {
                db.BranchAccess.Add(BranchAccess.Create(clinicId, membership.Id, b));
            }
        }

        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Seed order 300: services with branch prices (UZS) and durations, doctors linked to branches and services,
/// weekly working hours (Mon–Fri 09:00–13:00 and 14:00–18:00; the first doctor of a two-branch clinic
/// alternates branches by weekday). The demo doctor account is linked to the first doctor. Idempotent: a
/// clinic that already has services is left unchanged.
/// </summary>
public sealed class CatalogSchedulingDemoSeedContributor(IOqTishDb db, ITransactionRunner transactions, TimeProvider clock) : ISeedContributor
{
    public int Order => 300;

    public string Name => "catalog-scheduling-demo";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var result = await transactions.RunAsync(async ct =>
        {
            var now = clock.GetUtcNow();
            var doctorIndex = 0;
            foreach (var demo in DemoData.Clinics)
            {
                var clinic = await db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Name == demo.Name, ct);
                if (clinic is null)
                {
                    continue;
                }

                var doctorsHere = demo.Branches.Count > 1 ? 2 : demo.Status == ClinicVerificationStatus.Pending ? 1 : 2;
                if (await db.Services.AnyAsync(s => s.ClinicId == clinic.Id, ct))
                {
                    doctorIndex += doctorsHere;
                    continue;
                }

                var branches = await db.Branches.AsNoTracking().Where(b => b.ClinicId == clinic.Id).OrderBy(b => b.Name).Select(b => b.Id).ToListAsync(ct);
                var services = new List<Service>();
                for (var i = 0; i < DemoData.Services.Count; i++)
                {
                    var d = DemoData.Services[i];
                    // Orthodontics only at the second clinic; whitening not at the third.
                    if ((d.Category == "orthodontics" && demo != DemoData.Clinics[1]) || (d.Category == "whitening" && demo == DemoData.Clinics[2]))
                    {
                        continue;
                    }

                    var service = Service.Create(clinic.Id, d.Name, d.Category, true, now);
                    db.Services.Add(service);
                    services.Add(service);
                    foreach (var branchId in branches)
                    {
                        var price = Money.Uzs(decimal.Round(d.Price * demo.PriceFactor / 1000m, 0) * 1000m);
                        db.BranchServices.Add(BranchService.Create(clinic.Id, branchId, service.Id, price, d.Kind, d.Minutes, true, now));
                    }
                }

                await db.SaveChangesAsync(ct);
                for (var k = 0; k < doctorsHere; k++)
                {
                    var name = DemoData.DoctorNames[doctorIndex++ % DemoData.DoctorNames.Count];
                    var userId = name == DemoData.FirstClinicStaff[3].DisplayName
                        ? await db.AuthIdentities.Where(i => i.Kind == AuthIdentityKind.EmailPassword && i.NormalizedIdentifier == DemoData.FirstClinicStaff[3].Email).Select(i => (Guid?)i.UserId).FirstOrDefaultAsync(ct)
                        : null;
                    var doctor = Doctor.Create(clinic.Id, name, true, userId, now);
                    db.Doctors.Add(doctor);
                    var doctorBranches = k == 0 ? branches : [branches[0]];
                    foreach (var b in doctorBranches)
                    {
                        db.DoctorBranches.Add(DoctorBranch.Create(clinic.Id, doctor.Id, b));
                    }

                    var provided = k == 0 ? services.Where(s => s.Category is "diagnostics" or "hygiene" or "therapy" or "whitening" or "orthodontics") : services;
                    foreach (var s in provided)
                    {
                        db.DoctorServices.Add(DoctorService.Create(clinic.Id, doctor.Id, s.Id));
                    }

                    await db.SaveChangesAsync(ct);
                    for (short day = 1; day <= 5; day++)
                    {
                        var branch = doctorBranches[(day - 1) % doctorBranches.Count];
                        db.WorkingHours.Add(WorkingHours.Create(clinic.Id, doctor.Id, branch, day, new TimeOnly(9, 0), new TimeOnly(13, 0)));
                        db.WorkingHours.Add(WorkingHours.Create(clinic.Id, doctor.Id, branch, day, new TimeOnly(14, 0), new TimeOnly(18, 0)));
                    }
                }

                await db.SaveChangesAsync(ct);
            }

            return Result.Success;
        }, cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Catalog/scheduling demo seed failed: {result.Error.Code}");
        }
    }
}
