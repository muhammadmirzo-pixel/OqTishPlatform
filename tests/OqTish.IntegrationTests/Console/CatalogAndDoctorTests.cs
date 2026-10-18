using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OqTish.Application.Catalog;
using OqTish.Application.Scheduling;
using OqTish.Application.Staff;
using OqTish.Domain.Staff;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Infrastructure.TestAuth;

namespace OqTish.IntegrationTests.Console;

/// <summary>Services, branch offers, doctors (links, userId, Doctor-own) and working hours.</summary>
public class CatalogAndDoctorTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object Offer(string amount = "150000.00", int minutes = 30) =>
        new { price = new { amount, currency = "UZS" }, priceKind = "From", durationMinutes = minutes, isActive = true };

    private static async Task<ServiceDto> CreateServiceAsync(HttpClient owner, Guid clinicId, string name = "Plomba", string category = "therapy") =>
        await (await owner.PostAsync($"/api/clinics/{clinicId}/services", new { name, category, isActive = true })).ReadAsync<ServiceDto>(HttpStatusCode.Created);

    [Fact]
    public async Task Services_and_offers_follow_catalog_permission_and_branch_scope()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var a = await CreateClinicAsync(factory, 2);
        var b = await CreateClinicAsync(factory, 1);
        var owner = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicOwner);
        var branchAdmin = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicAdmin, [a.BranchIds[0]]);
        var receptionist = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.Receptionist);
        var otherOwner = await SignInAsStaffAsync(factory, b.ClinicId, StaffRole.ClinicOwner);

        var service = await CreateServiceAsync(owner.Client, a.ClinicId);
        var foreign = await CreateServiceAsync(otherOwner.Client, b.ClinicId);
        await AssertProblemAsync(await owner.Client.PostAsync($"/api/clinics/{a.ClinicId}/services", new { name = "X", category = "magic" }), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await receptionist.Client.PostAsync($"/api/clinics/{a.ClinicId}/services", new { name = "X", category = "therapy" }), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await branchAdmin.Client.PostAsync($"/api/clinics/{a.ClinicId}/services", new { name = "X", category = "therapy" }), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await otherOwner.Client.PutAsync($"/api/clinics/{a.ClinicId}/services/{service.Id}", new { name = "X", category = "therapy" }), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await owner.Client.PutAsync($"/api/clinics/{a.ClinicId}/services/{foreign.Id}", new { name = "X", category = "therapy" }), HttpStatusCode.NotFound, "common.not_found");
        var renamed = await (await owner.Client.PutAsync($"/api/clinics/{a.ClinicId}/services/{service.Id}", new { name = "Plomba (yorug'lik)", category = "therapy", isActive = true })).ReadAsync<ServiceDto>();
        Assert.Equal("Plomba (yorug'lik)", renamed.Name);

        var offerA0 = $"/api/clinics/{a.ClinicId}/branches/{a.BranchIds[0]}/services/{service.Id}";
        (await owner.Client.PutAsync($"/api/clinics/{a.ClinicId}/branches/{a.BranchIds[1]}/services/{service.Id}", Offer("200000"))).AssertStatus(HttpStatusCode.NoContent);
        (await branchAdmin.Client.PutAsync(offerA0, Offer())).AssertStatus(HttpStatusCode.NoContent);
        (await branchAdmin.Client.PutAsync(offerA0, Offer("175000.50", 45))).AssertStatus(HttpStatusCode.NoContent);
        await AssertProblemAsync(await branchAdmin.Client.PutAsync($"/api/clinics/{a.ClinicId}/branches/{a.BranchIds[1]}/services/{service.Id}", Offer()), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await owner.Client.PutAsync($"/api/clinics/{a.ClinicId}/branches/{a.BranchIds[0]}/services/{foreign.Id}", Offer()), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await owner.Client.PutAsync($"/api/clinics/{a.ClinicId}/branches/{b.BranchIds[0]}/services/{service.Id}", Offer()), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await owner.Client.PutAsync(offerA0, Offer(minutes: 4)), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await owner.Client.PutAsync(offerA0, Offer("-1.00")), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await owner.Client.PutAsync(offerA0, Offer("1.005")), HttpStatusCode.BadRequest, "finance.amount_invalid");

        var ownerList = await owner.Client.GetOkAsync<List<ServiceDto>>($"/api/clinics/{a.ClinicId}/services");
        Assert.Equal(2, Assert.Single(ownerList).Offers.Count);
        var scoped = Assert.Single(Assert.Single(await branchAdmin.Client.GetOkAsync<List<ServiceDto>>($"/api/clinics/{a.ClinicId}/services")).Offers);
        Assert.Equal((a.BranchIds[0], "175000.50", 45), (scoped.BranchId, scoped.Price.ToAmountString(), scoped.DurationMinutes));
        await AssertProblemAsync(await otherOwner.Client.GetAsync($"/api/clinics/{a.ClinicId}/services", Ct), HttpStatusCode.NotFound, "common.not_found");
        Assert.Equal(3, await ConsoleKit.AuditCountAsync(factory, "catalog.offer.updated", a.ClinicId));
    }

    [Fact]
    public async Task Doctors_are_managed_per_branch_scope_and_links_are_tenant_checked()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var a = await CreateClinicAsync(factory, 2);
        var b = await CreateClinicAsync(factory, 1);
        var owner = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicOwner);
        var receptionist = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.Receptionist, [a.BranchIds[0]]);
        var doctorUser = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.Doctor);
        var otherOwner = await SignInAsStaffAsync(factory, b.ClinicId, StaffRole.ClinicOwner);
        var service = await CreateServiceAsync(owner.Client, a.ClinicId);
        var foreign = await CreateServiceAsync(otherOwner.Client, b.ClinicId);
        var url = $"/api/clinics/{a.ClinicId}/doctors";

        var d1 = await (await receptionist.Client.PostAsync(url, new { displayName = "Dr. A", isActive = true, branchIds = new[] { a.BranchIds[0] }, serviceIds = new[] { service.Id } }))
            .ReadAsync<DoctorDto>(HttpStatusCode.Created);
        Assert.Equal([a.BranchIds[0]], d1.BranchIds);
        await AssertProblemAsync(await receptionist.Client.PostAsync(url, new { displayName = "Dr. B", branchIds = new[] { a.BranchIds[1] } }), HttpStatusCode.Forbidden, "common.forbidden");
        var d2 = await (await owner.Client.PostAsync(url, new { displayName = "Dr. B", branchIds = new[] { a.BranchIds[1] } })).ReadAsync<DoctorDto>(HttpStatusCode.Created);
        await AssertProblemAsync(await receptionist.Client.PutAsync($"{url}/{d2.Id}", new { displayName = "Dr. B2", branchIds = new[] { a.BranchIds[1] } }), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await doctorUser.Client.PostAsync(url, new { displayName = "Dr. C" }), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await owner.Client.PostAsync(url, new { displayName = "Dr. C", serviceIds = new[] { foreign.Id } }), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await owner.Client.PostAsync(url, new { displayName = "Dr. C", branchIds = new[] { b.BranchIds[0] } }), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await otherOwner.Client.PutAsync($"{url}/{d1.Id}", new { displayName = "x" }), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await otherOwner.Client.PutAsync($"/api/clinics/{b.ClinicId}/doctors/{d1.Id}", new { displayName = "x" }), HttpStatusCode.NotFound, "common.not_found");

        Assert.Equal(2, (await owner.Client.GetOkAsync<List<DoctorDto>>(url)).Count);
        Assert.Equal([d1.Id], (await receptionist.Client.GetOkAsync<List<DoctorDto>>(url)).Select(d => d.Id));
        await AssertProblemAsync(await otherOwner.Client.GetAsync(url, Ct), HttpStatusCode.NotFound, "common.not_found");

        // userId links grant Doctor-own access: staff.manage required, member of this clinic, once per clinic.
        await AssertProblemAsync(await receptionist.Client.PutAsync($"{url}/{d1.Id}", new { displayName = "Dr. A", branchIds = new[] { a.BranchIds[0] }, userId = doctorUser.Me.Id }),
            HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await owner.Client.PutAsync($"{url}/{d1.Id}", new { displayName = "Dr. A", branchIds = new[] { a.BranchIds[0] }, userId = otherOwner.Me.Id }),
            HttpStatusCode.UnprocessableEntity, "schedule.doctor_user_invalid");
        var linked = await (await owner.Client.PutAsync($"{url}/{d1.Id}", new { displayName = "Dr. A", isActive = true, branchIds = new[] { a.BranchIds[0] }, serviceIds = new[] { service.Id }, userId = doctorUser.Me.Id }))
            .ReadAsync<DoctorDto>();
        Assert.Equal(doctorUser.Me.Id, linked.UserId);
        await AssertProblemAsync(await owner.Client.PutAsync($"{url}/{d2.Id}", new { displayName = "Dr. B", branchIds = new[] { a.BranchIds[1] }, userId = doctorUser.Me.Id }),
            HttpStatusCode.Conflict, "schedule.doctor_user_taken");

        await using var scope = factory.Services.CreateAsyncScope();
        var access = await scope.ServiceProvider.GetRequiredService<IAccessService>().GetScopeForUserAsync(doctorUser.Me.Id, Ct);
        Assert.Equal(d1.Id, access.DoctorIdFor(a.ClinicId));
        Assert.True(access.IsOwnOnly(Permissions.AppointmentView, a.ClinicId));
        Assert.Null((await scope.ServiceProvider.GetRequiredService<IAccessService>().GetScopeForUserAsync(owner.Me.Id, Ct)).DoctorIdFor(a.ClinicId));
    }

    [Fact]
    public async Task Working_hours_never_overlap_across_branches_and_scoped_edits_keep_other_branches()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var a = await CreateClinicAsync(factory, 3);
        var owner = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicOwner);
        var receptionist = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.Receptionist, [a.BranchIds[0]]);
        var outsider = await SignInAsStaffAsync(factory, (await CreateClinicAsync(factory, 1)).ClinicId, StaffRole.ClinicOwner);
        var doctor = await (await owner.Client.PostAsync($"/api/clinics/{a.ClinicId}/doctors", new { displayName = "Dr. H", branchIds = new[] { a.BranchIds[0], a.BranchIds[1] } }))
            .ReadAsync<DoctorDto>(HttpStatusCode.Created);
        var url = $"/api/clinics/{a.ClinicId}/doctors/{doctor.Id}/working-hours";

        object[] week =
        [
            new { branchId = a.BranchIds[0], weekday = 1, start = "09:00", end = "13:00" },
            new { branchId = a.BranchIds[1], weekday = 1, start = "14:00", end = "18:00" },
            new { branchId = a.BranchIds[1], weekday = 2, start = "09:00", end = "18:00" },
        ];
        (await owner.Client.PutAsync(url, week)).AssertStatus(HttpStatusCode.NoContent);
        Assert.Equal(3, (await owner.Client.GetOkAsync<List<WorkingHoursEntryDto>>(url)).Count);
        Assert.Single(await receptionist.Client.GetOkAsync<List<WorkingHoursEntryDto>>(url));
        await AssertProblemAsync(await outsider.Client.GetAsync(url, Ct), HttpStatusCode.NotFound, "common.not_found");

        await AssertProblemAsync(await owner.Client.PutAsync(url, new object[]
        {
            new { branchId = a.BranchIds[0], weekday = 3, start = "09:00", end = "13:00" },
            new { branchId = a.BranchIds[1], weekday = 3, start = "12:30", end = "18:00" },
        }), HttpStatusCode.UnprocessableEntity, "schedule.hours_overlap");
        await AssertProblemAsync(await owner.Client.PutAsync(url, new object[] { new { branchId = a.BranchIds[2], weekday = 1, start = "09:00", end = "10:00" } }),
            HttpStatusCode.UnprocessableEntity, "schedule.branch_not_linked");
        await AssertProblemAsync(await owner.Client.PutAsync(url, new object[] { new { branchId = a.BranchIds[0], weekday = 1, start = "10:00", end = "09:00" } }),
            HttpStatusCode.BadRequest, "common.validation");

        // Receptionist of branch 0: may not touch branch 1, replaces only branch 0, and overlap with kept branch-1 hours is refused.
        await AssertProblemAsync(await receptionist.Client.PutAsync(url, new object[] { new { branchId = a.BranchIds[1], weekday = 4, start = "09:00", end = "10:00" } }),
            HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await receptionist.Client.PutAsync(url, new object[] { new { branchId = a.BranchIds[0], weekday = 2, start = "08:00", end = "10:00" } }),
            HttpStatusCode.UnprocessableEntity, "schedule.hours_overlap");
        (await receptionist.Client.PutAsync(url, new object[] { new { branchId = a.BranchIds[0], weekday = 5, start = "08:00", end = "12:00" } })).AssertStatus(HttpStatusCode.NoContent);
        var after = await owner.Client.GetOkAsync<List<WorkingHoursEntryDto>>(url);
        Assert.Equal(["1 14:00 B1", "2 09:00 B1", "5 08:00 B0"], after.Select(e => $"{e.Weekday} {e.Start} B{a.BranchIds.ToList().IndexOf(e.BranchId)}"));

        // Unlinking branch 1 removes its hours.
        (await owner.Client.PutAsync($"/api/clinics/{a.ClinicId}/doctors/{doctor.Id}", new { displayName = "Dr. H", branchIds = new[] { a.BranchIds[0] } })).AssertStatus(HttpStatusCode.OK);
        Assert.Single(await owner.Client.GetOkAsync<List<WorkingHoursEntryDto>>(url));
        Assert.Equal(2, await ConsoleKit.AuditCountAsync(factory, "schedule.working_hours.updated", a.ClinicId));
    }

    [Fact]
    public async Task Database_rejects_overlapping_hours_and_cross_clinic_links()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var a = await CreateClinicAsync(factory, 2);
        var b = await CreateClinicAsync(factory, 1);
        var owner = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicOwner);
        var doctor = await (await owner.Client.PostAsync($"/api/clinics/{a.ClinicId}/doctors", new { displayName = "Dr. DB", branchIds = a.BranchIds })).ReadAsync<DoctorDto>(HttpStatusCode.Created);

        await using var db = factory.CreateDbContext();
        await db.Database.ExecuteSqlAsync($"INSERT INTO scheduling.working_hours (id, clinic_id, doctor_id, branch_id, weekday, start_local, end_local) VALUES ({Guid.CreateVersion7()}, {a.ClinicId}, {doctor.Id}, {a.BranchIds[0]}, 1, '09:00', '13:00')", Ct);
        var overlap = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"INSERT INTO scheduling.working_hours (id, clinic_id, doctor_id, branch_id, weekday, start_local, end_local) VALUES ({Guid.CreateVersion7()}, {a.ClinicId}, {doctor.Id}, {a.BranchIds[1]}, 1, '12:00', '14:00')", Ct));
        Assert.Equal(("23P01", "ex_working_hours_no_overlap"), (overlap.SqlState, overlap.ConstraintName));
        // Adjacent [13:00, 14:00) is fine.
        await db.Database.ExecuteSqlAsync($"INSERT INTO scheduling.working_hours (id, clinic_id, doctor_id, branch_id, weekday, start_local, end_local) VALUES ({Guid.CreateVersion7()}, {a.ClinicId}, {doctor.Id}, {a.BranchIds[1]}, 1, '13:00', '14:00')", Ct);

        var crossBranch = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"INSERT INTO scheduling.doctor_branches (clinic_id, doctor_id, branch_id) VALUES ({a.ClinicId}, {doctor.Id}, {b.BranchIds[0]})", Ct));
        Assert.Equal("23503", crossBranch.SqlState);
        var crossOffer = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"INSERT INTO catalog.branch_services (clinic_id, branch_id, service_id, price, currency, price_kind, duration_minutes, is_active, created_at, updated_at) VALUES ({b.ClinicId}, {a.BranchIds[0]}, {Guid.CreateVersion7()}, 1, 'UZS', 'Fixed', 30, true, now(), now())", Ct));
        Assert.Equal("23503", crossOffer.SqlState);
    }
}
