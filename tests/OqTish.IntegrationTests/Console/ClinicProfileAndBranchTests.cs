using System.Net;
using Microsoft.EntityFrameworkCore;
using OqTish.Application.Audit;
using OqTish.Application.Common;
using OqTish.Application.Identity;
using OqTish.Application.Tenancy;
using OqTish.Domain.Staff;
using OqTish.Domain.Tenancy;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Infrastructure.TestAuth;

namespace OqTish.IntegrationTests.Console;

/// <summary>Clinic registration, profile, dashboard, branches + hours, clinic audit (docs/API.md §4).</summary>
public class ClinicProfileAndBranchTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly object Profile = new { name = "Yangi klinika", legalName = "Yangi MChJ", description = "Tavsif", phone = "+998 71 200 00 00" };

    private static object BranchBody(string name = "Yangi filial", decimal? lat = 41.3m, decimal? lng = 69.2m) =>
        new { name, district = "Yunusobod", address = "Ko'cha 1", latitude = lat, longitude = lng, phone = (string?)null, timeZone = "Asia/Tashkent", isActive = true };

    [Fact]
    public async Task Registering_makes_the_caller_owner_of_a_pending_clinic_hidden_from_the_public()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var user = await SignInAsPatientAsync(factory);
        var created = await (await user.Client.PostAsync("/api/clinics", Profile)).ReadAsync<ClinicProfileDto>(HttpStatusCode.Created);
        Assert.Equal(ClinicVerificationStatus.Pending, created.VerificationStatus);
        Assert.Equal("+998712000000", created.Phone);

        var me = await user.Client.GetOkAsync<MeDto>("/api/me");
        var membership = Assert.Single(me.Memberships);
        Assert.Equal((created.Id, StaffRole.ClinicOwner, MembershipScope.Clinic), (membership.ClinicId, membership.Role, membership.Scope));
        Assert.Equal(created.Name, (await user.Client.GetOkAsync<ClinicProfileDto>($"/api/clinics/{created.Id}")).Name);

        var anonymous = factory.CreateApiClient();
        (await anonymous.GetAsync($"/api/public/clinics/{created.Id}", Ct)).AssertStatus(HttpStatusCode.NotFound);
        Assert.Equal(1, await ConsoleKit.AuditCountAsync(factory, "clinic.registered", created.Id));
    }

    [Fact]
    public async Task Registration_is_validated_and_limited_per_user()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var user = await SignInAsPatientAsync(factory);
        await AssertProblemAsync(await user.Client.PostAsync("/api/clinics", new { name = " " }), HttpStatusCode.BadRequest, "common.validation");
        for (var i = 0; i < 3; i++)
        {
            (await user.Client.PostAsync("/api/clinics", Profile)).AssertStatus(HttpStatusCode.Created);
        }

        await AssertProblemAsync(await user.Client.PostAsync("/api/clinics", Profile), HttpStatusCode.UnprocessableEntity, "clinic.registration_limit");
    }

    [Fact]
    public async Task Profile_read_needs_membership_and_edit_needs_clinic_wide_profile_permission()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var a = await CreateClinicAsync(factory, 2);
        var b = await CreateClinicAsync(factory, 1);
        var owner = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicOwner);
        var receptionist = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.Receptionist);
        var branchAdmin = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicAdmin, [a.BranchIds[0]]);
        var otherOwner = await SignInAsStaffAsync(factory, b.ClinicId, StaffRole.ClinicOwner);

        (await receptionist.Client.GetAsync($"/api/clinics/{a.ClinicId}", Ct)).AssertStatus(HttpStatusCode.OK);
        await AssertProblemAsync(await otherOwner.Client.GetAsync($"/api/clinics/{a.ClinicId}", Ct), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await otherOwner.Client.PutAsync($"/api/clinics/{a.ClinicId}", Profile), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await receptionist.Client.PutAsync($"/api/clinics/{a.ClinicId}", Profile), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await branchAdmin.Client.PutAsync($"/api/clinics/{a.ClinicId}", Profile), HttpStatusCode.Forbidden, "common.forbidden");

        var updated = await (await owner.Client.PutAsync($"/api/clinics/{a.ClinicId}", Profile)).ReadAsync<ClinicProfileDto>();
        Assert.Equal("Yangi klinika", updated.Name);
        Assert.Equal(1, await ConsoleKit.AuditCountAsync(factory, "clinic.profile.updated", a.ClinicId));
    }

    [Fact]
    public async Task Dashboard_counts_respect_branch_scope_and_hide_money_without_finance_permission()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var a = await CreateClinicAsync(factory, 2);
        var b = await CreateClinicAsync(factory, 1);
        var owner = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicOwner);
        var receptionist = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.Receptionist, [a.BranchIds[0]]);

        var ownerView = await owner.Client.GetOkAsync<DashboardDto>($"/api/clinics/{a.ClinicId}/dashboard");
        Assert.Equal((0, 0, 0, 0), (ownerView.Pending, ownerView.Confirmed, ownerView.Completed, ownerView.NoShow));
        Assert.Equal("0.00", ownerView.Collected!.Value.ToAmountString());

        var receptionView = await receptionist.Client.GetOkAsync<DashboardDto>($"/api/clinics/{a.ClinicId}/dashboard?branchId={a.BranchIds[0]}");
        Assert.Null(receptionView.Collected);
        Assert.Equal(a.BranchIds[0], receptionView.BranchId);
        await AssertProblemAsync(await receptionist.Client.GetAsync($"/api/clinics/{a.ClinicId}/dashboard?branchId={a.BranchIds[1]}", Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await receptionist.Client.GetAsync($"/api/clinics/{a.ClinicId}/dashboard?branchId={b.BranchIds[0]}", Ct), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await receptionist.Client.GetAsync($"/api/clinics/{b.ClinicId}/dashboard", Ct), HttpStatusCode.NotFound, "common.not_found");
    }

    [Fact]
    public async Task Branch_list_is_filtered_by_scope_and_writes_check_the_branch()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var a = await CreateClinicAsync(factory, 2);
        var b = await CreateClinicAsync(factory, 1);
        var owner = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicOwner);
        var branchAdmin = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicAdmin, [a.BranchIds[0]]);
        var receptionist = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.Receptionist);
        var otherOwner = await SignInAsStaffAsync(factory, b.ClinicId, StaffRole.ClinicOwner);

        Assert.Equal(2, (await owner.Client.GetOkAsync<List<BranchDto>>($"/api/clinics/{a.ClinicId}/branches")).Count);
        Assert.Equal([a.BranchIds[0]], (await branchAdmin.Client.GetOkAsync<List<BranchDto>>($"/api/clinics/{a.ClinicId}/branches")).Select(x => x.Id));
        await AssertProblemAsync(await otherOwner.Client.GetAsync($"/api/clinics/{a.ClinicId}/branches", Ct), HttpStatusCode.NotFound, "common.not_found");

        var created = await (await owner.Client.PostAsync($"/api/clinics/{a.ClinicId}/branches", BranchBody())).ReadAsync<BranchDto>(HttpStatusCode.Created);
        Assert.Equal(41.3m, created.Latitude);
        await AssertProblemAsync(await branchAdmin.Client.PostAsync($"/api/clinics/{a.ClinicId}/branches", BranchBody()), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await receptionist.Client.PostAsync($"/api/clinics/{a.ClinicId}/branches", BranchBody()), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await otherOwner.Client.PostAsync($"/api/clinics/{a.ClinicId}/branches", BranchBody()), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await owner.Client.PostAsync($"/api/clinics/{a.ClinicId}/branches", BranchBody(lng: null)), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await owner.Client.PostAsync($"/api/clinics/{a.ClinicId}/branches",
            new { name = "x", district = "y", address = "z", timeZone = "Europe/Moscow" }), HttpStatusCode.BadRequest, "common.validation");

        (await branchAdmin.Client.PutAsync($"/api/clinics/{a.ClinicId}/branches/{a.BranchIds[0]}", BranchBody("Tahrirlangan"))).AssertStatus(HttpStatusCode.OK);
        await AssertProblemAsync(await branchAdmin.Client.PutAsync($"/api/clinics/{a.ClinicId}/branches/{a.BranchIds[1]}", BranchBody()), HttpStatusCode.Forbidden, "common.forbidden");
        // Cross-clinic id substitution: clinic B's branch under clinic A's route, and clinic A's branch for clinic B's owner.
        await AssertProblemAsync(await owner.Client.PutAsync($"/api/clinics/{a.ClinicId}/branches/{b.BranchIds[0]}", BranchBody()), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await otherOwner.Client.PutAsync($"/api/clinics/{b.ClinicId}/branches/{a.BranchIds[0]}", BranchBody()), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await otherOwner.Client.PutAsync($"/api/clinics/{a.ClinicId}/branches/{a.BranchIds[0]}", BranchBody()), HttpStatusCode.NotFound, "common.not_found");
    }

    [Fact]
    public async Task Branch_hours_are_replaced_and_validated()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var a = await CreateClinicAsync(factory, 2);
        var owner = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicOwner);
        var receptionist = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.Receptionist);
        var url = $"/api/clinics/{a.ClinicId}/branches/{a.BranchIds[0]}/hours";
        object[] hours = [new { weekday = 1, open = "09:00", close = "13:00" }, new { weekday = 1, open = "14:00", close = "18:00" }, new { weekday = 6, open = "10:00", close = "14:00" }];

        (await owner.Client.PutAsync(url, hours)).AssertStatus(HttpStatusCode.NoContent);
        (await owner.Client.PutAsync(url, hours)).AssertStatus(HttpStatusCode.NoContent);
        var branch = (await owner.Client.GetOkAsync<List<BranchDto>>($"/api/clinics/{a.ClinicId}/branches")).Single(x => x.Id == a.BranchIds[0]);
        Assert.Equal(["1 09:00-13:00", "1 14:00-18:00", "6 10:00-14:00"], branch.Hours.Select(h => $"{h.Weekday} {h.Open}-{h.Close}"));

        await AssertProblemAsync(await owner.Client.PutAsync(url, new object[] { new { weekday = 1, open = "09:00", close = "13:00" }, new { weekday = 1, open = "12:00", close = "15:00" } }), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await owner.Client.PutAsync(url, new object[] { new { weekday = 8, open = "09:00", close = "13:00" } }), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await owner.Client.PutAsync(url, new object[] { new { weekday = 2, open = "9:00", close = "13:00" } }), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await receptionist.Client.PutAsync(url, hours), HttpStatusCode.Forbidden, "common.forbidden");
    }

    [Fact]
    public async Task Clinic_audit_log_is_owner_only_and_tenant_scoped()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var a = await CreateClinicAsync(factory, 1);
        var b = await CreateClinicAsync(factory, 1);
        var owner = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicOwner);
        var admin = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicAdmin);
        var otherOwner = await SignInAsStaffAsync(factory, b.ClinicId, StaffRole.ClinicOwner);
        (await owner.Client.PutAsync($"/api/clinics/{a.ClinicId}", Profile)).AssertStatus(HttpStatusCode.OK);
        (await otherOwner.Client.PutAsync($"/api/clinics/{b.ClinicId}", Profile)).AssertStatus(HttpStatusCode.OK);

        var page = await owner.Client.GetOkAsync<Page<AuditEntryDto>>($"/api/clinics/{a.ClinicId}/audit?page=1&pageSize=10");
        var entry = Assert.Single(page.Items);
        Assert.Equal(("clinic.profile.updated", owner.Me.Id), (entry.Action, entry.ActorUserId!.Value));
        await AssertProblemAsync(await admin.Client.GetAsync($"/api/clinics/{a.ClinicId}/audit", Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await otherOwner.Client.GetAsync($"/api/clinics/{a.ClinicId}/audit", Ct), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await owner.Client.GetAsync($"/api/clinics/{a.ClinicId}/audit?pageSize=500", Ct), HttpStatusCode.BadRequest, "common.validation");
    }
}
