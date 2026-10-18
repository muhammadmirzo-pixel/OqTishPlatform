using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OqTish.Domain.Audit;
using OqTish.Domain.Common;
using OqTish.Domain.Identity;
using OqTish.Domain.Notifications;

namespace OqTish.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages", Schemas.Notify, t => t.HasCheckConstraint("ck_outbox_messages_attempts", "attempts >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Type).HasMaxLength(100);
        b.Property(x => x.DedupKey).HasMaxLength(200);
        b.Property(x => x.Payload).HasColumnType("jsonb");
        b.Property(x => x.Status).AsEnumText(b, "outbox_messages", "status");
        b.Property(x => x.LastError).HasMaxLength(500);
        b.HasIndex(x => x.DedupKey).IsUnique();
        b.HasIndex(x => x.NextAttemptAt).HasFilter("status = 'Pending'").HasDatabaseName("ix_outbox_messages_due");
    }
}

internal sealed class TelegramLinkTokenConfiguration : IEntityTypeConfiguration<TelegramLinkToken>
{
    public void Configure(EntityTypeBuilder<TelegramLinkToken> b)
    {
        b.ToTable("telegram_link_tokens", Schemas.Notify);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TokenHash).HasMaxLength(64);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.UserId);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TelegramChatLinkConfiguration : IEntityTypeConfiguration<TelegramChatLink>
{
    public void Configure(EntityTypeBuilder<TelegramChatLink> b)
    {
        b.ToTable("telegram_links", Schemas.Notify);
        b.HasKey(x => x.UserId);
        b.HasIndex(x => x.TelegramUserId).IsUnique();
        b.HasOne<User>().WithOne().HasForeignKey<TelegramChatLink>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> b)
    {
        // No FKs on purpose: the log must survive anonymisation and never block deletes elsewhere.
        b.ToTable("audit_log", Schemas.Audit);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.OccurredAt).HasDefaultValueSql("now()");
        b.Property(x => x.ActorKind).AsEnumText(b, "audit_log", "actor_kind");
        b.Property(x => x.Action).HasMaxLength(100);
        b.Property(x => x.TargetType).HasMaxLength(64);
        b.Property(x => x.TargetId).HasMaxLength(100);
        b.Property(x => x.CorrelationId).HasMaxLength(100);
        b.Property(x => x.IpHash).HasMaxLength(64);
        b.Property(x => x.Details).HasColumnType("jsonb");
        b.HasIndex(x => new { x.ClinicId, x.OccurredAt });
        b.HasIndex(x => new { x.ActorUserId, x.OccurredAt });
        b.HasIndex(x => new { x.Action, x.OccurredAt });
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.ToTable("idempotency_keys", Schemas.Common);
        b.HasKey(x => new { x.UserId, x.Key });
        b.Property(x => x.Key).HasMaxLength(64);
        b.Property(x => x.RequestHash).HasMaxLength(64);
        b.Property(x => x.Status).AsEnumText(b, "idempotency_keys", "status");
        b.Property(x => x.ResponseJson).HasColumnType("jsonb");
        b.HasIndex(x => x.CreatedAt);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
