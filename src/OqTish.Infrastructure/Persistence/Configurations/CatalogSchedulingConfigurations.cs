using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OqTish.Domain.Catalog;
using OqTish.Domain.Common;
using OqTish.Domain.Identity;
using OqTish.Domain.Scheduling;
using OqTish.Domain.Tenancy;

namespace OqTish.Infrastructure.Persistence.Configurations;

internal sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> b)
    {
        b.ToTable("services", Schemas.Catalog);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasAlternateKey(x => new { x.ClinicId, x.Id });
        b.Property(x => x.Name).HasMaxLength(Service.MaxNameLength);
        b.Property(x => x.Category).HasMaxLength(32);
        b.HasIndex(x => new { x.ClinicId, x.IsActive });
        b.HasIndex(x => x.Category);
        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BranchServiceConfiguration : IEntityTypeConfiguration<BranchService>
{
    public void Configure(EntityTypeBuilder<BranchService> b)
    {
        b.ToTable("branch_services", Schemas.Catalog, t =>
        {
            t.HasCheckConstraint("ck_branch_services_price", "price >= 0");
            t.HasCheckConstraint("ck_branch_services_duration", $"duration_minutes BETWEEN {BranchService.MinDurationMinutes} AND {BranchService.MaxDurationMinutes}");
        });
        b.HasKey(x => new { x.BranchId, x.ServiceId });
        b.HasAlternateKey(x => new { x.ClinicId, x.BranchId, x.ServiceId });
        b.Property(x => x.Price).HasPrecision(19, 2);
        b.Property(x => x.Currency).AsEnumText(b, "branch_services", "currency").HasMaxLength(3).IsFixedLength();
        b.Property(x => x.PriceKind).AsEnumText(b, "branch_services", "price_kind");
        b.Ignore(x => x.PriceMoney);
        b.HasIndex(x => new { x.ClinicId, x.ServiceId });
        b.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.BranchId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Service>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.ServiceId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DoctorConfiguration : IEntityTypeConfiguration<Doctor>
{
    public void Configure(EntityTypeBuilder<Doctor> b)
    {
        b.ToTable("doctors", Schemas.Scheduling);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasAlternateKey(x => new { x.ClinicId, x.Id });
        b.Property(x => x.DisplayName).HasMaxLength(Doctor.MaxNameLength);
        b.HasIndex(x => new { x.ClinicId, x.UserId }).IsUnique().HasFilter("user_id IS NOT NULL");
        b.HasIndex(x => x.UserId).HasFilter("user_id IS NOT NULL");
        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DoctorBranchConfiguration : IEntityTypeConfiguration<DoctorBranch>
{
    public void Configure(EntityTypeBuilder<DoctorBranch> b)
    {
        b.ToTable("doctor_branches", Schemas.Scheduling);
        b.HasKey(x => new { x.DoctorId, x.BranchId });
        b.HasAlternateKey(x => new { x.ClinicId, x.DoctorId, x.BranchId });
        b.HasIndex(x => new { x.ClinicId, x.BranchId });
        b.HasOne<Doctor>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.DoctorId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.BranchId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DoctorServiceConfiguration : IEntityTypeConfiguration<DoctorService>
{
    public void Configure(EntityTypeBuilder<DoctorService> b)
    {
        b.ToTable("doctor_services", Schemas.Scheduling);
        b.HasKey(x => new { x.DoctorId, x.ServiceId });
        b.HasAlternateKey(x => new { x.ClinicId, x.DoctorId, x.ServiceId });
        b.HasIndex(x => new { x.ClinicId, x.ServiceId });
        b.HasOne<Doctor>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.DoctorId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Service>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.ServiceId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WorkingHoursConfiguration : IEntityTypeConfiguration<WorkingHours>
{
    /// <summary>Exclusion constraint created by raw SQL in migration <c>CatalogScheduling</c>.</summary>
    public const string NoOverlapConstraint = "ex_working_hours_no_overlap";

    public void Configure(EntityTypeBuilder<WorkingHours> b)
    {
        b.ToTable("working_hours", Schemas.Scheduling, t =>
        {
            t.HasCheckConstraint("ck_working_hours_weekday", "weekday BETWEEN 1 AND 7");
            t.HasCheckConstraint("ck_working_hours_interval", "start_local < end_local");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => new { x.DoctorId, x.Weekday });
        b.HasIndex(x => new { x.ClinicId, x.BranchId, x.Weekday });
        b.HasOne<DoctorBranch>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.DoctorId, x.BranchId })
            .HasPrincipalKey(x => new { x.ClinicId, x.DoctorId, x.BranchId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
