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
        b.Property(x => x.LastErrorCode).HasMaxLength(64);
        b.Property(x => x.Channel).HasMaxLength(32);
        b.HasIndex(x => x.DedupKey).IsUnique();
        b.HasIndex(x => x.NextAttemptAt).HasFilter("status = 'Pending'").HasDatabaseName("ix_outbox_messages_due");
        b.HasIndex(x => x.LockedUntil).HasFilter("status = 'Processing'").HasDatabaseName("ix_outbox_messages_lease");
        b.HasIndex(x => new { x.Status, x.CreatedAt }).HasDatabaseName("ix_outbox_messages_status_created");
        b.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_outbox_messages_created");
    }
}

internal sealed class DeliveryAttemptConfiguration : IEntityTypeConfiguration<DeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<DeliveryAttempt> b)
    {
        b.ToTable("delivery_attempts", Schemas.Notify, t => t.HasCheckConstraint("ck_delivery_attempts_attempt_number", "attempt_number >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.RecipientKey).HasMaxLength(64);
        b.Property(x => x.Channel).AsEnumText(b, "delivery_attempts", "channel");
        b.Property(x => x.Outcome).AsEnumText(b, "delivery_attempts", "outcome");
        b.Property(x => x.ProviderStatus).HasMaxLength(32);
        b.Property(x => x.ErrorCode).HasMaxLength(64);
        b.Property(x => x.Error).HasMaxLength(DeliveryAttempt.MaxErrorLength);
        b.HasIndex(x => new { x.OutboxMessageId, x.RecipientKey });
        b.HasOne<OutboxMessage>().WithMany().HasForeignKey(x => x.OutboxMessageId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TelegramBotStateConfiguration : IEntityTypeConfiguration<TelegramBotState>
{
    public void Configure(EntityTypeBuilder<TelegramBotState> b)
    {
        b.ToTable("telegram_bot_state", Schemas.Notify);
        b.HasKey(x => x.BotId);
        b.Property(x => x.BotId).ValueGeneratedNever();
    }
}

internal sealed class DevCapturedMessageConfiguration : IEntityTypeConfiguration<DevCapturedMessage>
{
    public void Configure(EntityTypeBuilder<DevCapturedMessage> b)
    {
        b.ToTable("dev_messages", Schemas.Notify);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Channel).HasMaxLength(16);
        b.Property(x => x.Recipient).HasMaxLength(64);
        b.Property(x => x.Text).HasMaxLength(4096);
        b.Property(x => x.Buttons).HasColumnType("jsonb");
        b.Property(x => x.Origin).HasMaxLength(100);
        b.HasIndex(x => new { x.Channel, x.CreatedAt });
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
        b.ToTable("telegram_links", Schemas.Notify, t => t.HasCheckConstraint("ck_telegram_links_language_code", "language_code IN ('uz', 'ru', 'en')"));
        b.HasKey(x => x.UserId);
        b.Property(x => x.LanguageCode).HasMaxLength(8);
        b.Property(x => x.DeactivationReason).HasMaxLength(32);
        b.HasIndex(x => x.TelegramUserId).IsUnique();
        b.HasIndex(x => x.ChatId);
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
