using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OqTish.Domain.Booking;
using OqTish.Domain.Finance;
using OqTish.Domain.Identity;
using OqTish.Domain.Tenancy;

namespace OqTish.Infrastructure.Persistence.Configurations;

/// <summary>
/// finance schema (BE3, migration <c>Finance</c>). Raw SQL in the migration adds: the commission-rule range column +
/// exclusion constraint and its "only end" trigger, the append-only triggers + REVOKE on journal_entries /
/// ledger_lines, and the deferred balance constraint triggers.
/// </summary>
internal sealed class ServiceCompletionConfiguration : IEntityTypeConfiguration<ServiceCompletion>
{
    public void Configure(EntityTypeBuilder<ServiceCompletion> b)
    {
        b.ToTable("service_completions", Schemas.Finance, t => t.HasCheckConstraint("ck_service_completions_charged_amount", "charged_amount >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasAlternateKey(x => new { x.ClinicId, x.Id });
        b.Property(x => x.ChargedAmount).HasPrecision(19, 2);
        b.Property(x => x.Currency).AsEnumText(b, "service_completions", "currency").HasMaxLength(3).IsFixedLength();
        b.Ignore(x => x.ChargedMoney);
        b.HasIndex(x => x.AppointmentId).IsUnique();
        b.HasIndex(x => new { x.ClinicId, x.PatientUserId });
        b.HasIndex(x => new { x.BranchId, x.PostingDateLocal });
        b.HasOne<Appointment>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.AppointmentId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.BranchId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.PatientUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CompletedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments", Schemas.Finance, t =>
        {
            t.HasCheckConstraint("ck_payments_amount_positive", "amount > 0");
            t.HasCheckConstraint("ck_payments_refund_link", "(direction = 'Refund') = (refund_of_payment_id IS NOT NULL)");
            t.HasCheckConstraint("ck_payments_refund_recorded", "direction <> 'Refund' OR status = 'Recorded'");
            t.HasCheckConstraint("ck_payments_failed_fields", "(status = 'Failed') = (failed_at IS NOT NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasAlternateKey(x => new { x.ClinicId, x.Id });
        b.Property(x => x.Direction).AsEnumText(b, "payments", "direction");
        b.Property(x => x.Method).AsEnumText(b, "payments", "method");
        b.Property(x => x.Status).AsEnumText(b, "payments", "status");
        b.Property(x => x.CollectedBy).AsEnumText(b, "payments", "collected_by");
        b.Property(x => x.Currency).AsEnumText(b, "payments", "currency").HasMaxLength(3).IsFixedLength();
        b.Property(x => x.Amount).HasPrecision(19, 2);
        b.Property(x => x.IdempotencyKey).HasMaxLength(64);
        b.Property(x => x.ExternalRef).HasMaxLength(Payment.MaxExternalRefLength);
        b.Property(x => x.Reason).HasMaxLength(Payment.MaxReasonLength);
        b.Property(x => x.FailureReason).HasMaxLength(Payment.MaxReasonLength);
        b.Ignore(x => x.AmountMoney);
        b.HasIndex(x => new { x.ClinicId, x.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL")
            .HasDatabaseName("ux_payments_clinic_idempotency_key");
        b.HasIndex(x => new { x.ClinicId, x.BranchId, x.RecordedAt });
        b.HasIndex(x => x.AppointmentId);
        b.HasIndex(x => x.RefundOfPaymentId);
        b.HasOne<Appointment>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.AppointmentId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.BranchId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Payment>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.RefundOfPaymentId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.FailedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PaymentAllocationConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> b)
    {
        b.ToTable("payment_allocations", Schemas.Finance, t => t.HasCheckConstraint("ck_payment_allocations_amount_positive", "amount > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Amount).HasPrecision(19, 2);
        b.Property(x => x.Currency).AsEnumText(b, "payment_allocations", "currency").HasMaxLength(3).IsFixedLength();
        b.HasIndex(x => x.PaymentId);
        b.HasIndex(x => x.AppointmentId);
        b.HasOne<Payment>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.PaymentId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Appointment>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.AppointmentId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ServiceCompletion>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.ServiceCompletionId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CommissionRuleConfiguration : IEntityTypeConfiguration<CommissionRule>
{
    /// <summary>Created by raw SQL in migration <c>Finance</c> on the generated column <c>effective tstzrange</c>.</summary>
    public const string NoOverlapConstraint = "ex_commission_rules_no_overlap";

    public void Configure(EntityTypeBuilder<CommissionRule> b)
    {
        b.ToTable("commission_rules", Schemas.Finance, t =>
        {
            t.HasCheckConstraint("ck_commission_rules_amount_by_basis",
                "(basis IN ('CollectedPayment', 'CompletedService') AND rate IS NOT NULL AND rate > 0 AND rate <= 1 AND fixed_fee IS NULL) OR " +
                "(basis IN ('ConfirmedAppointment', 'NewPatient') AND fixed_fee IS NOT NULL AND fixed_fee > 0 AND rate IS NULL)");
            t.HasCheckConstraint("ck_commission_rules_range", "effective_to IS NULL OR effective_to > effective_from");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasAlternateKey(x => new { x.ClinicId, x.Id });
        b.Property(x => x.Basis).AsEnumText(b, "commission_rules", "basis");
        b.Property(x => x.Rounding).AsEnumText(b, "commission_rules", "rounding");
        b.Property(x => x.Currency).AsEnumText(b, "commission_rules", "currency").HasMaxLength(3).IsFixedLength();
        b.Property(x => x.Rate).HasPrecision(7, 4);
        b.Property(x => x.FixedFee).HasPrecision(19, 2);
        b.Property(x => x.ContractReference).HasMaxLength(CommissionRule.MaxContractReferenceLength);
        b.Ignore(x => x.FixedFeeMoney);
        b.HasIndex(x => new { x.ClinicId, x.Basis, x.EffectiveFrom });
        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.EndedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> b)
    {
        // Append-only (trigger + REVOKE) and balanced (deferred constraint trigger): migration Finance.
        b.ToTable("journal_entries", Schemas.Finance);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasAlternateKey(x => new { x.ClinicId, x.Id });
        b.Property(x => x.EntryType).AsEnumText(b, "journal_entries", "entry_type");
        b.Property(x => x.SourceEventKey).HasMaxLength(JournalEntry.MaxSourceEventKeyLength);
        b.Property(x => x.Description).HasMaxLength(JournalEntry.MaxDescriptionLength);
        b.Property(x => x.Sequence).UseIdentityAlwaysColumn();
        b.HasIndex(x => x.SourceEventKey).IsUnique();
        b.HasIndex(x => x.Sequence).IsUnique();
        b.HasIndex(x => new { x.ClinicId, x.BranchId, x.PostingDateLocal });
        b.HasIndex(x => new { x.ClinicId, x.EntryType, x.PostingDateLocal });
        b.HasIndex(x => x.AppointmentId);
        b.HasIndex(x => x.PaymentId);
        b.HasIndex(x => x.ReversesEntryId);
        b.HasMany(x => x.Lines).WithOne()
            .HasForeignKey(x => new { x.ClinicId, x.JournalEntryId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.BranchId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<JournalEntry>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.ReversesEntryId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Appointment>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.AppointmentId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Payment>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.PaymentId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ServiceCompletion>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.ServiceCompletionId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Settlement>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.SettlementId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LedgerLineConfiguration : IEntityTypeConfiguration<LedgerLine>
{
    public void Configure(EntityTypeBuilder<LedgerLine> b)
    {
        b.ToTable("ledger_lines", Schemas.Finance, t =>
        {
            t.HasCheckConstraint("ck_ledger_lines_amount_nonzero", "amount <> 0");
            t.HasCheckConstraint("ck_ledger_lines_rule_snapshot", "commission_rule_id IS NOT NULL OR (rate_snapshot IS NULL AND basis_amount_snapshot IS NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasAlternateKey(x => new { x.ClinicId, x.Id });
        b.Property(x => x.Account).AsEnumText(b, "ledger_lines", "account");
        b.Property(x => x.Party).AsEnumText(b, "ledger_lines", "party");
        b.Property(x => x.Currency).AsEnumText(b, "ledger_lines", "currency").HasMaxLength(3).IsFixedLength();
        b.Property(x => x.Amount).HasPrecision(19, 2);
        b.Property(x => x.RateSnapshot).HasPrecision(7, 4);
        b.Property(x => x.BasisAmountSnapshot).HasPrecision(19, 2);
        b.Ignore(x => x.AmountMoney);
        b.HasIndex(x => x.JournalEntryId);
        b.HasIndex(x => new { x.ClinicId, x.Account });
        b.HasOne<CommissionRule>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.CommissionRuleId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SettlementConfiguration : IEntityTypeConfiguration<Settlement>
{
    public void Configure(EntityTypeBuilder<Settlement> b)
    {
        b.ToTable("settlements", Schemas.Finance, t =>
        {
            t.HasCheckConstraint("ck_settlements_period_month", "extract(day from period_start) = 1 AND period_end = (period_start + interval '1 month')::date");
            t.HasCheckConstraint("ck_settlements_finalized_fields", "status NOT IN ('Finalized', 'Paid') OR finalized_at IS NOT NULL");
            t.HasCheckConstraint("ck_settlements_paid_fields", "(status = 'Paid') = (paid_at IS NOT NULL)");
            t.HasCheckConstraint("ck_settlements_voided_fields", "(status = 'Voided') = (voided_at IS NOT NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasAlternateKey(x => new { x.ClinicId, x.Id });
        b.Property(x => x.Status).AsEnumText(b, "settlements", "status");
        b.Property(x => x.Currency).AsEnumText(b, "settlements", "currency").HasMaxLength(3).IsFixedLength();
        b.Property(x => x.AmountDue).HasPrecision(19, 2);
        b.Property(x => x.PaymentTerms).HasMaxLength(Settlement.MaxPaymentTermsLength);
        b.Property(x => x.PaidReference).HasMaxLength(Settlement.MaxReferenceLength);
        b.Property(x => x.VoidReason).HasMaxLength(Settlement.MaxReasonLength);
        b.Ignore(x => x.AmountDueMoney);
        b.Ignore(x => x.Year);
        b.Ignore(x => x.Month);
        b.Ignore(x => x.LocksPeriod);
        b.HasIndex(x => new { x.ClinicId, x.PeriodStart }).IsUnique().HasFilter("status <> 'Voided'").HasDatabaseName("ux_settlements_clinic_period_active");
        b.HasIndex(x => new { x.Status, x.PeriodStart });
        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SettlementItemConfiguration : IEntityTypeConfiguration<SettlementItem>
{
    public void Configure(EntityTypeBuilder<SettlementItem> b)
    {
        b.ToTable("settlement_items", Schemas.Finance);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Amount).HasPrecision(19, 2);
        b.Property(x => x.Currency).AsEnumText(b, "settlement_items", "currency").HasMaxLength(3).IsFixedLength();
        b.HasIndex(x => x.LedgerLineId).IsUnique().HasDatabaseName("ix_settlement_items_ledger_line_id");
        b.HasIndex(x => x.SettlementId);
        b.HasOne<Settlement>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.SettlementId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LedgerLine>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.LedgerLineId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DailyFinancialSummaryConfiguration : IEntityTypeConfiguration<DailyFinancialSummary>
{
    public void Configure(EntityTypeBuilder<DailyFinancialSummary> b)
    {
        b.ToTable("daily_financial_summaries", Schemas.Finance, t => t.HasCheckConstraint("ck_daily_financial_summaries_visits", "visits >= 0"));
        b.HasKey(x => new { x.BranchId, x.LocalDate });
        b.Property(x => x.Currency).AsEnumText(b, "daily_financial_summaries", "currency").HasMaxLength(3).IsFixedLength();
        b.Property(x => x.Charged).HasPrecision(19, 2);
        b.Property(x => x.Collected).HasPrecision(19, 2);
        b.Property(x => x.Refunded).HasPrecision(19, 2);
        b.Property(x => x.PriorPeriodRefunded).HasPrecision(19, 2);
        b.Property(x => x.Commission).HasPrecision(19, 2);
        b.Ignore(x => x.Totals);
        b.HasIndex(x => new { x.ClinicId, x.LocalDate });
        b.HasOne<Branch>().WithMany()
            .HasForeignKey(x => new { x.ClinicId, x.BranchId })
            .HasPrincipalKey(x => new { x.ClinicId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
