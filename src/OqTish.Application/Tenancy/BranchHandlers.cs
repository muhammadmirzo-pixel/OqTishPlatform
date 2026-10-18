using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Staff;
using OqTish.Domain.Staff;
using OqTish.Domain.Tenancy;

namespace OqTish.Application.Tenancy;

/// <summary>
/// Branches of a clinic (docs/API.md §4).
/// <list type="bullet">
/// <item>GET …/branches — any membership; Branches-scoped members see only their branches (filtered in SQL).</item>
/// <item>POST …/branches — <c>branch.manage</c> clinic-wide (a new branch is outside every branch scope).</item>
/// <item>PUT …/branches/{branchId} — <c>branch.manage</c> on that branch.</item>
/// <item>PUT …/branches/{branchId}/hours — <c>branch.manage</c> on that branch; replaces the weekly hours.</item>
/// </list>
/// Side effects: audit <c>branch.created</c>, <c>branch.updated</c>, <c>branch.hours.updated</c>.
/// </summary>
public sealed class BranchHandlers(IOqTishDb db, IAccessService access, ITransactionRunner transactions, IAuditLog audit, TimeProvider clock)
{
    public async Task<Result<IReadOnlyList<BranchDto>>> ListAsync(Guid clinicId, CancellationToken ct)
    {
        var scope = await access.GetScopeAsync(ct);
        var check = scope.CheckMember(clinicId);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var m = scope.MembershipIn(clinicId)!;
        var clinicWide = m.Scope == MembershipScope.Clinic;
        var granted = m.GrantedBranchIds;
        var branches = await db.Branches.AsNoTracking()
            .Where(b => b.ClinicId == clinicId && (clinicWide || granted.Contains(b.Id)))
            .OrderBy(b => b.Name).ThenBy(b => b.Id)
            .ToListAsync(ct);
        var ids = branches.Select(b => b.Id).ToList();
        var hours = await db.BranchOpeningHours.AsNoTracking().Where(h => ids.Contains(h.BranchId)).ToListAsync(ct);
        return branches.Select(b => b.ToDto(hours)).ToList();
    }

    public async Task<Result<BranchDto>> CreateAsync(Guid clinicId, BranchCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var check = (await access.GetScopeAsync(ct)).CheckClinicWide(Permissions.BranchManage, clinicId);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var errors = new FieldErrors();
        if (TenancyRules.ValidateBranch(command, errors) is not { } v)
        {
            return errors.ToError();
        }

        var branch = Branch.Create(clinicId, v.Name, v.District, v.Address, v.Latitude, v.Longitude, v.Phone, v.TimeZone, clock.GetUtcNow());
        if (!v.IsActive)
        {
            branch.Update(v.Name, v.District, v.Address, v.Latitude, v.Longitude, v.Phone, v.TimeZone, false, clock.GetUtcNow());
        }

        db.Branches.Add(branch);
        audit.Record(new AuditEvent("branch.created", clinicId, branch.Id, "branch", branch.Id.ToString()));
        await db.SaveChangesAsync(ct);
        return branch.ToDto([]);
    }

    public async Task<Result<BranchDto>> UpdateAsync(Guid clinicId, Guid branchId, BranchCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == branchId && b.ClinicId == clinicId, ct);
        if (branch is null)
        {
            return CommonErrors.NotFound;
        }

        var check = (await access.GetScopeAsync(ct)).Check(Permissions.BranchManage, branch.ClinicId, branch.Id);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var errors = new FieldErrors();
        if (TenancyRules.ValidateBranch(command, errors) is not { } v)
        {
            return errors.ToError();
        }

        branch.Update(v.Name, v.District, v.Address, v.Latitude, v.Longitude, v.Phone, v.TimeZone, v.IsActive, clock.GetUtcNow());
        audit.Record(new AuditEvent("branch.updated", clinicId, branch.Id, "branch", branch.Id.ToString(), new { v.IsActive }));
        await db.SaveChangesAsync(ct);
        var hours = await db.BranchOpeningHours.AsNoTracking().Where(h => h.BranchId == branch.Id).ToListAsync(ct);
        return branch.ToDto(hours);
    }

    public async Task<Result> SetHoursAsync(Guid clinicId, Guid branchId, IReadOnlyList<OpeningHoursDto>? hours, CancellationToken ct)
    {
        var branch = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == branchId && b.ClinicId == clinicId, ct);
        if (branch is null)
        {
            return CommonErrors.NotFound;
        }

        var check = (await access.GetScopeAsync(ct)).Check(Permissions.BranchManage, branch.ClinicId, branch.Id);
        if (!check.IsSuccess)
        {
            return check;
        }

        var errors = new FieldErrors();
        if (TenancyRules.ValidateHours(hours, errors) is not { } valid)
        {
            return errors.ToError();
        }

        return await transactions.RunAsync(async token =>
        {
            await db.BranchOpeningHours.Where(h => h.BranchId == branch.Id).ExecuteDeleteAsync(token);
            foreach (var (weekday, open, close) in valid)
            {
                db.BranchOpeningHours.Add(BranchOpeningHours.Create(branch.ClinicId, branch.Id, weekday, open, close));
            }

            audit.Record(new AuditEvent("branch.hours.updated", branch.ClinicId, branch.Id, "branch", branch.Id.ToString(), new { intervals = valid.Count }));
            await db.SaveChangesAsync(token);
            return Result.Success;
        }, ct);
    }
}
