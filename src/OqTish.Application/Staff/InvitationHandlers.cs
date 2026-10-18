using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Application.Common.Security;
using OqTish.Application.Identity;
using OqTish.Domain.Identity;
using OqTish.Domain.Staff;
using OqTish.Domain.Tenancy;

namespace OqTish.Application.Staff;

/// <summary>
/// Staff invitations by phone (OD-21), clinic side: GET/POST/DELETE …/invitations. Permission
/// <c>staff.manage</c> plus <see cref="StaffRules"/> (role and branch containment). The phone is stored only
/// as an HMAC (<c>Security:OtpPepper</c>, purpose <c>phone</c>) plus a masked form. A new invitation for
/// the same phone replaces (revokes) the open one. Expiry <c>Staff:InvitationTtlDays</c> (7). No SMS is
/// sent in v1: the invitee sees it under GET /api/me/invitations after signing in with that phone.
/// Side effects: audit <c>staff.invitation.created</c> / <c>staff.invitation.revoked</c>.
/// </summary>
public sealed class ClinicInvitationHandlers(
    IOqTishDb db,
    IAccessService access,
    ITransactionRunner transactions,
    IRowLocks locks,
    IKeyedHasher hasher,
    IAuditLog audit,
    ICurrentUser currentUser,
    TimeProvider clock,
    IOptions<StaffOptions> options)
{
    public async Task<Result<IReadOnlyList<InvitationDto>>> ListAsync(Guid clinicId, CancellationToken ct)
    {
        var scope = await access.GetScopeAsync(ct);
        var check = scope.Check(Permissions.StaffManage, clinicId);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var actor = scope.MembershipIn(clinicId)!;
        var open = await db.StaffInvitations.AsNoTracking()
            .Where(i => i.ClinicId == clinicId && i.AcceptedAt == null && i.RevokedAt == null)
            .OrderByDescending(i => i.CreatedAt).ThenBy(i => i.Id)
            .ToListAsync(ct);
        // Branches-scoped managers see only invitations inside their branches (the array column is filtered
        // after loading the clinic's open invitations; they are few and all belong to the caller's clinic).
        return open.Where(i => StaffRules.Covers(actor, i.Scope, i.BranchIds)).Select(ToDto).ToList();
    }

    public async Task<Result<InvitationDto>> CreateAsync(Guid clinicId, InvitationCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var scope = await access.GetScopeAsync(ct);
        var check = scope.Check(Permissions.StaffManage, clinicId);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        var actor = scope.MembershipIn(clinicId)!;
        if (!PhoneNumber.TryNormalize(command.Phone, out var phone))
        {
            return Error.InvalidField("phone", "Enter an Uzbekistan mobile number (+998 and 9 digits).");
        }

        if (StaffRules.ValidateRole(command.Role) is { } roleError)
        {
            return roleError;
        }

        var grant = await StaffRules.ValidateScopeAsync(db, clinicId, command.Scope, command.BranchIds, ct);
        if (!grant.IsSuccess)
        {
            return grant.Error;
        }

        var (grantScope, branchIds) = grant.Value;
        if (!StaffRules.CanManageRole(actor, command.Role!.Value) || !StaffRules.Covers(actor, grantScope, branchIds))
        {
            return StaffErrors.CannotGrantRole;
        }

        var phoneHash = hasher.Hash(HashPurposes.Phone, phone);
        return await transactions.RunAsync<InvitationDto>(async token =>
        {
            await locks.AdvisoryLockAsync("staff:" + clinicId, token);
            var isMember = await (
                    from i in db.AuthIdentities
                    join m in db.ClinicMemberships on i.UserId equals m.UserId
                    where i.Kind == AuthIdentityKind.Phone && i.NormalizedIdentifier == phone
                        && m.ClinicId == clinicId && m.Status != MembershipStatus.Removed
                    select m.Id)
                .AnyAsync(token);
            if (isMember)
            {
                return StaffErrors.AlreadyMember;
            }

            var now = clock.GetUtcNow();
            var previous = await db.StaffInvitations
                .Where(i => i.ClinicId == clinicId && i.PhoneHash == phoneHash && i.AcceptedAt == null && i.RevokedAt == null)
                .ToListAsync(token);
            foreach (var p in previous)
            {
                p.Revoke(now);
            }

            var invitation = StaffInvitation.Create(clinicId, phoneHash, PhoneNumber.Mask(phone), command.Role!.Value, grantScope, branchIds,
                currentUser.RequiredUserId, now, TimeSpan.FromDays(options.Value.InvitationTtlDays));
            db.StaffInvitations.Add(invitation);
            audit.Record(new AuditEvent("staff.invitation.created", clinicId, TargetType: "invitation", TargetId: invitation.Id.ToString(),
                Details: new { role = invitation.Role.ToString(), scope = invitation.Scope.ToString(), branchIds, replaced = previous.Select(p => p.Id) }));
            await db.SaveChangesAsync(token);
            return ToDto(invitation);
        }, ct);
    }

    public async Task<Result> RevokeAsync(Guid clinicId, Guid invitationId, CancellationToken ct)
    {
        var invitation = await db.StaffInvitations.FirstOrDefaultAsync(i => i.Id == invitationId && i.ClinicId == clinicId && i.AcceptedAt == null && i.RevokedAt == null, ct);
        if (invitation is null)
        {
            return CommonErrors.NotFound;
        }

        var scope = await access.GetScopeAsync(ct);
        var check = scope.Check(Permissions.StaffManage, invitation.ClinicId);
        if (!check.IsSuccess)
        {
            return check;
        }

        var actor = scope.MembershipIn(invitation.ClinicId)!;
        if (!StaffRules.CanManageRole(actor, invitation.Role) || !StaffRules.Covers(actor, invitation.Scope, invitation.BranchIds))
        {
            return StaffErrors.CannotGrantRole;
        }

        invitation.Revoke(clock.GetUtcNow());
        audit.Record(new AuditEvent("staff.invitation.revoked", invitation.ClinicId, TargetType: "invitation", TargetId: invitation.Id.ToString()));
        await db.SaveChangesAsync(ct);
        return Result.Success;
    }

    private static InvitationDto ToDto(StaffInvitation i) =>
        new(i.Id, i.PhoneMasked, i.Role, i.Scope, [.. i.BranchIds], i.CreatedAt, i.ExpiresAt);
}

