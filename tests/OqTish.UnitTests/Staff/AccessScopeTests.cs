using OqTish.Application.Staff;
using OqTish.Domain.Staff;
using OqTish.Domain.Tenancy;

namespace OqTish.UnitTests.Staff;

public class AccessScopeTests
{
    private static readonly Guid ClinicA = Guid.NewGuid();
    private static readonly Guid B1 = Guid.NewGuid();
    private static readonly Guid B2 = Guid.NewGuid();

    private static MembershipAccess Membership(StaffRole role, MembershipScope scope, bool totpBlocked = false, Guid? doctorId = null) =>
        new(Guid.NewGuid(), ClinicA, "A", ClinicVerificationStatus.Approved, role, scope,
            scope == MembershipScope.Branches ? [B1] : [], [B1, B2], totpBlocked, doctorId);

    private static AccessScope Scope(params MembershipAccess[] memberships) => new(Guid.NewGuid(), true, true, memberships, []);

    [Fact]
    public void Unknown_clinic_is_not_found_for_every_check()
    {
        var scope = Scope(Membership(StaffRole.ClinicOwner, MembershipScope.Clinic));
        var other = Guid.NewGuid();
        Assert.Equal("common.not_found", scope.Check(Permissions.AppointmentView, other).Error!.Code);
        Assert.Equal("common.not_found", scope.CheckMember(other).Error!.Code);
        Assert.Empty(scope.BranchIdsWith(Permissions.AppointmentView, other));
    }

    [Fact]
    public void Branch_scope_limits_branches_and_clinic_wide_permissions()
    {
        var scope = Scope(Membership(StaffRole.ClinicAdmin, MembershipScope.Branches));
        Assert.True(scope.Check(Permissions.CatalogManage, ClinicA, B1).IsSuccess);
        Assert.Equal("common.forbidden", scope.Check(Permissions.CatalogManage, ClinicA, B2).Error!.Code);
        Assert.Equal("common.forbidden", scope.Check(Permissions.ClinicProfileManage, ClinicA).Error!.Code);
        Assert.Equal([B1], scope.BranchIdsWith(Permissions.CatalogManage, ClinicA));
    }

    [Fact]
    public void Totp_block_and_doctor_own_semantics()
    {
        var doctorId = Guid.NewGuid();
        Assert.Equal("auth.totp_setup_required", Scope(Membership(StaffRole.FinanceManager, MembershipScope.Clinic, totpBlocked: true))
            .Check(Permissions.PaymentRecord, ClinicA).Error!.Code);
        var doctor = Scope(Membership(StaffRole.Doctor, MembershipScope.Clinic, doctorId: doctorId));
        Assert.True(doctor.IsOwnOnly(Permissions.AppointmentOutcome, ClinicA));
        Assert.Equal(doctorId, doctor.DoctorIdFor(ClinicA));
        Assert.False(Scope(Membership(StaffRole.Receptionist, MembershipScope.Clinic)).IsOwnOnly(Permissions.AppointmentOutcome, ClinicA));
    }

    [Fact]
    public void Platform_permissions_are_explicit()
    {
        var scope = new AccessScope(Guid.NewGuid(), totpEnabled: false, totpRequiredForPrivileged: false, [], [PlatformPermissions.AuditView]);
        Assert.True(scope.CheckPlatform(PlatformPermissions.AuditView).IsSuccess);
        Assert.Equal("common.forbidden", scope.CheckPlatform(PlatformPermissions.TenantDataRead).Error!.Code);
        Assert.Equal([PlatformPermissions.AuditView], scope.EffectivePlatformPermissions);
    }
}
