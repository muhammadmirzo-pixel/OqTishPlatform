using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Domain.Staff;

namespace OqTish.Application.Staff;

/// <summary>
/// Clinic memberships (docs/API.md §4). Permission <c>staff.manage</c> plus <see cref="StaffRules"/>.
/// <list type="bullet">
/// <item>GET …/staff — non-removed memberships the caller may manage (Branches-scoped managers: only
/// Branches-scoped memberships inside their branches; filtered in SQL).</item>
/// <item>PUT …/staff/{membershipId} <c>{ role, scope, branchIds }</c> — 403 <c>staff.cannot_grant_role</c> for
/// role/scope escalation or a change to one's own membership; 422 <c>staff.last_owner</c>.</item>
/// <item>DELETE …/staff/{membershipId} — status Removed, effective on the caller's next request (the access
/// scope is computed per request). Self-removal (leaving) is allowed unless last owner.</item>
/// </list>
/// Staff changes of one clinic are serialised by an advisory lock (<c>staff:{clinicId}</c>) so two
/// concurrent demotions cannot both pass the last-owner guard. Side effects: audit
/// <c>staff.membership.changed</c> / <c>staff.membership.removed</c>.
/// </summary>
public sealed class StaffHandlers(IOqTishDb db, IAccessService access, ITransactionRunner transactions, IRowLocks locks, IAuditLog audit, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<Result<IReadOnlyList<StaffMemberDto>>> ListAsync(Guid clinicId, CancellationToken ct)
    {
        var scope = await access.GetScopeAsync(ct);
        var check = scope.Check(Permissions.StaffManage, clinicId);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var actor = scope.MembershipIn(clinicId)!;
        var clinicWide = actor.Scope == MembershipScope.Clinic;
        var own = actor.GrantedBranchIds;
        var memberships = await db.ClinicMemberships.AsNoTracking()
            .Where(m => m.ClinicId == clinicId && m.Status != MembershipStatus.Removed)
            .Where(m => clinicWide || (m.Scope == MembershipScope.Branches
                && !db.BranchAccess.Any(a => a.MembershipId == m.Id && !own.Contains(a.BranchId))))
            .OrderBy(m => m.Role).ThenBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .ToListAsync(ct);
        return await ToDtosAsync(memberships, ct);
    }

    public Task<Result<StaffMemberDto>> UpdateAsync(Guid clinicId, Guid membershipId, StaffChangeCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        return transactions.RunAsync<StaffMemberDto>(async token =>
        {
            var loaded = await LoadAsync(clinicId, membershipId, token);
            if (!loaded.IsSuccess)
            {
                return loaded.Error;
            }

            var (target, actor) = loaded.Value;
            if (StaffRules.ValidateRole(command.Role) is { } roleError)
            {
                return roleError;
            }

            var grant = await StaffRules.ValidateScopeAsync(db, target.ClinicId, command.Scope, command.BranchIds, token);
            if (!grant.IsSuccess)
            {
                return grant.Error;
            }

            var newRole = command.Role!.Value;
            var (newScope, newBranches) = grant.Value;
            if (target.UserId == currentUser.RequiredUserId
                || !StaffRules.CanManageRole(actor, newRole)
                || !StaffRules.Covers(actor, newScope, newBranches))
            {
                return StaffErrors.CannotGrantRole;
            }

            if (target.Role == StaffRole.ClinicOwner && newRole != StaffRole.ClinicOwner && !await HasOtherActiveOwnerAsync(target, token))
            {
                return StaffErrors.LastOwner;
            }

            var before = new { role = target.Role.ToString(), scope = target.Scope.ToString() };
            target.Change(newRole, newScope, clock.GetUtcNow());
            await db.BranchAccess.Where(a => a.MembershipId == target.Id).ExecuteDeleteAsync(token);
            foreach (var branchId in newBranches)
            {
                db.BranchAccess.Add(BranchAccess.Create(target.ClinicId, target.Id, branchId));
            }

            audit.Record(new AuditEvent("staff.membership.changed", target.ClinicId, TargetType: "membership", TargetId: target.Id.ToString(),
                Details: new { userId = target.UserId, from = before, to = new { role = newRole.ToString(), scope = newScope.ToString(), branchIds = newBranches } }));
            await db.SaveChangesAsync(token);
            return (await ToDtosAsync([target], token))[0];
        }, ct);
    }

    public Task<Result> RemoveAsync(Guid clinicId, Guid membershipId, CancellationToken ct) =>
        transactions.RunAsync(async token =>
        {
            var loaded = await LoadAsync(clinicId, membershipId, token, allowSelf: true);
            if (!loaded.IsSuccess)
            {
                return loaded.Error;
            }

            var (target, _) = loaded.Value;
            if (target.Role == StaffRole.ClinicOwner && target.Status == MembershipStatus.Active && !await HasOtherActiveOwnerAsync(target, token))
            {
                return StaffErrors.LastOwner;
            }

            target.SetStatus(MembershipStatus.Removed, clock.GetUtcNow());
            await db.BranchAccess.Where(a => a.MembershipId == target.Id).ExecuteDeleteAsync(token);
            audit.Record(new AuditEvent("staff.membership.removed", target.ClinicId, TargetType: "membership", TargetId: target.Id.ToString(),
                Details: new { userId = target.UserId, role = target.Role.ToString(), self = target.UserId == currentUser.RequiredUserId }));
            await db.SaveChangesAsync(token);
            return Result.Success;
        }, ct);

    /// <summary>Lock, load (route clinic must match), check <c>staff.manage</c> and that the actor may manage the target.</summary>
    private async Task<Result<(ClinicMembership Target, MembershipAccess Actor)>> LoadAsync(Guid clinicId, Guid membershipId, CancellationToken ct, bool allowSelf = false)
    {
        var target = await db.ClinicMemberships.AsNoTracking().FirstOrDefaultAsync(m => m.Id == membershipId && m.ClinicId == clinicId && m.Status != MembershipStatus.Removed, ct);
        if (target is null)
        {
            return CommonErrors.NotFound;
        }

        var scope = await access.GetScopeAsync(ct);
        var check = scope.Check(Permissions.StaffManage, target.ClinicId);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        await locks.AdvisoryLockAsync("staff:" + target.ClinicId, ct);
        target = await db.ClinicMemberships.FirstOrDefaultAsync(m => m.Id == membershipId && m.Status != MembershipStatus.Removed, ct);
        if (target is null)
        {
            return CommonErrors.NotFound;
        }

        var actor = scope.MembershipIn(target.ClinicId)!;
        var self = target.UserId == currentUser.RequiredUserId;
        if (self && allowSelf)
        {
            return (target, actor);
        }

        var branches = await db.BranchAccess.AsNoTracking().Where(a => a.MembershipId == target.Id).Select(a => a.BranchId).ToListAsync(ct);
        if (!StaffRules.CanManageRole(actor, target.Role) || !StaffRules.Covers(actor, target.Scope, branches))
        {
            return StaffErrors.CannotGrantRole;
        }

        return (target, actor);
    }

    private Task<bool> HasOtherActiveOwnerAsync(ClinicMembership target, CancellationToken ct) =>
        db.ClinicMemberships.AnyAsync(m => m.ClinicId == target.ClinicId && m.Id != target.Id && m.Role == StaffRole.ClinicOwner && m.Status == MembershipStatus.Active, ct);

    private async Task<List<StaffMemberDto>> ToDtosAsync(IReadOnlyList<ClinicMembership> memberships, CancellationToken ct)
    {
        var ids = memberships.Select(m => m.Id).ToList();
        var grants = await db.BranchAccess.AsNoTracking().Where(a => ids.Contains(a.MembershipId)).Select(a => new { a.MembershipId, a.BranchId }).ToListAsync(ct);
        var people = await StaffRules.PeopleAsync(db, [.. memberships.Select(m => m.UserId).Distinct()], ct);
        return [.. memberships.Select(m => new StaffMemberDto(
            m.Id, m.UserId, people.TryGetValue(m.UserId, out var p) ? p.Name : "—", people.TryGetValue(m.UserId, out var q) ? q.PhoneMasked : null,
            m.Role, m.Scope, [.. grants.Where(a => a.MembershipId == m.Id).Select(a => a.BranchId).Order()], m.Status))];
    }
}
