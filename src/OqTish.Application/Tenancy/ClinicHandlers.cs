using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Staff;
using OqTish.Domain.Staff;
using OqTish.Domain.Tenancy;

namespace OqTish.Application.Tenancy;

/// <summary>
/// POST /api/clinics (OD-20). Auth: any signed-in user. Creates a <c>Pending</c> clinic (hidden from the
/// public until a platform admin approves it) and makes the caller its ClinicOwner (Clinic scope). At most
/// <see cref="TenancyRules.MaxPendingClinicsPerUser"/> pending registrations per user
/// (422 <c>clinic.registration_limit</c>). Side effects: audit <c>clinic.registered</c>.
/// </summary>
public sealed class RegisterClinicHandler(IOqTishDb db, ITransactionRunner transactions, ICurrentUser currentUser, IAuditLog audit, IRowLocks locks, TimeProvider clock)
{
    public async Task<Result<ClinicProfileDto>> HandleAsync(ClinicProfileCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = new FieldErrors();
        if (TenancyRules.ValidateProfile(command, errors) is not { } v)
        {
            return errors.ToError();
        }

        var userId = currentUser.RequiredUserId;
        return await transactions.RunAsync<ClinicProfileDto>(async token =>
        {
            await locks.AdvisoryLockAsync("clinic-register:" + userId, token);
            var pending = await db.Clinics.CountAsync(c => c.CreatedByUserId == userId && c.VerificationStatus == ClinicVerificationStatus.Pending, token);
            if (pending >= TenancyRules.MaxPendingClinicsPerUser)
            {
                return TenancyErrors.RegistrationLimit;
            }

            var now = clock.GetUtcNow();
            var clinic = Clinic.Register(v.Name, v.LegalName, v.Description, v.Phone, userId, now);
            db.Clinics.Add(clinic);
            var membership = ClinicMembership.Create(clinic.Id, userId, StaffRole.ClinicOwner, MembershipScope.Clinic, now);
            db.ClinicMemberships.Add(membership);
            audit.Record(new AuditEvent("clinic.registered", clinic.Id, TargetType: "clinic", TargetId: clinic.Id.ToString(), Details: new { membershipId = membership.Id }));
            await db.SaveChangesAsync(token);
            return clinic.ToDto();
        }, ct);
    }
}

/// <summary>
/// GET /api/clinics/{clinicId} (any membership) and PUT (<c>clinic.profile.manage</c>, clinic-wide).
/// Side effects (PUT): audit <c>clinic.profile.updated</c>.
/// </summary>
public sealed class ClinicProfileHandler(IOqTishDb db, IAccessService access, IAuditLog audit, TimeProvider clock)
{
    public async Task<Result<ClinicProfileDto>> GetAsync(Guid clinicId, CancellationToken ct)
    {
        var clinic = await db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clinicId, ct);
        if (clinic is null)
        {
            return CommonErrors.NotFound;
        }

        var check = (await access.GetScopeAsync(ct)).CheckMember(clinic.Id);
        return check.IsSuccess ? clinic.ToDto() : check.Error;
    }

    public async Task<Result<ClinicProfileDto>> UpdateAsync(Guid clinicId, ClinicProfileCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var clinic = await db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId, ct);
        if (clinic is null)
        {
            return CommonErrors.NotFound;
        }

        var check = (await access.GetScopeAsync(ct)).CheckClinicWide(Permissions.ClinicProfileManage, clinic.Id);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var errors = new FieldErrors();
        if (TenancyRules.ValidateProfile(command, errors) is not { } v)
        {
            return errors.ToError();
        }

        clinic.UpdateProfile(v.Name, v.LegalName, v.Description, v.Phone, clock.GetUtcNow());
        audit.Record(new AuditEvent("clinic.profile.updated", clinic.Id, TargetType: "clinic", TargetId: clinic.Id.ToString()));
        await db.SaveChangesAsync(ct);
        return clinic.ToDto();
    }
}