/// <summary>
/// The invitee's side: GET /api/me/invitations and POST /api/me/invitations/{id}/accept. Auth: user.
/// Only invitations addressed to the caller's <b>verified</b> phone are visible (others → 404, no
/// enumeration). Accepting checks expiry with database time in the same statement that marks the
/// invitation accepted (422 <c>staff.invitation_expired</c>), then creates the membership (or reactivates a
/// removed one) with the invited role, scope and branch access. An existing active membership → 409
/// <c>staff.already_member</c>. Suspended clinics are invisible. Side effects: audit <c>staff.invitation.accepted</c>.
/// </summary>
public sealed class MyInvitationHandlers(IOqTishDb db, ITransactionRunner transactions, IKeyedHasher hasher, IAuditLog audit, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<Result<IReadOnlyList<MyInvitationDto>>> ListAsync(CancellationToken ct)
    {
        if (await PhoneHashAsync(ct) is not { } phoneHash)
        {
            return Array.Empty<MyInvitationDto>();
        }

        var rows = await (
                from i in db.StaffInvitations.AsNoTracking()
                join c in db.Clinics.AsNoTracking() on i.ClinicId equals c.Id
                where i.PhoneHash == phoneHash && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > DateTimeOffset.UtcNow
                    && c.VerificationStatus != ClinicVerificationStatus.Suspended
                orderby i.CreatedAt descending
                select new { Invitation = i, ClinicName = c.Name })
            .ToListAsync(ct);
        var branchIds = rows.SelectMany(r => r.Invitation.BranchIds).Distinct().ToList();
        var names = await db.Branches.AsNoTracking().Where(b => branchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        return rows.Select(r => new MyInvitationDto(
                r.Invitation.Id, r.Invitation.ClinicId, r.ClinicName, r.Invitation.Role, r.Invitation.Scope,
                [.. r.Invitation.BranchIds.Where(names.ContainsKey).Select(id => names[id]).Order(StringComparer.Ordinal)],
                r.Invitation.ExpiresAt))
            .ToList();
    }

    public async Task<Result> AcceptAsync(Guid invitationId, CancellationToken ct)
    {
        var userId = currentUser.RequiredUserId;
        if (await PhoneHashAsync(ct) is not { } phoneHash)
        {
            return CommonErrors.NotFound;
        }

        return await transactions.RunAsync(async token =>
        {
            var invitation = await (
                    from i in db.StaffInvitations.AsNoTracking()
                    join c in db.Clinics.AsNoTracking() on i.ClinicId equals c.Id
                    where i.Id == invitationId && i.PhoneHash == phoneHash && i.AcceptedAt == null && i.RevokedAt == null
                        && c.VerificationStatus != ClinicVerificationStatus.Suspended
                    select i)
                .FirstOrDefaultAsync(token);
            if (invitation is null)
            {
                return CommonErrors.NotFound;
            }

            var now = clock.GetUtcNow();
            var marked = await db.StaffInvitations
                .Where(i => i.Id == invitation.Id && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > DateTimeOffset.UtcNow)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.AcceptedAt, now).SetProperty(i => i.AcceptedByUserId, userId), token);
            if (marked == 0)
            {
                var stillOpen = await db.StaffInvitations.AnyAsync(i => i.Id == invitation.Id && i.AcceptedAt == null && i.RevokedAt == null, token);
                return stillOpen ? StaffErrors.InvitationExpired : CommonErrors.NotFound;
            }

            var membership = await db.ClinicMemberships.FirstOrDefaultAsync(m => m.ClinicId == invitation.ClinicId && m.UserId == userId, token);
            if (membership is { Status: not MembershipStatus.Removed })
            {
                return StaffErrors.AlreadyMember;
            }

            if (membership is null)
            {
                membership = ClinicMembership.Create(invitation.ClinicId, userId, invitation.Role, invitation.Scope, now);
                db.ClinicMemberships.Add(membership);
            }
            else
            {
                membership.Change(invitation.Role, invitation.Scope, now);
                membership.SetStatus(MembershipStatus.Active, now);
                await db.BranchAccess.Where(a => a.MembershipId == membership.Id).ExecuteDeleteAsync(token);
            }

            var invited = invitation.BranchIds.ToList();
            var existing = await db.Branches.AsNoTracking().Where(b => b.ClinicId == invitation.ClinicId && invited.Contains(b.Id)).Select(b => b.Id).ToListAsync(token);
            foreach (var branchId in existing)
            {
                db.BranchAccess.Add(BranchAccess.Create(invitation.ClinicId, membership.Id, branchId));
            }

            audit.Record(new AuditEvent("staff.invitation.accepted", invitation.ClinicId, TargetType: "invitation", TargetId: invitation.Id.ToString(),
                Details: new { membershipId = membership.Id, role = invitation.Role.ToString(), scope = invitation.Scope.ToString() }));
            await db.SaveChangesAsync(token);
            return Result.Success;
        }, ct);
    }

    private async Task<string?> PhoneHashAsync(CancellationToken ct)
    {
        var userId = currentUser.RequiredUserId;
        var phone = await db.AuthIdentities.AsNoTracking()
            .Where(i => i.UserId == userId && i.Kind == AuthIdentityKind.Phone && i.VerifiedAt != null)
            .Select(i => i.NormalizedIdentifier)
            .FirstOrDefaultAsync(ct);
        return phone is null ? null : hasher.Hash(HashPurposes.Phone, phone);
    }
}
