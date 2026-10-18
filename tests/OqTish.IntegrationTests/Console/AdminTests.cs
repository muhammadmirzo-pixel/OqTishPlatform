using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OqTish.Api.Seeding;
using OqTish.Application.Admin;
using OqTish.Application.Audit;
using OqTish.Application.Common;
using OqTish.Application.Identity;
using OqTish.Application.Staff;
using OqTish.Domain.Identity;
using OqTish.Domain.Staff;
using OqTish.Domain.Tenancy;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Infrastructure.TestAuth;

namespace OqTish.IntegrationTests.Console;

/// <summary>Platform admin endpoints (docs/API.md §5) and the create-platform-admin bootstrap CLI.</summary>
public class AdminTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Dashboard_needs_any_platform_permission()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        await CreateClinicAsync(factory, 1, ClinicVerificationStatus.Pending);
        await CreateClinicAsync(factory, 1);
        var auditor = await ConsoleKit.SignInAsPlatformAdminAsync(factory, PlatformPermissions.AuditView);
        var patient = await SignInAsPatientAsync(factory);

        var dto = await auditor.Client.GetOkAsync<AdminDashboardDto>("/api/admin/dashboard");
        Assert.Equal((1, 1, 0, 0), (dto.ClinicsByStatus["Pending"], dto.ClinicsByStatus["Approved"], dto.ClinicsByStatus["Suspended"], dto.AppointmentsToday));
        Assert.True(dto.ActiveUsers >= 2);
        await AssertProblemAsync(await patient.Client.GetAsync("/api/admin/dashboard", Ct), HttpStatusCode.Forbidden, "common.forbidden");
    }

    [Fact]
    public async Task Clinic_verification_is_audited_and_permission_split()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var pending = await CreateClinicAsync(factory, 1, ClinicVerificationStatus.Pending);
        var verifier = await ConsoleKit.SignInAsPlatformAdminAsync(factory, PlatformPermissions.ClinicVerify);
        var suspender = await ConsoleKit.SignInAsPlatformAdminAsync(factory, PlatformPermissions.AccountSuspend);
        var owner = await SignInAsStaffAsync(factory, pending.ClinicId, StaffRole.ClinicOwner);
        var url = $"/api/admin/clinics/{pending.ClinicId}";

        var list = await verifier.Client.GetOkAsync<Page<AdminClinicDto>>("/api/admin/clinics?status=Pending");
        Assert.Equal(pending.ClinicId, Assert.Single(list.Items).Id);
        await AssertProblemAsync(await suspender.Client.GetAsync("/api/admin/clinics", Ct), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await owner.Client.PostAsync($"{url}/approve", (object?)null), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await suspender.Client.PostAsync($"{url}/approve", (object?)null), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await verifier.Client.PostAsync($"{url}/reject", new { reason = "" }), HttpStatusCode.BadRequest, "common.validation");
        await AssertProblemAsync(await verifier.Client.PostAsync($"/api/admin/clinics/{Guid.NewGuid()}/approve", (object?)null), HttpStatusCode.NotFound, "common.not_found");
        await AssertProblemAsync(await verifier.Client.PostAsync($"{url}/suspend", new { reason = "x" }), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await suspender.Client.PostAsync($"{url}/suspend", new { reason = "x" }), HttpStatusCode.Conflict, "admin.invalid_status_transition");

        var rejected = await (await verifier.Client.PostAsync($"{url}/reject", new { reason = "Hujjatlar yetarli emas" })).ReadAsync<AdminClinicDto>();
        Assert.Equal((ClinicVerificationStatus.Rejected, "Hujjatlar yetarli emas"), (rejected.VerificationStatus, rejected.StatusReason));
        var approved = await (await verifier.Client.PostAsync($"{url}/approve", (object?)null)).ReadAsync<AdminClinicDto>();
        Assert.Equal(ClinicVerificationStatus.Approved, approved.VerificationStatus);
        await AssertProblemAsync(await verifier.Client.PostAsync($"{url}/approve", (object?)null), HttpStatusCode.Conflict, "admin.invalid_status_transition");
        (await factory.CreateApiClient().GetAsync($"/api/public/clinics/{pending.ClinicId}", Ct)).AssertStatus(HttpStatusCode.OK);

        var suspended = await (await suspender.Client.PostAsync($"{url}/suspend", new { reason = "Shikoyat" })).ReadAsync<AdminClinicDto>();
        Assert.True(suspended.IsSuspended);
        await AssertProblemAsync(await owner.Client.GetAsync($"/api/clinics/{pending.ClinicId}", Ct), HttpStatusCode.NotFound, "common.not_found");
        Assert.DoesNotContain((await owner.Client.GetOkAsync<MeDto>("/api/me")).Memberships, m => m.ClinicId == pending.ClinicId);
        (await suspender.Client.PostAsync($"{url}/reinstate", (object?)null)).AssertStatus(HttpStatusCode.OK);
        (await owner.Client.GetAsync($"/api/clinics/{pending.ClinicId}", Ct)).AssertStatus(HttpStatusCode.OK);

        foreach (var action in new[] { "admin.clinic.rejected", "admin.clinic.approved", "admin.clinic.suspended", "admin.clinic.reinstated" })
        {
            Assert.Equal(1, await ConsoleKit.AuditCountAsync(factory, action, pending.ClinicId));
        }
    }

    [Fact]
    public async Task Platform_permissions_require_totp_when_the_rule_is_on()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, settings: new Dictionary<string, string?> { ["Security:RequireTotpForPrivilegedRoles"] = "true" });
        var admin = await ConsoleKit.SignInAsPlatformAdminAsync(factory);
        await AssertProblemAsync(await admin.Client.GetAsync("/api/admin/clinics", Ct), HttpStatusCode.Forbidden, "auth.totp_setup_required");
        await AssertProblemAsync(await admin.Client.GetAsync("/api/admin/dashboard", Ct), HttpStatusCode.Forbidden, "auth.totp_setup_required");
        await EnableTotpAsync(admin.Client);
        (await admin.Client.GetAsync("/api/admin/clinics", Ct)).AssertStatus(HttpStatusCode.OK);
    }

    [Fact]
    public async Task User_suspension_revokes_sessions_and_is_reversible()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var admin = await ConsoleKit.SignInAsPlatformAdminAsync(factory, PlatformPermissions.AccountSuspend);
        var auditor = await ConsoleKit.SignInAsPlatformAdminAsync(factory, PlatformPermissions.AuditView);
        var victim = await SignInAsPatientAsync(factory);

        var found = await admin.Client.GetOkAsync<Page<AdminUserDto>>($"/api/admin/users?q={Uri.EscapeDataString(victim.Phone)}");
        Assert.Equal((victim.Me.Id, PhoneNumber.Mask(victim.Phone)), (Assert.Single(found.Items).Id, found.Items[0].PhoneMasked));
        await AssertProblemAsync(await auditor.Client.PostAsync($"/api/admin/users/{victim.Me.Id}/suspend", new { reason = "x" }), HttpStatusCode.Forbidden, "common.forbidden");
        await AssertProblemAsync(await admin.Client.PostAsync($"/api/admin/users/{admin.Me.Id}/suspend", new { reason = "x" }), HttpStatusCode.UnprocessableEntity, "admin.cannot_suspend_self");
        await AssertProblemAsync(await admin.Client.PostAsync($"/api/admin/users/{Guid.NewGuid()}/suspend", new { reason = "x" }), HttpStatusCode.NotFound, "common.not_found");

        (await admin.Client.PostAsync($"/api/admin/users/{victim.Me.Id}/suspend", new { reason = "Spam" })).AssertStatus(HttpStatusCode.NoContent);
        await AssertProblemAsync(await victim.Client.GetAsync("/api/me", Ct), HttpStatusCode.Unauthorized, "auth.unauthenticated");
        await using (var db = factory.CreateDbContext())
        {
            Assert.False(await db.Sessions.AnyAsync(s => s.UserId == victim.Me.Id && s.RevokedAt == null, Ct));
            Assert.Equal(UserStatus.Suspended, (await db.Users.SingleAsync(u => u.Id == victim.Me.Id, Ct)).Status);
        }

        await AssertProblemAsync(await admin.Client.PostAsync($"/api/admin/users/{victim.Me.Id}/suspend", new { reason = "Spam" }), HttpStatusCode.Conflict, "admin.invalid_status_transition");
        (await admin.Client.PostAsync($"/api/admin/users/{victim.Me.Id}/reinstate", (object?)null)).AssertStatus(HttpStatusCode.NoContent);
        await using (var db = factory.CreateDbContext())
        {
            Assert.Equal(UserStatus.Active, (await db.Users.SingleAsync(u => u.Id == victim.Me.Id, Ct)).Status);
        }

        var audit = await auditor.Client.GetOkAsync<Page<AuditEntryDto>>("/api/admin/audit?action=admin.user.");
        Assert.Equal(["admin.user.reinstated", "admin.user.suspended"], audit.Items.Select(a => a.Action));
        Assert.Equal(admin.Me.Id, audit.Items[0].ActorUserId);
        await AssertProblemAsync(await admin.Client.GetAsync("/api/admin/audit", Ct), HttpStatusCode.Forbidden, "common.forbidden");
    }

    [Fact]
    public async Task Admin_audit_filters_by_clinic()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var a = await CreateClinicAsync(factory, 1, ClinicVerificationStatus.Pending);
        var b = await CreateClinicAsync(factory, 1, ClinicVerificationStatus.Pending);
        var admin = await ConsoleKit.SignInAsPlatformAdminAsync(factory);
        (await admin.Client.PostAsync($"/api/admin/clinics/{a.ClinicId}/approve", (object?)null)).AssertStatus(HttpStatusCode.OK);
        (await admin.Client.PostAsync($"/api/admin/clinics/{b.ClinicId}/approve", (object?)null)).AssertStatus(HttpStatusCode.OK);
        var page = await admin.Client.GetOkAsync<Page<AuditEntryDto>>($"/api/admin/audit?clinicId={a.ClinicId}");
        Assert.Equal(a.ClinicId, Assert.Single(page.Items).ClinicId);
    }

    [Fact]
    public async Task Create_platform_admin_cli_is_idempotent_refuses_weak_passwords_and_works_in_production()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, "Production");
        _ = factory.CreateApiClient();
        const string Password = "Str0ng!Bootstrap#2026";
        var phone = NewPhone();
        string[] args = [PlatformAdminBootstrapCommand.Verb, "--email", "root@oqtish.example", "--phone", phone];

        Assert.Equal(1, await PlatformAdminBootstrapCommand.RunAsync(factory.Services, args, () => "short", NullLogger.Instance, Ct));
        Assert.Equal(1, await PlatformAdminBootstrapCommand.RunAsync(factory.Services, args, () => "alllowercaseletters", NullLogger.Instance, Ct));
        Assert.Equal(1, await PlatformAdminBootstrapCommand.RunAsync(factory.Services, args, () => "Root@oqtish-1234", NullLogger.Instance, Ct));
        Assert.Equal(1, await PlatformAdminBootstrapCommand.RunAsync(factory.Services, [.. args, "--password", Password], () => Password, NullLogger.Instance, Ct));
        Assert.Equal(1, await PlatformAdminBootstrapCommand.RunAsync(factory.Services, [PlatformAdminBootstrapCommand.Verb, "--email", "root@oqtish.example"], () => Password, NullLogger.Instance, Ct));

        Assert.Equal(0, await PlatformAdminBootstrapCommand.RunAsync(factory.Services, args, () => Password, NullLogger.Instance, Ct));
        Assert.Equal(0, await PlatformAdminBootstrapCommand.RunAsync(factory.Services, args, () => "Different!Passw0rd-x", NullLogger.Instance, Ct));

        await using (var db = factory.CreateDbContext())
        {
            var identity = await db.AuthIdentities.SingleAsync(i => i.NormalizedIdentifier == "root@oqtish.example", Ct);
            Assert.Equal(1, await db.AuthIdentities.CountAsync(i => i.UserId == identity.UserId && i.Kind == AuthIdentityKind.Phone && i.NormalizedIdentifier == phone && i.VerifiedAt != null, Ct));
            Assert.Equal(PlatformPermissions.All.Order(StringComparer.Ordinal),
                (await db.PlatformAdminGrants.Where(g => g.UserId == identity.UserId && g.RevokedAt == null).Select(g => g.Permission).ToListAsync(Ct)).Order(StringComparer.Ordinal));
            Assert.Equal(2, await db.AuditLog.CountAsync(a => a.Action == "platform.admin.bootstrapped" && a.TargetId == identity.UserId.ToString(), Ct));
        }

        // The first password stays (idempotent re-run does not reset it); the admin can sign in with it.
        var client = factory.CreateApiClient();
        var login = await client.PostAsJsonAsync("/api/auth/password/login", new { email = "root@oqtish.example", password = Password }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        // A phone owned by another account is never merged.
        var other = NewPhone();
        await using (var db = factory.CreateDbContext())
        {
            var u = User.Create("x", DateTimeOffset.UtcNow);
            db.Users.Add(u);
            db.AuthIdentities.Add(AuthIdentity.Create(u.Id, AuthIdentityKind.Phone, other, true, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(3, await PlatformAdminBootstrapCommand.RunAsync(factory.Services, [PlatformAdminBootstrapCommand.Verb, "--email", "second@oqtish.example", "--phone", other], () => Password, NullLogger.Instance, Ct));
    }
}
