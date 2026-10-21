using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Staff;
using OqTish.Domain.Scheduling;
using OqTish.Domain.Staff;

namespace OqTish.Application.Scheduling;

/// <summary>
/// Doctors and their branch/service links (docs/API.md §4).
/// <list type="bullet">
/// <item>GET …/doctors — any membership. Clinic-scoped members see every doctor; Branches-scoped members see
/// doctors linked to one of their branches (SQL filter).</item>
/// <item>POST/PUT …/doctors[/{doctorId}] <c>{ displayName, isActive, branchIds, serviceIds, userId? }</c> —
/// <c>schedule.manage</c> on every branch the doctor has before and after the change (a doctor without
/// branches: the permission anywhere in the clinic). Setting/changing <c>userId</c> additionally needs
/// <c>staff.manage</c>, because it grants Doctor-own access; the user must hold a non-removed membership in the
/// clinic (422 <c>schedule.doctor_user_invalid</c>) and not be linked to another doctor (409
/// <c>schedule.doctor_user_taken</c>). Unlinking a branch deletes the doctor's working hours there. A branch or
/// service link referenced by appointments or time blocks cannot be removed (409 <c>schedule.link_in_use</c>, BE2).</item>
/// </list>
/// Side effects: audit <c>schedule.doctor.created</c> / <c>schedule.doctor.updated</c>.
/// </summary>
public sealed class DoctorHandlers(IOqTishDb db, IAccessService access, ITransactionRunner transactions, IAuditLog audit, IDbErrorClassifier errors, TimeProvider clock)
{
    public async Task<Result<IReadOnlyList<DoctorDto>>> ListAsync(Guid clinicId, CancellationToken ct)
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
        var doctors = await db.Doctors.AsNoTracking()
            .Where(d => d.ClinicId == clinicId && (clinicWide || db.DoctorBranches.Any(x => x.DoctorId == d.Id && granted.Contains(x.BranchId))))
            .OrderBy(d => d.DisplayName).ThenBy(d => d.Id)
            .ToListAsync(ct);
        return await ToDtosAsync(doctors, ct);
    }

    public Task<Result<DoctorDto>> CreateAsync(Guid clinicId, DoctorCommand command, CancellationToken ct) =>
        SaveAsync(clinicId, null, command, ct);

    public Task<Result<DoctorDto>> UpdateAsync(Guid clinicId, Guid doctorId, DoctorCommand command, CancellationToken ct) =>
        SaveAsync(clinicId, doctorId, command, ct);

    private async Task<Result<DoctorDto>> SaveAsync(Guid clinicId, Guid? doctorId, DoctorCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        try
        {
            return await transactions.RunAsync<DoctorDto>(token => SaveInTransactionAsync(clinicId, doctorId, command, token), ct);
        }
        catch (DbUpdateException ex) when (errors.Classify(ex) is { Kind: DbErrorKind.UniqueViolation })
        {
            // Two concurrent links of the same user (unique index on clinic_id, user_id).
            return ScheduleErrors.DoctorUserTaken;
        }
        catch (Exception ex) when (errors.Classify(ex) is { Kind: DbErrorKind.ForeignKeyViolation })
        {
            // A booking referenced a link between the in-use check and the delete.
            return ScheduleErrors.LinkInUse;
        }
    }

    private async Task<Result<DoctorDto>> SaveInTransactionAsync(Guid clinicId, Guid? doctorId, DoctorCommand command, CancellationToken ct)
    {
        Doctor? doctor = null;
        List<Guid> oldBranches = [];
        if (doctorId is { } id)
        {
            doctor = await db.Doctors.FirstOrDefaultAsync(d => d.Id == id && d.ClinicId == clinicId, ct);
            if (doctor is null)
            {
                return CommonErrors.NotFound;
            }

            oldBranches = await db.DoctorBranches.Where(x => x.DoctorId == id).Select(x => x.BranchId).ToListAsync(ct);
        }

        var scope = await access.GetScopeAsync(ct);
        var member = scope.CheckMember(clinicId);
        if (!member.IsSuccess)
        {
            return member.Error;
        }

        var validation = new FieldErrors();
        var name = validation.Text(command.DisplayName, "displayName", Doctor.MaxNameLength, required: true);
        var branchIds = (command.BranchIds ?? []).Distinct().ToList();
        var serviceIds = (command.ServiceIds ?? []).Distinct().ToList();
        if (branchIds.Count > 0 && await db.Branches.CountAsync(b => b.ClinicId == clinicId && branchIds.Contains(b.Id), ct) != branchIds.Count)
        {
            validation.Add("branchIds", "Unknown branch.");
        }

        if (serviceIds.Count > 0 && await db.Services.CountAsync(s => s.ClinicId == clinicId && serviceIds.Contains(s.Id), ct) != serviceIds.Count)
        {
            validation.Add("serviceIds", "Unknown service.");
        }

        // Authorization on every branch touched (before and after), never only the route clinic.
        var touched = oldBranches.Union(branchIds).ToList();
        var auth = touched.Count == 0
            ? scope.Check(Permissions.ScheduleManage, clinicId)
            : touched.Select(b => scope.Check(Permissions.ScheduleManage, clinicId, b)).FirstOrDefault(r => !r.IsSuccess, Result.Success);
        if (!auth.IsSuccess)
        {
            return auth.Error;
        }

        if (validation.HasErrors)
        {
            return validation.ToError();
        }

        if (command.UserId != doctor?.UserId)
        {
            var staff = scope.Check(Permissions.StaffManage, clinicId);
            if (!staff.IsSuccess)
            {
                return staff.Error;
            }

            if (command.UserId is { } uid && !await db.ClinicMemberships.AnyAsync(m => m.ClinicId == clinicId && m.UserId == uid && m.Status != MembershipStatus.Removed, ct))
            {
                return ScheduleErrors.DoctorUserInvalid;
            }

            if (command.UserId is { } taken && await db.Doctors.AnyAsync(d => d.ClinicId == clinicId && d.UserId == taken && d.Id != doctorId, ct))
            {
                return ScheduleErrors.DoctorUserTaken;
            }
        }

        var now = clock.GetUtcNow();
        if (doctor is null)
        {
            doctor = Doctor.Create(clinicId, name!, command.IsActive ?? true, command.UserId, now);
            db.Doctors.Add(doctor);
        }
        else
        {
            doctor.Update(name!, command.IsActive ?? doctor.IsActive, command.UserId, now);
        }

        var removedBranches = oldBranches.Except(branchIds).ToList();
        if (doctorId is { } existingId)
        {
            // Appointments and time blocks reference (clinic, doctor, branch) / (clinic, doctor, service) with
            // RESTRICT foreign keys (BE2): such links cannot be removed.
            var removedServices = await db.DoctorServices.Where(x => x.DoctorId == existingId && !serviceIds.Contains(x.ServiceId)).Select(x => x.ServiceId).ToListAsync(ct);
            var inUse = (removedBranches.Count > 0
                    && (await db.Appointments.AnyAsync(a => a.DoctorId == existingId && removedBranches.Contains(a.BranchId), ct)
                        || await db.DoctorTimeBlocks.AnyAsync(b => b.DoctorId == existingId && b.BranchId != null && removedBranches.Contains(b.BranchId.Value), ct)))
                || (removedServices.Count > 0 && await db.Appointments.AnyAsync(a => a.DoctorId == existingId && removedServices.Contains(a.ServiceId), ct));
            if (inUse)
            {
                return ScheduleErrors.LinkInUse;
            }
        }

        await db.SaveChangesAsync(ct);
        await db.DoctorBranches.Where(x => x.DoctorId == doctor.Id && removedBranches.Contains(x.BranchId)).ExecuteDeleteAsync(ct);
        foreach (var b in branchIds.Except(oldBranches))
        {
            db.DoctorBranches.Add(DoctorBranch.Create(clinicId, doctor.Id, b));
        }

        await db.DoctorServices.Where(x => x.DoctorId == doctor.Id && !serviceIds.Contains(x.ServiceId)).ExecuteDeleteAsync(ct);
        var existingServices = await db.DoctorServices.Where(x => x.DoctorId == doctor.Id).Select(x => x.ServiceId).ToListAsync(ct);
        foreach (var s in serviceIds.Except(existingServices))
        {
            db.DoctorServices.Add(DoctorService.Create(clinicId, doctor.Id, s));
        }

        audit.Record(new AuditEvent(doctorId is null ? "schedule.doctor.created" : "schedule.doctor.updated", clinicId, TargetType: "doctor", TargetId: doctor.Id.ToString(),
            Details: new { doctor.IsActive, branchIds, serviceIds, userId = doctor.UserId }));
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([doctor], ct))[0];
    }

    private async Task<List<DoctorDto>> ToDtosAsync(IReadOnlyList<Doctor> doctors, CancellationToken ct)
    {
        var ids = doctors.Select(d => d.Id).ToList();
        var branches = await db.DoctorBranches.AsNoTracking().Where(x => ids.Contains(x.DoctorId)).Select(x => new { x.DoctorId, x.BranchId }).ToListAsync(ct);
        var services = await db.DoctorServices.AsNoTracking().Where(x => ids.Contains(x.DoctorId)).Select(x => new { x.DoctorId, x.ServiceId }).ToListAsync(ct);
        var avatars = await StaffRules.AvatarsAsync(db, [.. doctors.Where(d => d.UserId != null).Select(d => d.UserId!.Value).Distinct()], ct);
        return [.. doctors.Select(d => new DoctorDto(d.Id, d.DisplayName, d.IsActive,
            [.. branches.Where(x => x.DoctorId == d.Id).Select(x => x.BranchId).Order()],
            [.. services.Where(x => x.DoctorId == d.Id).Select(x => x.ServiceId).Order()],
            d.UserId,
            d.UserId is { } u ? avatars.GetValueOrDefault(u) : null))];
    }
}
