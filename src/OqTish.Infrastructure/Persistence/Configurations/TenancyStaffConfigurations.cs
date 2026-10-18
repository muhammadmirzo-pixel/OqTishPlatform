using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OqTish.Domain.Identity;
using OqTish.Domain.Staff;
using OqTish.Domain.Tenancy;

namespace OqTish.Infrastructure.Persistence.Configurations;

internal sealed class ClinicConfiguration : IEntityTypeConfiguration<Clinic>
{
    public void Configure(EntityTypeBuilder<Clinic> b)
    {
        b.ToTable("clinics", Schemas.Tenancy);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.LegalName).HasMaxLength(300);
        b.Property(x => x.Description).HasMaxLength(4000);
        b.Property(x => x.Phone).HasMaxLength(16);
        b.Property(x => x.VerificationStatus).AsEnumText(b, "clinics", "verification_status");
        b.Property(x => x.StatusReason).HasMaxLength(1000);
        b.Ignore(x => x.IsSuspended);
        b.HasIndex(x => x.VerificationStatus);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> b)
    {
        b.ToTable("branches", Schemas.Tenancy, t =>
        {
            t.HasCheckConstraint("ck_branches_latitude", "latitude IS NULL OR (latitude >= -90 AND latitude <= 90)");
            t.HasCheckConstraint("ck_branches_longitude", "longitude IS NULL OR (longitude >= -180 AND longitude <= 180)");
            t.HasCheckConstraint("ck_branches_lat_lng_pair", "(latitude IS NULL) = (longitude IS NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasAlternateKey(x => new { x.ClinicId, x.Id });
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.District).HasMaxLength(100);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.Latitude).HasPrecision(9, 6);
        b.Property(x => x.Longitude).HasPrecision(9, 6);
        b.Property(x => x.Phone).HasMaxLength(16);
        b.Property(x => x.TimeZone).HasMaxLength(64).HasDefaultValue(Branch.DefaultTimeZone);
        b.HasIndex(x => new { x.ClinicId, x.IsActive });
        b.HasIndex(x => x.District);
        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BranchOpeningHoursConfiguration : IEntityTypeConfiguration<BranchOpeningHours>
{
    public void Configure(EntityTypeBuilder<BranchOpeningHours> b)
    {
        b.ToTable("branch_opening_hours", Schemas.Tenancy, t =>
        {
            t.HasCheckConstraint("ck_branch_opening_hours_weekday", "weekday BETWEEN 1 AND 7");
            t.HasCheckConstraint("ck_branch_opening_hours_interval", "opens < closes");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => new { x.BranchId, x.Weekday, x.Opens }).IsUnique();
        b.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.BranchId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BranchBookingLockConfiguration : IEntityTypeConfiguration<BranchBookingLock>
{
    public void Configure(EntityTypeBuilder<BranchBookingLock> b)
    {
        b.ToTable("branch_booking_locks", Schemas.Tenancy);
        b.HasKey(x => x.BranchId);
        b.Property(x => x.BranchId).ValueGeneratedNever();
        b.HasOne<Branch>().WithOne()
            .HasForeignKey<BranchBookingLock>(x => new { x.ClinicId, x.BranchId })
            .HasPrincipalKey<Branch>(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ClinicMembershipConfiguration : IEntityTypeConfiguration<ClinicMembership>
{
    public void Configure(EntityTypeBuilder<ClinicMembership> b)
    {
        b.ToTable("clinic_memberships", Schemas.Staff);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasAlternateKey(x => new { x.ClinicId, x.Id });
        b.Property(x => x.Role).AsEnumText(b, "clinic_memberships", "role");
        b.Property(x => x.Scope).AsEnumText(b, "clinic_memberships", "scope");
        b.Property(x => x.Status).AsEnumText(b, "clinic_memberships", "status");
        b.HasIndex(x => new { x.ClinicId, x.UserId }).IsUnique();
        b.HasIndex(x => new { x.UserId, x.Status });
        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BranchAccessConfiguration : IEntityTypeConfiguration<BranchAccess>
{
    public void Configure(EntityTypeBuilder<BranchAccess> b)
    {
        b.ToTable("branch_access", Schemas.Staff);
        b.HasKey(x => new { x.MembershipId, x.BranchId });
        b.HasIndex(x => new { x.ClinicId, x.BranchId });
        b.HasOne<ClinicMembership>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.MembershipId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.BranchId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class StaffInvitationConfiguration : IEntityTypeConfiguration<StaffInvitation>
{
    public void Configure(EntityTypeBuilder<StaffInvitation> b)
    {
        b.ToTable("staff_invitations", Schemas.Staff, t =>
        {
            t.HasCheckConstraint("ck_staff_invitations_expiry", "expires_at > created_at");
            t.HasCheckConstraint("ck_staff_invitations_branches", "scope <> 'Branches' OR cardinality(branch_ids) > 0");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.PhoneHash).HasMaxLength(64);
        b.Property(x => x.PhoneMasked).HasMaxLength(16);
        b.Property(x => x.Role).AsEnumText(b, "staff_invitations", "role");
        b.Property(x => x.Scope).AsEnumText(b, "staff_invitations", "scope");
        b.Property(x => x.BranchIds).HasColumnType("uuid[]");
        b.HasIndex(x => new { x.ClinicId, x.CreatedAt });
        b.HasIndex(x => x.PhoneHash).HasFilter("accepted_at IS NULL AND revoked_at IS NULL");
        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.InvitedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.AcceptedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PlatformAdminGrantConfiguration : IEntityTypeConfiguration<PlatformAdminGrant>
{
    public void Configure(EntityTypeBuilder<PlatformAdminGrant> b)
    {
        b.ToTable("platform_admin_grants", Schemas.Staff);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Permission).HasMaxLength(64);
        b.HasIndex(x => new { x.UserId, x.Permission }).IsUnique().HasFilter("revoked_at IS NULL");
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.GrantedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.RevokedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
