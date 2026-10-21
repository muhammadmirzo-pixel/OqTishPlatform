using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Common.Images;
using OqTish.Application.Staff;
using OqTish.Domain.Tenancy;

namespace OqTish.Application.Tenancy;

/// <summary>
/// PUT /api/clinics/{clinicId}/logo (multipart, field <c>file</c>) and DELETE. Auth: the same rule as
/// PUT clinic profile — <c>clinic.profile.manage</c> clinic-wide (no membership → 404, otherwise 403).
/// The image is validated by <see cref="ImageRules"/> (400 <c>common.validation</c>, field <c>file</c>).
/// Replacing or removing deletes the previous image row in the same transaction (the clinic row is locked,
/// so concurrent uploads cannot orphan an image). Side effects: audit <c>clinic.logo.updated</c> /
/// <c>clinic.logo.removed</c> (removal of a missing logo is a no-op without audit).
/// </summary>
public sealed class ClinicLogoHandler(IOqTishDb db, IAccessService access, ITransactionRunner transactions, IRowLocks locks, IAuditLog audit, TimeProvider clock)
{
    /// <param name="upload">The uploaded bytes, or the transport error of reading the form (checked after authorization).</param>
    public async Task<Result<ClinicLogoDto>> SetAsync(Guid clinicId, Result<byte[]> upload, CancellationToken ct)
    {
        var allowed = await CheckAsync(clinicId, ct);
        if (!allowed.IsSuccess)
        {
            return allowed.Error;
        }

        if (!upload.IsSuccess)
        {
            return upload.Error;
        }

        var image = ImageRules.Validate(upload.Value);
        if (!image.IsSuccess)
        {
            return image.Error;
        }

        return await transactions.RunAsync<ClinicLogoDto>(async token =>
        {
            var clinic = await locks.LoadForUpdateAsync<Clinic>(clinicId, token);
            if (clinic is null)
            {
                return CommonErrors.NotFound;
            }

            var now = clock.GetUtcNow();
            var previous = clinic.LogoImageId;
            var stored = StoredImageStore.Add(db, image.Value, now);
            clinic.SetLogo(stored.Id, now);
            audit.Record(new AuditEvent("clinic.logo.updated", clinic.Id, TargetType: "clinic", TargetId: clinic.Id.ToString(),
                Details: new { imageId = stored.Id, previousImageId = previous, stored.ContentType, stored.SizeBytes }));
            await db.SaveChangesAsync(token);
            await StoredImageStore.DeleteAsync(db, previous, token);
            return new ClinicLogoDto(stored.Id);
        }, ct);
    }

    public async Task<Result> RemoveAsync(Guid clinicId, CancellationToken ct)
    {
        var allowed = await CheckAsync(clinicId, ct);
        if (!allowed.IsSuccess)
        {
            return allowed;
        }

        return await transactions.RunAsync(async token =>
        {
            var clinic = await locks.LoadForUpdateAsync<Clinic>(clinicId, token);
            if (clinic is null)
            {
                return CommonErrors.NotFound;
            }

            if (clinic.LogoImageId is not { } previous)
            {
                return Result.Success;
            }

            clinic.SetLogo(null, clock.GetUtcNow());
            audit.Record(new AuditEvent("clinic.logo.removed", clinic.Id, TargetType: "clinic", TargetId: clinic.Id.ToString(),
                Details: new { previousImageId = previous }));
            await db.SaveChangesAsync(token);
            await StoredImageStore.DeleteAsync(db, previous, token);
            return Result.Success;
        }, ct);
    }

    private async Task<Result> CheckAsync(Guid clinicId, CancellationToken ct)
    {
        if (!await db.Clinics.AsNoTracking().AnyAsync(c => c.Id == clinicId, ct))
        {
            return CommonErrors.NotFound;
        }

        return (await access.GetScopeAsync(ct)).CheckClinicWide(Permissions.ClinicProfileManage, clinicId);
    }
}
