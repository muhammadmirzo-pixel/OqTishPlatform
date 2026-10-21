using OqTish.Application.Common;
using OqTish.Application.Common.Images;
using OqTish.Domain.Identity;

namespace OqTish.Application.Identity.Handlers;

/// <summary>PUT /api/me/avatar: the new avatar image id (served by GET /api/files/images/{id}).</summary>
public sealed record AvatarDto(Guid AvatarId);

/// <summary>
/// PUT /api/me/avatar (multipart, field <c>file</c>) and DELETE. Auth: user (own account only). The image is
/// validated by <see cref="ImageRules"/> (400 <c>common.validation</c>, field <c>file</c>). Replacing or
/// removing deletes the previous image row in the same transaction (the user row is locked). Side effects:
/// none besides the rows (no audit, like PUT /me/profile: not security relevant). Account deletion clears
/// the avatar too (<see cref="DeleteAccountHandler"/>).
/// </summary>
public sealed class AvatarHandler(IOqTishDb db, ICurrentUser currentUser, ITransactionRunner transactions, IRowLocks locks, TimeProvider clock)
{
    /// <param name="upload">The uploaded bytes, or the transport error of reading the form.</param>
    public async Task<Result<AvatarDto>> SetAsync(Result<byte[]> upload, CancellationToken ct)
    {
        if (!upload.IsSuccess)
        {
            return upload.Error;
        }

        var image = ImageRules.Validate(upload.Value);
        if (!image.IsSuccess)
        {
            return image.Error;
        }

        var userId = currentUser.RequiredUserId;
        return await transactions.RunAsync<AvatarDto>(async token =>
        {
            var user = await locks.LoadForUpdateAsync<User>(userId, token);
            if (user is null)
            {
                return AuthErrors.Unauthenticated;
            }

            var now = clock.GetUtcNow();
            var previous = user.AvatarImageId;
            var stored = StoredImageStore.Add(db, image.Value, now);
            user.SetAvatar(stored.Id, now);
            await db.SaveChangesAsync(token);
            await StoredImageStore.DeleteAsync(db, previous, token);
            return new AvatarDto(stored.Id);
        }, ct);
    }

    public async Task<Result> RemoveAsync(CancellationToken ct)
    {
        var userId = currentUser.RequiredUserId;
        return await transactions.RunAsync(async token =>
        {
            var user = await locks.LoadForUpdateAsync<User>(userId, token);
            if (user is null)
            {
                return AuthErrors.Unauthenticated;
            }

            if (user.AvatarImageId is not { } previous)
            {
                return Result.Success;
            }

            user.SetAvatar(null, clock.GetUtcNow());
            await db.SaveChangesAsync(token);
            await StoredImageStore.DeleteAsync(db, previous, token);
            return Result.Success;
        }, ct);
    }
}
