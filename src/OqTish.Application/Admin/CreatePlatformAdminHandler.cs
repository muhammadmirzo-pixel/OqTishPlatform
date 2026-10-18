using Microsoft.EntityFrameworkCore;
using OqTish.Application.Common;
using OqTish.Application.Common.Security;
using OqTish.Application.Identity;
using OqTish.Application.Staff;
using OqTish.Domain.Identity;
using OqTish.Domain.Staff;

namespace OqTish.Application.Admin;

public sealed record CreatePlatformAdminCommand(string? Email, string? Phone, string? Password);

/// <summary>What the bootstrap did (no secrets).</summary>
public sealed record CreatePlatformAdminOutcome(Guid UserId, bool Created, bool PhoneLinked, IReadOnlyList<string> GrantedPermissions);

/// <summary>
/// Production bootstrap (<c>dotnet OqTish.Api.dll create-platform-admin --email … --phone …</c>, any
/// environment): creates — or completes — a platform admin with an e-mail + password sign-in, a verified
/// phone (for password recovery) and every platform permission. Idempotent: an existing account keeps its
/// password; missing phone/grants are added. The password must be ≥ 12 characters with at least three of
/// lower case, upper case, digits and symbols, and must not contain the e-mail's local part. A phone or
/// e-mail that belongs to another account is refused (never merged). Side effects: audit
/// <c>platform.admin.bootstrapped</c> (actor System). TOTP must still be enabled by the admin before the
/// permissions take effect (SECURITY.md §8).
/// </summary>
public sealed class CreatePlatformAdminHandler(IOqTishDb db, ITransactionRunner transactions, IPasswordHashService passwords, IAuditLog audit, TimeProvider clock)
{
    public const int MinPasswordLength = 12;

    public async Task<Result<CreatePlatformAdminOutcome>> HandleAsync(CreatePlatformAdminCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var email = IdentifierRules.Email(command.Email);
        if (!email.IsSuccess)
        {
            return email.Error;
        }

        if (!PhoneNumber.TryNormalize(command.Phone, out var phone))
        {
            return Error.InvalidField("phone", "Enter an Uzbekistan mobile number (+998 and 9 digits).");
        }

        if (WeakPasswordReason(command.Password, email.Value) is { } weak)
        {
            return Error.InvalidField("password", weak);
        }

        return await transactions.RunAsync<CreatePlatformAdminOutcome>(async token =>
        {
            var now = clock.GetUtcNow();
            var emailIdentity = await db.AuthIdentities.FirstOrDefaultAsync(i => i.Kind == AuthIdentityKind.EmailPassword && i.NormalizedIdentifier == email.Value, token);
            var phoneIdentity = await db.AuthIdentities.FirstOrDefaultAsync(i => i.Kind == AuthIdentityKind.Phone && i.NormalizedIdentifier == phone, token);
            var created = false;
            Guid userId;
            if (emailIdentity is null)
            {
                if (phoneIdentity is not null)
                {
                    return AuthErrors.IdentityInUse;
                }

                var user = User.Create("Platform admin", now);
                db.Users.Add(user);
                db.AuthIdentities.Add(AuthIdentity.Create(user.Id, AuthIdentityKind.EmailPassword, email.Value, verified: true, now));
                db.PasswordCredentials.Add(PasswordCredential.Create(user.Id, passwords.Hash(command.Password!), now));
                userId = user.Id;
                created = true;
            }
            else
            {
                userId = emailIdentity.UserId;
                if (phoneIdentity is not null && phoneIdentity.UserId != userId)
                {
                    return AuthErrors.IdentityInUse;
                }

                if (!await db.Users.AnyAsync(u => u.Id == userId && u.Status == UserStatus.Active, token))
                {
                    return AuthErrors.AccountSuspended;
                }

                if (!await db.PasswordCredentials.AnyAsync(c => c.UserId == userId, token))
                {
                    db.PasswordCredentials.Add(PasswordCredential.Create(userId, passwords.Hash(command.Password!), now));
                }
            }

            var phoneLinked = false;
            if (phoneIdentity is null)
            {
                var otherPhone = await db.AuthIdentities.FirstOrDefaultAsync(i => i.UserId == userId && i.Kind == AuthIdentityKind.Phone, token);
                if (otherPhone is null)
                {
                    db.AuthIdentities.Add(AuthIdentity.Create(userId, AuthIdentityKind.Phone, phone, verified: true, now));
                    phoneLinked = true;
                }
                else
                {
                    return Error.InvalidField("phone", "The account already has a different phone number.");
                }
            }

            var granted = await db.PlatformAdminGrants.Where(g => g.UserId == userId && g.RevokedAt == null).Select(g => g.Permission).ToListAsync(token);
            var missing = PlatformPermissions.All.Except(granted, StringComparer.Ordinal).ToList();
            foreach (var permission in missing)
            {
                db.PlatformAdminGrants.Add(PlatformAdminGrant.Create(userId, permission, null, now));
            }

            audit.Record(new AuditEvent("platform.admin.bootstrapped", TargetType: "user", TargetId: userId.ToString(),
                Details: new { created, phoneLinked, granted = missing }));
            await db.SaveChangesAsync(token);
            return new CreatePlatformAdminOutcome(userId, created, phoneLinked, missing);
        }, ct);
    }

    /// <summary>Null when the password is acceptable, otherwise the reason (English, no secret echoed).</summary>
    public static string? WeakPasswordReason(string? password, string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength || password.Length > 128)
        {
            return $"Password must be {MinPasswordLength}–128 characters long.";
        }

        var classes = new[] { password.Any(char.IsAsciiLetterLower), password.Any(char.IsAsciiLetterUpper), password.Any(char.IsAsciiDigit), password.Any(c => !char.IsAsciiLetterOrDigit(c)) }.Count(x => x);
        if (classes < 3 || password.Distinct().Count() < 6)
        {
            return "Password is too weak: use at least three of lower case, upper case, digits and symbols.";
        }

        var local = email.Split('@')[0];
        if (local.Length >= 3 && password.Contains(local, StringComparison.OrdinalIgnoreCase))
        {
            return "Password must not contain the e-mail name.";
        }

        return null;
    }
}
