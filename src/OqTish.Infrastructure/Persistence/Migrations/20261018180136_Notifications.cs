using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OqTish.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Notifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_messages_status",
                schema: "notify",
                table: "outbox_messages");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deactivated_at",
                schema: "notify",
                table: "telegram_links",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deactivation_reason",
                schema: "notify",
                table: "telegram_links",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                schema: "notify",
                table: "telegram_links",
                type: "boolean",
                nullable: false,
                defaultValue: true); // existing links stay active (edited: the model has no default)

            migrationBuilder.AddColumn<string>(
                name: "language_code",
                schema: "notify",
                table: "telegram_links",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "uz"); // existing links default to Uzbek (edited: the model has no default)

            migrationBuilder.AddColumn<string>(
                name: "channel",
                schema: "notify",
                table: "outbox_messages",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "claim_token",
                schema: "notify",
                table: "outbox_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_error_code",
                schema: "notify",
                table: "outbox_messages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "locked_until",
                schema: "notify",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "delivery_attempts",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    outbox_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    recipient_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_attempts", x => x.id);
                    table.CheckConstraint("ck_delivery_attempts_attempt_number", "attempt_number >= 0");
                    table.CheckConstraint("ck_delivery_attempts_channel", "channel IN ('None', 'Telegram', 'Sms')");
                    table.CheckConstraint("ck_delivery_attempts_outcome", "outcome IN ('Sent', 'Retry', 'Rejected', 'Skipped')");
                    table.ForeignKey(
                        name: "fk_delivery_attempts_outbox_messages_outbox_message_id",
                        column: x => x.outbox_message_id,
                        principalSchema: "notify",
                        principalTable: "outbox_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dev_messages",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    recipient = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    text = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    buttons = table.Column<string>(type: "jsonb", nullable: true),
                    origin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dev_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "telegram_bot_state",
                schema: "notify",
                columns: table => new
                {
                    bot_id = table.Column<long>(type: "bigint", nullable: false),
                    update_offset = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_telegram_bot_state", x => x.bot_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_telegram_links_chat_id",
                schema: "notify",
                table: "telegram_links",
                column: "chat_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_telegram_links_language_code",
                schema: "notify",
                table: "telegram_links",
                sql: "language_code IN ('uz', 'ru', 'en')");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_created",
                schema: "notify",
                table: "outbox_messages",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_lease",
                schema: "notify",
                table: "outbox_messages",
                column: "locked_until",
                filter: "status = 'Processing'");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_status_created",
                schema: "notify",
                table: "outbox_messages",
                columns: new[] { "status", "created_at" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_messages_status",
                schema: "notify",
                table: "outbox_messages",
                sql: "status IN ('Pending', 'Processing', 'Sent', 'Failed', 'Cancelled', 'Skipped')");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_attempts_outbox_message_id_recipient_key",
                schema: "notify",
                table: "delivery_attempts",
                columns: new[] { "outbox_message_id", "recipient_key" });

            migrationBuilder.CreateIndex(
                name: "ix_dev_messages_channel_created_at",
                schema: "notify",
                table: "dev_messages",
                columns: new[] { "channel", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_attempts",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "dev_messages",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "telegram_bot_state",
                schema: "notify");

            migrationBuilder.DropIndex(
                name: "ix_telegram_links_chat_id",
                schema: "notify",
                table: "telegram_links");

            migrationBuilder.DropCheckConstraint(
                name: "ck_telegram_links_language_code",
                schema: "notify",
                table: "telegram_links");

            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_created",
                schema: "notify",
                table: "outbox_messages");

            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_lease",
                schema: "notify",
                table: "outbox_messages");

            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_status_created",
                schema: "notify",
                table: "outbox_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_messages_status",
                schema: "notify",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "deactivated_at",
                schema: "notify",
                table: "telegram_links");

            migrationBuilder.DropColumn(
                name: "deactivation_reason",
                schema: "notify",
                table: "telegram_links");

            migrationBuilder.DropColumn(
                name: "is_active",
                schema: "notify",
                table: "telegram_links");

            migrationBuilder.DropColumn(
                name: "language_code",
                schema: "notify",
                table: "telegram_links");

            migrationBuilder.DropColumn(
                name: "channel",
                schema: "notify",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "claim_token",
                schema: "notify",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "last_error_code",
                schema: "notify",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "locked_until",
                schema: "notify",
                table: "outbox_messages");

            // Skipped did not exist before this migration.
            migrationBuilder.Sql("UPDATE notify.outbox_messages SET status = 'Cancelled' WHERE status = 'Skipped';");

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_messages_status",
                schema: "notify",
                table: "outbox_messages",
                sql: "status IN ('Pending', 'Processing', 'Sent', 'Failed', 'Cancelled')");
        }
    }
}
