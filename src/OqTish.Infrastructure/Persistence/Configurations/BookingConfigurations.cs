using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OqTish.Domain.Booking;
using OqTish.Domain.Catalog;
using OqTish.Domain.Identity;
using OqTish.Domain.Reviews;
using OqTish.Domain.Scheduling;
using OqTish.Domain.Tenancy;

namespace OqTish.Infrastructure.Persistence.Configurations;

internal sealed class DoctorTimeBlockConfiguration : IEntityTypeConfiguration<DoctorTimeBlock>
{
    /// <summary>
    /// Created by raw SQL in migration <c>Booking</c> together with the generated column
    /// <c>period tstzrange GENERATED ALWAYS AS (tstzrange(starts_at, ends_at, '[)')) STORED</c>.
    /// </summary>
    public const string NoOverlapConstraint = "ex_doctor_time_blocks_no_overlap";

    public void Configure(EntityTypeBuilder<DoctorTimeBlock> b)
    {
        b.ToTable("doctor_time_blocks", Schemas.Scheduling, t =>
        {
            t.HasCheckConstraint("ck_doctor_time_blocks_interval", "ends_at > starts_at");
            t.HasCheckConstraint("ck_doctor_time_blocks_kind_refs",
                "(kind = 'TimeOff' AND appointment_id IS NULL AND branch_id IS NULL) OR (kind <> 'TimeOff' AND appointment_id IS NOT NULL AND branch_id IS NOT NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Kind).AsEnumText(b, "doctor_time_blocks", "kind");
        b.Property(x => x.Reason).HasMaxLength(DoctorTimeBlock.MaxReasonLength);
        b.HasIndex(x => x.AppointmentId).IsUnique().HasFilter("appointment_id IS NOT NULL");
        b.HasIndex(x => new { x.DoctorId, x.StartsAt }).HasFilter("is_active");
        b.HasIndex(x => new { x.ClinicId, x.BranchId, x.StartsAt });
        b.HasOne<Doctor>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.DoctorId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DoctorBranch>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.DoctorId, x.BranchId })
            .HasPrincipalKey(x => new { x.ClinicId, x.DoctorId, x.BranchId })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Appointment>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.AppointmentId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> b)
    {
        b.ToTable("appointments", Schemas.Booking, t =>
        {
            t.HasCheckConstraint("ck_appointments_interval", "ends_at > starts_at");
            t.HasCheckConstraint("ck_appointments_doctor_required", "status NOT IN ('Confirmed', 'Completed', 'NoShow') OR doctor_id IS NOT NULL");
            t.HasCheckConstraint("ck_appointments_pending_deadline", "status <> 'Pending' OR expires_at IS NOT NULL");
            t.HasCheckConstraint("ck_appointments_quoted_price", "quoted_price >= 0");
            t.HasCheckConstraint("ck_appointments_charged_amount", "(charged_amount IS NULL OR charged_amount >= 0) AND ((status = 'Completed') = (charged_amount IS NOT NULL))");
            t.HasCheckConstraint("ck_appointments_source_patient", "(source = 'Patient') = (patient_user_id IS NOT NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasAlternateKey(x => new { x.ClinicId, x.Id });
        b.Property(x => x.Status).AsEnumText(b, "appointments", "status");
        b.Property(x => x.Source).AsEnumText(b, "appointments", "source");
        b.Property(x => x.PriceKind).AsEnumText(b, "appointments", "price_kind");
        b.Property(x => x.Currency).AsEnumText(b, "appointments", "currency").HasMaxLength(3).IsFixedLength();
        b.Property(x => x.QuotedPrice).HasPrecision(19, 2);
        b.Property(x => x.ChargedAmount).HasPrecision(19, 2);
        b.Property(x => x.ContactName).HasMaxLength(Appointment.MaxContactNameLength);
        b.Property(x => x.ContactPhone).HasMaxLength(16);
        b.Property(x => x.ReasonCode).HasMaxLength(Appointment.MaxReasonCodeLength);
        b.Property(x => x.Version).IsRowVersion();
        b.Ignore(x => x.QuotedPriceMoney);
        b.Ignore(x => x.ChargedMoney);

        b.HasIndex(x => new { x.BranchId, x.StartsAt });
        b.HasIndex(x => new { x.PatientUserId, x.CreatedAt }).IsDescending(false, true);
        b.HasIndex(x => x.ExpiresAt).HasFilter("status = 'Pending'").HasDatabaseName("ix_appointments_pending_expires_at");
        b.HasIndex(x => new { x.ClinicId, x.Status, x.StartsAt });
        b.HasIndex(x => new { x.DoctorId, x.StartsAt });

        b.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.BranchId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BranchService>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.BranchId, x.ServiceId })
            .HasPrincipalKey(x => new { x.ClinicId, x.BranchId, x.ServiceId })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DoctorBranch>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.DoctorId, x.BranchId })
            .HasPrincipalKey(x => new { x.ClinicId, x.DoctorId, x.BranchId })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DoctorService>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.DoctorId, x.ServiceId })
            .HasPrincipalKey(x => new { x.ClinicId, x.DoctorId, x.ServiceId })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.PatientUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AppointmentStatusHistoryConfiguration : IEntityTypeConfiguration<AppointmentStatusHistory>
{
    public void Configure(EntityTypeBuilder<AppointmentStatusHistory> b)
    {
        // Append-only: trigger + REVOKE in migration Booking.
        b.ToTable("appointment_status_history", Schemas.Booking);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.From).AsEnumText(b, "appointment_status_history", "from_status").HasColumnName("from_status");
        b.Property(x => x.To).AsEnumText(b, "appointment_status_history", "to_status").HasColumnName("to_status");
        b.Property(x => x.ActorKind).AsEnumText(b, "appointment_status_history", "actor_kind");
        b.Property(x => x.ReasonCode).HasMaxLength(Appointment.MaxReasonCodeLength);
        b.HasIndex(x => new { x.AppointmentId, x.OccurredAt });
        b.HasOne<Appointment>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.AppointmentId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> b)
    {
        b.ToTable("reviews", Schemas.Reviews, t =>
        {
            t.HasCheckConstraint("ck_reviews_rating", "rating BETWEEN 1 AND 5");
            t.HasCheckConstraint("ck_reviews_text_length", $"char_length(text) <= {Review.MaxTextLength}");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Text).HasMaxLength(Review.MaxTextLength);
        b.Property(x => x.ModerationStatus).AsEnumText(b, "reviews", "moderation_status");
        b.Property(x => x.ModerationReason).HasMaxLength(Review.MaxModerationReasonLength);
        b.HasIndex(x => x.AppointmentId).IsUnique();
        b.HasIndex(x => new { x.ClinicId, x.ModerationStatus, x.CreatedAt });
        b.HasIndex(x => new { x.ModerationStatus, x.CreatedAt });
        b.HasOne<Appointment>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.AppointmentId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.BranchId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.PatientUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AbuseReportConfiguration : IEntityTypeConfiguration<AbuseReport>
{
    public void Configure(EntityTypeBuilder<AbuseReport> b)
    {
        b.ToTable("abuse_reports", Schemas.Reports);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TargetType).AsEnumText(b, "abuse_reports", "target_type");
        b.Property(x => x.Status).AsEnumText(b, "abuse_reports", "status");
        b.Property(x => x.Reason).HasMaxLength(AbuseReport.MaxReasonLength);
        b.Property(x => x.Resolution).HasMaxLength(AbuseReport.MaxResolutionLength);
        b.HasIndex(x => new { x.Status, x.CreatedAt });
        b.HasIndex(x => new { x.ReporterUserId, x.TargetType, x.TargetId }).IsUnique().HasFilter("status = 'Open'")
            .HasDatabaseName("ux_abuse_reports_open_per_reporter_target");
        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ReporterUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
