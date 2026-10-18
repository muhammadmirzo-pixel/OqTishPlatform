using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OqTish.Api.Seeding;
using OqTish.Application.Identity;
using OqTish.Application.Staff;
using OqTish.Domain.Identity;
using OqTish.Domain.Staff;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Infrastructure.TestAuth;

namespace OqTish.IntegrationTests.Identity;

public class MeAndAccountTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Profile_update_sets_patient_name()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var p = await SignInAsPatientAsync(factory);
        var response = await p.Client.PutAsJsonAsync("/api/me/profile", new { fullName = "  Dilnoza Karimova " }, Ct);
        var me = (await response.Content.ReadFromJsonAsync<MeDto>(Json, Ct))!;
        Assert.Equal("Dilnoza Karimova", me.Patient!.FullName);
        Assert.Equal("Dilnoza Karimova", me.DisplayName);
        await AssertProblemAsync(await p.Client.PutAsJsonAsync("/api/me/profile", new { fullName = "" }, Ct), HttpStatusCode.BadRequest, "common.validation");
    }

    [Fact]
    public async Task Delete_anonymises_and_signs_out_but_staff_must_leave_first()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, settings: new Dictionary<string, string?> { ["Auth:Otp:ResendCooldownSeconds"] = "0" });
        var p = await SignInAsPatientAsync(factory);
        await p.Client.PutAsJsonAsync("/api/me/profile", new { fullName = "Delete Me" }, Ct);
        var deleted = await p.Client.PostAsync("/api/me/delete", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await p.Client.GetAsync("/api/me", Ct)).StatusCode);

        await using (var db = factory.CreateDbContext())
        {
            var user = await db.Users.SingleAsync(u => u.Id == p.Me.Id, Ct);
            Assert.Equal(UserStatus.Deleted, user.Status);
            Assert.False(await db.AuthIdentities.AnyAsync(i => i.UserId == p.Me.Id, Ct));
            var profile = await db.PatientProfiles.SingleAsync(x => x.UserId == p.Me.Id, Ct);
            Assert.True(profile.IsAnonymised);
            Assert.Null(profile.ContactPhone);
        }

        // The phone is free again: signing in creates a NEW account.
        var again = await SignInAsPatientAsync(factory, p.Phone);
        Assert.NotEqual(p.Me.Id, again.Me.Id);

        var clinic = await CreateClinicAsync(factory, 1);
        var staff = await SignInAsStaffAsync(factory, clinic.ClinicId, StaffRole.Receptionist);
        await AssertProblemAsync(await staff.Client.PostAsync("/api/me/delete", null, Ct), HttpStatusCode.UnprocessableEntity, "account.delete_blocked");
    }

    [Fact]
    public async Task Me_lists_effective_permissions_per_membership()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var clinic = await CreateClinicAsync(factory, 2);
        var staff = await SignInAsStaffAsync(factory, clinic.ClinicId, StaffRole.Receptionist, [clinic.BranchIds[0]]);
        var me = (await staff.Client.GetFromJsonAsync<MeDto>("/api/me", Json, Ct))!;
        var membership = Assert.Single(me.Memberships);
        Assert.Equal(StaffRole.Receptionist, membership.Role);
        Assert.Equal(MembershipScope.Branches, membership.Scope);
        Assert.Equal([clinic.BranchIds[0]], membership.BranchIds);
        Assert.Equal(Permissions.For(StaffRole.Receptionist).Keys.Order(StringComparer.Ordinal), membership.Permissions.Order(StringComparer.Ordinal));
        Assert.Empty(me.PlatformPermissions);
    }

    [Fact]
    public async Task Seed_is_idempotent_and_admin_can_sign_in()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var client = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/dev/seed", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/dev/seed", null, Ct)).StatusCode);

        await using (var db = factory.CreateDbContext())
        {
            Assert.Equal(1, await db.AuthIdentities.CountAsync(i => i.NormalizedIdentifier == "admin@oqtish.local", Ct));
            Assert.Equal(1, await db.AuthIdentities.CountAsync(i => i.NormalizedIdentifier == "+998900000001", Ct));
            Assert.Equal(PlatformPermissions.All.Count, await db.PlatformAdminGrants.CountAsync(Ct));
        }

        var login = await client.PostAsJsonAsync("/api/auth/password/login", new { email = "admin@oqtish.local", password = "Admin123!demo" }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var me = (await login.Content.ReadFromJsonAsync<MeDto>(Json, Ct))!;
        Assert.Equal(PlatformPermissions.All, me.PlatformPermissions);
    }

    [Fact]
    public async Task Seed_command_refuses_production()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        _ = factory.CreateApiClient();
        var env = new FakeEnvironment { EnvironmentName = Environments.Production };
        Assert.Equal(2, await SeedCommand.RunAsync(factory.Services, env, NullLogger.Instance, Ct));
        Assert.False(SeedCommand.IsAllowed(env));
        Assert.True(SeedCommand.IsAllowed(new FakeEnvironment { EnvironmentName = Environments.Staging }));
        Assert.Equal(0, await SeedCommand.RunAsync(factory.Services, new FakeEnvironment { EnvironmentName = Environments.Development }, NullLogger.Instance, Ct));
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "test";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
