using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OqTish.Domain.Identity;

namespace OqTish.Infrastructure.Persistence.Configurations;

internal static class Schemas
{
    public const string Identity = "identity";
    public const string Tenancy = "tenancy";
    public const string Staff = "staff";
    public const string Catalog = "catalog";
    public const string Scheduling = "scheduling";
    public const string Notify = "notify";
    public const string Audit = "audit";
    public const string Common = "common";
    public const string Booking = "booking";
    public const string Reviews = "reviews";
    public const string Reports = "reports";
    public const string Finance = "finance";
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users", Schemas.Identity);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.DisplayName).HasMaxLength(100);
        b.Property(x => x.Status).AsEnumText(b, "users", "status");
        b.Property(x => x.SecurityStamp).HasMaxLength(64);
        b.Property(x => x.StatusReason).HasMaxLength(500);
        b.Ignore(x => x.IsActive);
        b.HasIndex(x => x.Status);
    }
}

internal sealed class AuthIdentityConfiguration : IEntityTypeConfiguration<AuthIdentity>
{
    public void Configure(EntityTypeBuilder<AuthIdentity> b)
    {
        b.ToTable("auth_identities", Schemas.Identity);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Kind).AsEnumText(b, "auth_identities", "kind");
        b.Property(x => x.NormalizedIdentifier).HasMaxLength(254);
        b.HasIndex(x => new { x.Kind, x.NormalizedIdentifier }).IsUnique();
        b.HasIndex(x => new { x.UserId, x.Kind }).IsUnique();
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PasswordCredentialConfiguration : IEntityTypeConfiguration<PasswordCredential>
{
    public void Configure(EntityTypeBuilder<PasswordCredential> b)
    {
        b.ToTable("password_credentials", Schemas.Identity, t => t.HasCheckConstraint("ck_password_credentials_failed_attempts", "failed_attempts >= 0"));
        b.HasKey(x => x.UserId);
        b.Property(x => x.PasswordHash).HasMaxLength(512);
        b.HasOne<User>().WithOne().HasForeignKey<PasswordCredential>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> b)
    {
        b.ToTable("otp_challenges", Schemas.Identity, t =>
        {
            t.HasCheckConstraint("ck_otp_challenges_attempts", "attempts >= 0 AND attempts <= max_attempts");
            t.HasCheckConstraint("ck_otp_challenges_expiry", "expires_at > created_at");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Purpose).AsEnumText(b, "otp_challenges", "purpose");
        b.Property(x => x.IdentifierHash).HasMaxLength(64);
        b.Property(x => x.Phone).HasMaxLength(16);
        b.Property(x => x.CodeHash).HasMaxLength(64);
        b.Property(x => x.IpHash).HasMaxLength(64);
        b.HasIndex(x => new { x.IdentifierHash, x.CreatedAt });
        b.HasIndex(x => new { x.IpHash, x.CreatedAt });
        b.HasIndex(x => x.CreatedAt);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> b)
    {
        b.ToTable("sessions", Schemas.Identity);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TokenHash).HasMaxLength(64);
        b.Property(x => x.SecurityStamp).HasMaxLength(64);
        b.Property(x => x.AuthMethod).HasMaxLength(32);
        b.Property(x => x.RevokedReason).HasMaxLength(64);
        b.Property(x => x.IpHash).HasMaxLength(64);
        b.Property(x => x.UserAgent).HasMaxLength(256);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.UserId, x.RevokedAt });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TotpCredentialConfiguration : IEntityTypeConfiguration<TotpCredential>
{
    public void Configure(EntityTypeBuilder<TotpCredential> b)
    {
        b.ToTable("totp_credentials", Schemas.Identity);
        b.HasKey(x => x.UserId);
        b.Property(x => x.ProtectedSecret).HasMaxLength(1024);
        b.Ignore(x => x.IsEnabled);
        b.HasOne<User>().WithOne().HasForeignKey<TotpCredential>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PatientProfileConfiguration : IEntityTypeConfiguration<PatientProfile>
{
    public void Configure(EntityTypeBuilder<PatientProfile> b)
    {
        b.ToTable("patient_profiles", Schemas.Identity);
        b.HasKey(x => x.UserId);
        b.Property(x => x.FullName).HasMaxLength(PatientProfile.MaxNameLength);
        b.Property(x => x.ContactPhone).HasMaxLength(16);
        b.HasOne<User>().WithOne().HasForeignKey<PatientProfile>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DataProtectionKeyConfiguration : IEntityTypeConfiguration<DataProtectionKey>
{
    public void Configure(EntityTypeBuilder<DataProtectionKey> b) => b.ToTable("data_protection_keys", Schemas.Identity);
}
