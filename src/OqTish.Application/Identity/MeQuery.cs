using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Staff;
using OqTish.Domain.Identity;

namespace OqTish.Application.Identity;

/// <summary>
/// GET /api/me. Builds <see cref="MeDto"/> for a user: identities (phone masked), patient profile, TOTP
/// state and the effective permissions per membership from <see cref="IAccessService"/>.
/// Auth: user. Side effects: none.
/// </summary>
public sealed class MeQuery(IOqTishDb db, IAccessService access, ICurrentUser currentUser)
{
    public async Task<Result<MeDto>> HandleAsync(CancellationToken ct) =>
        currentUser.UserId is { } userId && await BuildAsync(userId, ct) is { } me ? me : AuthErrors.Unauthenticated;

    public async Task<MeDto?> BuildAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return null;
        }

        var identities = await db.AuthIdentities.AsNoTracking().Where(i => i.UserId == userId).ToListAsync(ct);
        var patient = await db.PatientProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var scope = await access.GetScopeForUserAsync(userId, ct);

        var phone = identities.FirstOrDefault(i => i.Kind == AuthIdentityKind.Phone);
        var email = identities.FirstOrDefault(i => i.Kind == AuthIdentityKind.EmailPassword);
        var phoneMasked = phone is null ? null : PhoneNumber.Mask(phone.NormalizedIdentifier);

        var displayName = user.DisplayName
            ?? (string.IsNullOrWhiteSpace(patient?.FullName) ? null : patient.FullName)
            ?? phoneMasked
            ?? email?.NormalizedIdentifier
            ?? "OqTish";

        var memberships = scope.Memberships
            .Select(m => new MembershipDto(
                m.MembershipId,
                m.ClinicId,
                m.ClinicName,
                m.ClinicStatus,
                m.Role,
                m.Scope,
                m.AccessibleBranchIds,
                [.. Permissions.All.Where(m.EffectivePermissions.ContainsKey)],
                m.TotpBlocked))
            .ToList();

        return new MeDto(
            user.Id,
            displayName,
            PhoneVerified: phone?.VerifiedAt is not null,
            phoneMasked,
            email?.NormalizedIdentifier,
            TelegramLinked: identities.Any(i => i.Kind == AuthIdentityKind.Telegram),
            scope.TotpEnabled,
            scope.TotpSetupRequired,
            patient is null || patient.IsAnonymised ? null : new PatientDto(patient.FullName, patient.ContactPhone),
            memberships,
            scope.EffectivePlatformPermissions);
    }
}
