using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OqTish.Application.Identity;
using OqTish.Application.Staff;
using OqTish.Application.Tenancy;
using OqTish.Domain.Staff;
using OqTish.IntegrationTests.Infrastructure;

namespace OqTish.IntegrationTests.Console;

/// <summary>Demo seed contributors: idempotent, every role can sign in, doctor account linked.</summary>
public class DemoSeedTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<int[]> CountsAsync(ApiFactory factory)
    {
        await using var db = factory.CreateDbContext();
        return
        [
            await db.Clinics.CountAsync(Ct), await db.Branches.CountAsync(Ct), await db.BranchOpeningHours.CountAsync(Ct),
            await db.Services.CountAsync(Ct), await db.BranchServices.CountAsync(Ct), await db.Doctors.CountAsync(Ct),
            await db.DoctorBranches.CountAsync(Ct), await db.DoctorServices.CountAsync(Ct), await db.WorkingHours.CountAsync(Ct),
            await db.ClinicMemberships.CountAsync(Ct), await db.Users.CountAsync(Ct), await db.AuthIdentities.CountAsync(Ct),
            await db.Appointments.CountAsync(Ct), await db.AppointmentStatusHistory.CountAsync(Ct), await db.DoctorTimeBlocks.CountAsync(Ct),
            await db.Reviews.CountAsync(Ct), await db.AbuseReports.CountAsync(Ct),
        ];
    }

    [Fact]
    public async Task Seed_twice_gives_the_same_counts_and_demo_staff_can_sign_in()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateApiClient();
        (await client.PostAsync("/api/dev/seed", null, Ct)).AssertStatus(HttpStatusCode.OK);
        var first = await CountsAsync(factory);
        (await client.PostAsync("/api/dev/seed", null, Ct)).AssertStatus(HttpStatusCode.OK);
        Assert.Equal(first, await CountsAsync(factory));
        Assert.Equal([4, 5, 30, 24, 30, 7], first[..6]);
        Assert.Equal(6, first[9]);
        Assert.Equal([10, 3, 2, 1], [first[12], first[14], first[15], first[16]]); // BE2: appointments, active blocks, reviews, reports
        await using (var check = factory.CreateDbContext())
        {
            var statuses = await check.Appointments.Select(a => a.Status).Distinct().CountAsync(Ct);
            Assert.Equal(8, statuses); // every status is represented
        }

        foreach (var staff in DemoData.FirstClinicStaff.Append(DemoData.PendingClinicOwner))
        {
            var c = factory.CreateApiClient();
            var login = await c.PostAsJsonAsync("/api/auth/password/login", new { email = staff.Email, password = staff.Password }, Ct);
            var me = await login.ReadAsync<MeDto>();
            Assert.Equal(staff.Role, Assert.Single(me.Memberships).Role);
        }

        await using var db = factory.CreateDbContext();
        var doctorUser = await db.AuthIdentities.Where(i => i.NormalizedIdentifier == "doctor@demo.oqtish.local").Select(i => i.UserId).SingleAsync(Ct);
        var clinic = await db.Clinics.SingleAsync(c => c.Name == DemoData.Clinics[0].Name, Ct);
        await using var scope = factory.Services.CreateAsyncScope();
        var access = await scope.ServiceProvider.GetRequiredService<IAccessService>().GetScopeForUserAsync(doctorUser, Ct);
        var doctorId = await db.Doctors.Where(d => d.UserId == doctorUser).Select(d => d.Id).SingleAsync(Ct);
        Assert.Equal(doctorId, access.DoctorIdFor(clinic.Id));
        Assert.Equal(MembershipScope.Branches, (await db.ClinicMemberships.SingleAsync(m => m.Role == StaffRole.Receptionist, Ct)).Scope);
    }
}
