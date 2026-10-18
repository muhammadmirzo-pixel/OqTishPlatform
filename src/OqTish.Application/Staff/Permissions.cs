using System.Collections.Frozen;
using OqTish.Domain.Staff;

namespace OqTish.Application.Staff;

/// <summary>How far a role's permission reaches inside the membership's branch scope.</summary>
public enum PermissionReach
{
    /// <summary>Every resource in the membership's scope (whole clinic, or the granted branches).</summary>
    Scope = 1,

    /// <summary>
    /// Only resources tied to the caller's own doctor record (Doctor: own assigned appointments, own time
    /// off). Handlers must additionally filter by <see cref="AccessScope.DoctorIdFor"/>.
    /// </summary>
    OwnOnly = 2,
}

/// <summary>
/// Clinic permission catalogue and the definitive role → permission map (docs/SECURITY.md §8). This file
/// is the single source of truth; it is snapshot-tested (tests/OqTish.UnitTests/Staff). Changing it
/// requires updating SECURITY.md §8 and the snapshot in the same change.
/// </summary>
public static class Permissions
{
    public const string ClinicProfileManage = "clinic.profile.manage";
    public const string BranchManage = "branch.manage";
    public const string StaffManage = "staff.manage";
    public const string CatalogManage = "catalog.manage";
    public const string ScheduleManage = "schedule.manage";
    public const string ScheduleOwnTimeOff = "schedule.own_timeoff";
    public const string AppointmentView = "appointment.view";
    public const string AppointmentTriage = "appointment.triage";
    public const string AppointmentCancel = "appointment.cancel";
    public const string AppointmentOutcome = "appointment.outcome";
    public const string AppointmentCreateWalkIn = "appointment.create_walkin";
    public const string PaymentRecord = "payment.record";
    public const string PaymentRefund = "payment.refund";
    public const string FinanceReportView = "finance.report.view";
    public const string FinanceSettlementView = "finance.settlement.view";
    public const string AuditView = "audit.view";

    public static readonly IReadOnlyList<string> All =
    [
        ClinicProfileManage, BranchManage, StaffManage, CatalogManage, ScheduleManage, ScheduleOwnTimeOff,
        AppointmentView, AppointmentTriage, AppointmentCancel, AppointmentOutcome, AppointmentCreateWalkIn,
        PaymentRecord, PaymentRefund, FinanceReportView, FinanceSettlementView, AuditView,
    ];

    /// <summary>
    /// Permissions about the clinic as a whole. A <see cref="MembershipScope.Branches"/> membership never
    /// holds them, even if its role does (use <see cref="AccessScope.CheckClinicWide"/>).
    /// </summary>
    public static readonly FrozenSet<string> ClinicWideOnly = new[]
    {
        ClinicProfileManage, AuditView, FinanceSettlementView,
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Roles whose memberships require TOTP (SECURITY.md §8, when RequireTotpForPrivilegedRoles).</summary>
    public static readonly FrozenSet<StaffRole> TotpRequiredRoles = new[] { StaffRole.ClinicOwner, StaffRole.FinanceManager }.ToFrozenSet();

    private const PermissionReach S = PermissionReach.Scope;
    private const PermissionReach Own = PermissionReach.OwnOnly;

    /// <summary>The role → permission map.</summary>
    public static readonly FrozenDictionary<StaffRole, FrozenDictionary<string, PermissionReach>> RoleMap =
        new Dictionary<StaffRole, FrozenDictionary<string, PermissionReach>>
        {
            [StaffRole.ClinicOwner] = Map(
                (ClinicProfileManage, S), (BranchManage, S), (StaffManage, S), (CatalogManage, S), (ScheduleManage, S),
                (AppointmentView, S), (AppointmentTriage, S), (AppointmentCancel, S), (AppointmentOutcome, S), (AppointmentCreateWalkIn, S),
                (PaymentRecord, S), (PaymentRefund, S), (FinanceReportView, S), (FinanceSettlementView, S), (AuditView, S)),
            [StaffRole.ClinicAdmin] = Map(
                (ClinicProfileManage, S), (BranchManage, S), (StaffManage, S), (CatalogManage, S), (ScheduleManage, S),
                (AppointmentView, S), (AppointmentTriage, S), (AppointmentCancel, S), (AppointmentOutcome, S), (AppointmentCreateWalkIn, S)),
            [StaffRole.Receptionist] = Map(
                (ScheduleManage, S), (AppointmentView, S), (AppointmentTriage, S), (AppointmentCancel, S), (AppointmentOutcome, S),
                (AppointmentCreateWalkIn, S), (PaymentRecord, S)),
            [StaffRole.Doctor] = Map(
                (ScheduleOwnTimeOff, Own), (AppointmentView, Own), (AppointmentOutcome, Own)),
            [StaffRole.FinanceManager] = Map(
                (AppointmentView, S), (PaymentRecord, S), (PaymentRefund, S), (FinanceReportView, S), (FinanceSettlementView, S)),
        }.ToFrozenDictionary();

    /// <summary>Permissions (with reach) of a role; empty for an unknown role.</summary>
    public static FrozenDictionary<string, PermissionReach> For(StaffRole role) =>
        RoleMap.TryGetValue(role, out var map) ? map : FrozenDictionary<string, PermissionReach>.Empty;

    /// <summary>
    /// Roles a holder of <c>staff.manage</c> with <paramref name="actorRole"/> may grant or remove:
    /// owners manage every role; clinic admins everything except ClinicOwner/ClinicAdmin. No one else.
    /// </summary>
    public static IReadOnlyList<StaffRole> RolesManageableBy(StaffRole actorRole) => actorRole switch
    {
        StaffRole.ClinicOwner => [StaffRole.ClinicOwner, StaffRole.ClinicAdmin, StaffRole.Receptionist, StaffRole.Doctor, StaffRole.FinanceManager],
        StaffRole.ClinicAdmin => [StaffRole.Receptionist, StaffRole.Doctor, StaffRole.FinanceManager],
        _ => [],
    };

    private static FrozenDictionary<string, PermissionReach> Map(params (string Permission, PermissionReach Reach)[] entries) =>
        entries.ToFrozenDictionary(e => e.Permission, e => e.Reach, StringComparer.Ordinal);
}

/// <summary>Platform permissions: explicit grants only (<c>staff.platform_admin_grants</c>), each use audited.</summary>
public static class PlatformPermissions
{
    public const string ClinicVerify = "platform.clinic.verify";
    public const string AccountSuspend = "platform.account.suspend";
    public const string ReportsHandle = "platform.reports.handle";
    public const string FinanceSettle = "platform.finance.settle";
    public const string TenantDataRead = "platform.tenant-data.read";
    public const string AuditView = "platform.audit.view";

    public static readonly IReadOnlyList<string> All =
    [
        ClinicVerify, AccountSuspend, ReportsHandle, FinanceSettle, TenantDataRead, AuditView,
    ];

    public static bool IsKnown(string permission) => All.Contains(permission, StringComparer.Ordinal);
}