/// <summary>Input of <see cref="IClinicDashboardContributor"/>: what to count and for whom.</summary>
public sealed record ClinicDashboardContext(Guid ClinicId, IReadOnlyList<Guid> BranchIds, DateOnly LocalDate, string TimeZone, bool IncludeFinance);

/// <summary>Mutable counters filled by contributors (BE2: appointment counts, BE3: collected sum).</summary>
public sealed class ClinicDashboardCounts
{
    public int Pending { get; set; }

    public int Confirmed { get; set; }

    public int Completed { get; set; }

    public int NoShow { get; set; }

    /// <summary>Set only when <see cref="ClinicDashboardContext.IncludeFinance"/>; defaults to zero then.</summary>
    public OqTish.Domain.Common.Money? Collected { get; set; }
}

/// <summary>
/// Extension point for the clinic dashboard: Booking (BE2) and Finance (BE3) register implementations
/// (<c>services.AddScoped&lt;IClinicDashboardContributor, …&gt;()</c>) that add today's counts for
/// <see cref="ClinicDashboardContext.BranchIds"/>. Without contributors every count is 0.
/// </summary>
public interface IClinicDashboardContributor
{
    Task ContributeAsync(ClinicDashboardContext context, ClinicDashboardCounts counts, CancellationToken cancellationToken);
}

/// <summary>
/// GET /api/clinics/{clinicId}/dashboard?branchId. Auth: any membership; with <c>branchId</c> the branch
/// must be in the caller's scope (403 otherwise, 404 when it belongs to another clinic). Counts cover the
/// caller's accessible branches (or the one branch) for "today" in the branch/clinic time zone. The
/// collected sum is present only with <c>finance.report.view</c>. Side effects: none.
/// </summary>
public sealed class ClinicDashboardQuery(IOqTishDb db, IAccessService access, IEnumerable<IClinicDashboardContributor> contributors, TimeProvider clock)
{
    public async Task<Result<DashboardDto>> HandleAsync(Guid clinicId, Guid? branchId, CancellationToken ct)
    {
        var scope = await access.GetScopeAsync(ct);
        var member = scope.CheckMember(clinicId);
        if (!member.IsSuccess)
        {
            return member.Error;
        }

        var membership = scope.MembershipIn(clinicId)!;
        IReadOnlyList<Guid> branchIds = membership.AccessibleBranchIds;
        string? timeZone = null;
        if (branchId is { } b)
        {
            var branch = await db.Branches.AsNoTracking().Where(x => x.Id == b && x.ClinicId == clinicId).Select(x => new { x.Id, x.TimeZone }).FirstOrDefaultAsync(ct);
            if (branch is null)
            {
                return CommonErrors.NotFound;
            }

            if (!membership.AccessibleBranchIds.Contains(branch.Id))
            {
                return CommonErrors.Forbidden;
            }

            branchIds = [branch.Id];
            timeZone = branch.TimeZone;
        }

        timeZone ??= await db.Branches.AsNoTracking().Where(x => branchIds.Contains(x.Id)).OrderBy(x => x.CreatedAt).Select(x => x.TimeZone).FirstOrDefaultAsync(ct)
            ?? TenancyRules.TimeZones[0];
        var includeFinance = scope.Check(Permissions.FinanceReportView, clinicId, branchId).IsSuccess;
        var context = new ClinicDashboardContext(clinicId, branchIds, TenancyRules.LocalToday(clock.GetUtcNow(), timeZone), timeZone, includeFinance);
        var counts = new ClinicDashboardCounts { Collected = includeFinance ? OqTish.Domain.Common.Money.Zero(OqTish.Domain.Common.Currency.UZS) : null };
        foreach (var contributor in contributors)
        {
            await contributor.ContributeAsync(context, counts, ct);
        }

        return new DashboardDto(LocalFormats.Date(context.LocalDate), branchId, counts.Pending, counts.Confirmed, counts.Completed, counts.NoShow,
            includeFinance ? counts.Collected : null);
    }
}
