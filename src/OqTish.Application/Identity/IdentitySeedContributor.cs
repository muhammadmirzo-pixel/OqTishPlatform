using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OqTish.Application.Common;
using OqTish.Application.Common.Security;
using OqTish.Application.Common.Seeding;
using OqTish.Application.Staff;
using OqTish.Domain.Identity;
using OqTish.Domain.Staff;

namespace OqTish.Application.Identity;

/// <summary>Configuration section <c>Seed</c> (demo data; Development/Staging only).</summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public string AdminEmail { get; set; } = "admin@oqtish.local";

    /// <summary>Required. appsettings.Development.json holds the dev-only default <c>Admin123!demo</c>.</summary>
    public string AdminPassword { get; set; } = string.Empty;

    public string DemoPatientPhone { get; set; } = "+998900000001";
}

/// <summary>
/// Seed order 100: a platform admin (e-mail + password, every platform permission) and a demo patient with a
/// verified phone. Fictional data only. Idempotent: existing accounts are kept (password is not reset) and
/// missing platform grants are added.
/// </summary>
public sealed class IdentitySeedContributor(
    IOqTishDb db,
    ITransactionRunner transactions,
    IPasswordHashService passwords,
    TimeProvider clock,
    IOptions<SeedOptions> options) : ISeedContributor
{
    public int Order => 100;

    public string Name => "identity";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.AdminPassword))
        {
            throw new InvalidOperationException("Seed:AdminPassword is not configured.");
        }

        if (!PhoneNumber.TryNormalize(o.DemoPatientPhone, out var demoPhone))
        {
            throw new InvalidOperationException("Seed:DemoPatientPhone is not a valid Uzbekistan number.");
        }

        var adminEmail = o.AdminEmail.Trim().ToLowerInvariant();
        var result = await transactions.RunAsync(async ct =>
        {
            var now = clock.GetUtcNow();
            var adminIdentity = await db.AuthIdentities
                .FirstOrDefaultAsync(i => i.Kind == AuthIdentityKind.EmailPassword && i.NormalizedIdentifier == adminEmail, ct);
            Guid adminId;
            if (adminIdentity is null)
            {
                var admin = User.Create("Platform Admin (demo)", now);
                db.Users.Add(admin);
                db.AuthIdentities.Add(AuthIdentity.Create(admin.Id, AuthIdentityKind.EmailPassword, adminEmail, verified: true, now));
                db.PasswordCredentials.Add(PasswordCredential.Create(admin.Id, passwords.Hash(o.AdminPassword), now));
                adminId = admin.Id;
            }
            else
            {
                adminId = adminIdentity.UserId;
            }

            var granted = await db.PlatformAdminGrants
                .Where(g => g.UserId == adminId && g.RevokedAt == null)
                .Select(g => g.Permission)
                .ToListAsync(ct);
            foreach (var permission in PlatformPermissions.All.Except(granted, StringComparer.Ordinal))
            {
                db.PlatformAdminGrants.Add(PlatformAdminGrant.Create(adminId, permission, null, now));
            }

            var patientExists = await db.AuthIdentities
                .AnyAsync(i => i.Kind == AuthIdentityKind.Phone && i.NormalizedIdentifier == demoPhone, ct);
            if (!patientExists)
            {
                var patient = User.Create("Demo Bemor", now);
                db.Users.Add(patient);
                db.AuthIdentities.Add(AuthIdentity.Create(patient.Id, AuthIdentityKind.Phone, demoPhone, verified: true, now));
                db.PatientProfiles.Add(PatientProfile.Create(patient.Id, "Demo Bemor", demoPhone, now));
            }

            await db.SaveChangesAsync(ct);
            return Result.Success;
        }, cancellationToken);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Identity seed failed: {result.Error.Code}");
        }
    }
}
