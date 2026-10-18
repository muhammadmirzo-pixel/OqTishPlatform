using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OqTish.Application.Staff;
using OqTish.Domain.Staff;
using OqTish.Domain.Tenancy;
using OqTish.IntegrationTests.Infrastructure;
using static OqTish.IntegrationTests.Infrastructure.TestAuth;

namespace OqTish.IntegrationTests.Staff;

/// <summary>Access rules computed from real DB state (CONVENTIONS §3).</summary>
public class AccessServiceTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<AccessScope> ScopeAsync(ApiFactory factory, Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAccessService>().GetScopeForUserAsync(userId, Ct);
    }

    [Fact]
    public async Task Cross_clinic_is_not_found_and_out_of_branch_is_forbidden()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var a = await CreateClinicAsync(factory, 2);
        var b = await CreateClinicAsync(factory, 1);
        var receptionist = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.Receptionist, [a.BranchIds[0]]);
        var scope = await ScopeAsync(factory, receptionist.Me.Id);

        Assert.True(scope.Check(Permissions.AppointmentTriage, a.ClinicId, a.BranchIds[0]).IsSuccess);
        Assert.Equal("common.forbidden", scope.Check(Permissions.AppointmentTriage, a.ClinicId, a.BranchIds[1]).Error!.Code);
        Assert.Equal("common.forbidden", scope.Check(Permissions.FinanceReportView, a.ClinicId, a.BranchIds[0]).Error!.Code);
        Assert.Equal("common.not_found", scope.Check(Permissions.AppointmentView, b.ClinicId, b.BranchIds[0]).Error!.Code);
        Assert.Equal([a.BranchIds[0]], scope.BranchIdsWith(Permissions.AppointmentView, a.ClinicId));
        Assert.Empty(scope.BranchIdsWith(Permissions.PaymentRefund, a.ClinicId));
        Assert.Empty(scope.BranchIdsWith(Permissions.AppointmentView, b.ClinicId));
    }

    [Fact]
    public async Task Clinic_scope_covers_all_branches_and_clinic_wide_permissions_need_clinic_scope()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var a = await CreateClinicAsync(factory, 3);
        var owner = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicOwner);
        var branchAdmin = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicAdmin, [a.BranchIds[1]]);

        var ownerScope = await ScopeAsync(factory, owner.Me.Id);
        Assert.Equal(a.BranchIds.Order(), ownerScope.BranchIdsWith(Permissions.AppointmentView, a.ClinicId).Order());
        Assert.True(ownerScope.CheckClinicWide(Permissions.ClinicProfileManage, a.ClinicId).IsSuccess);

        var adminScope = await ScopeAsync(factory, branchAdmin.Me.Id);
        Assert.Equal("common.forbidden", adminScope.Check(Permissions.ClinicProfileManage, a.ClinicId).Error!.Code);
        Assert.True(adminScope.Check(Permissions.BranchManage, a.ClinicId, a.BranchIds[1]).IsSuccess);
        Assert.Equal("common.forbidden", adminScope.CheckClinicWide(Permissions.BranchManage, a.ClinicId).Error!.Code);
        Assert.True(adminScope.CheckMember(a.ClinicId).IsSuccess);
    }

    [Fact]
    public async Task Suspended_clinic_or_membership_behaves_as_no_membership()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var suspended = await CreateClinicAsync(factory, 1, ClinicVerificationStatus.Suspended);
        var pending = await CreateClinicAsync(factory, 1, ClinicVerificationStatus.Pending);
        var user = await SignInAsStaffAsync(factory, suspended.ClinicId, StaffRole.ClinicOwner);
        var membershipId = await AddMembershipAsync(factory, user.Me.Id, pending.ClinicId, StaffRole.ClinicOwner);

        var scope = await ScopeAsync(factory, user.Me.Id);
        Assert.Equal("common.not_found", scope.Check(Permissions.AppointmentView, suspended.ClinicId).Error!.Code);
        Assert.True(scope.Check(Permissions.AppointmentView, pending.ClinicId).IsSuccess);

        await using (var db = factory.CreateDbContext())
        {
            var m = await db.ClinicMemberships.SingleAsync(x => x.Id == membershipId, Ct);
            m.SetStatus(MembershipStatus.Suspended, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal("common.not_found", (await ScopeAsync(factory, user.Me.Id)).Check(Permissions.AppointmentView, pending.ClinicId).Error!.Code);
    }

    [Fact]
    public async Task Doctor_permissions_are_own_only()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var a = await CreateClinicAsync(factory, 1);
        var doctor = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.Doctor);
        var scope = await ScopeAsync(factory, doctor.Me.Id);
        Assert.True(scope.Check(Permissions.AppointmentView, a.ClinicId, a.BranchIds[0]).IsSuccess);
        Assert.True(scope.IsOwnOnly(Permissions.AppointmentView, a.ClinicId));
        Assert.Equal("common.forbidden", scope.Check(Permissions.AppointmentTriage, a.ClinicId, a.BranchIds[0]).Error!.Code);
        Assert.Null(scope.DoctorIdFor(a.ClinicId));
    }

    [Fact]
    public async Task Totp_rule_blocks_privileged_roles_and_platform_grants_until_enabled()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, settings: new Dictionary<string, string?> { ["Security:RequireTotpForPrivilegedRoles"] = "true" });
        var a = await CreateClinicAsync(factory, 1);
        var owner = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicOwner);
        var receptionist = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.Receptionist);
        await using (var db = factory.CreateDbContext())
        {
            db.PlatformAdminGrants.Add(PlatformAdminGrant.Create(owner.Me.Id, PlatformPermissions.ClinicVerify, null, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(Ct);
        }

        var blocked = await ScopeAsync(factory, owner.Me.Id);
        Assert.Equal("auth.totp_setup_required", blocked.Check(Permissions.AppointmentView, a.ClinicId).Error!.Code);
        Assert.Equal("auth.totp_setup_required", blocked.CheckPlatform(PlatformPermissions.ClinicVerify).Error!.Code);
        Assert.Equal("common.forbidden", blocked.CheckPlatform(PlatformPermissions.AuditView).Error!.Code);
        Assert.True(blocked.TotpSetupRequired);
        Assert.True((await ScopeAsync(factory, receptionist.Me.Id)).Check(Permissions.AppointmentView, a.ClinicId).IsSuccess);

        var me = await owner.Client.GetFromJsonAsync<OqTish.Application.Identity.MeDto>("/api/me", Json, Ct);
        Assert.True(me!.TotpSetupRequired);
        Assert.Empty(Assert.Single(me.Memberships).Permissions);
        Assert.Empty(me.PlatformPermissions);

        await EnableTotpAsync(owner.Client);
        var allowed = await ScopeAsync(factory, owner.Me.Id);
        Assert.True(allowed.Check(Permissions.AppointmentView, a.ClinicId).IsSuccess);
        Assert.True(allowed.CheckPlatform(PlatformPermissions.ClinicVerify).IsSuccess);
    }

    [Fact]
    public async Task Suspended_user_has_an_empty_scope()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var a = await CreateClinicAsync(factory, 1);
        var owner = await SignInAsStaffAsync(factory, a.ClinicId, StaffRole.ClinicOwner);
        await using (var db = factory.CreateDbContext())
        {
            await db.Database.ExecuteSqlAsync($"UPDATE identity.users SET status = 'Suspended' WHERE id = {owner.Me.Id}", Ct);
        }

        Assert.Equal("common.not_found", (await ScopeAsync(factory, owner.Me.Id)).Check(Permissions.AppointmentView, a.ClinicId).Error!.Code);
    }
}
