using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OqTish.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StoredImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "avatar_image_id",
                schema: "identity",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "logo_image_id",
                schema: "tenancy",
                table: "clinics",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "stored_images",
                schema: "common",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stored_images", x => x.id);
                    table.CheckConstraint("ck_stored_images_content_type", "content_type IN ('image/jpeg', 'image/png', 'image/webp')");
                    table.CheckConstraint("ck_stored_images_size", "size_bytes > 0 AND size_bytes <= 2097152 AND size_bytes = octet_length(bytes)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_users_avatar_image_id",
                schema: "identity",
                table: "users",
                column: "avatar_image_id");

            migrationBuilder.CreateIndex(
                name: "ix_clinics_logo_image_id",
                schema: "tenancy",
                table: "clinics",
                column: "logo_image_id");

            migrationBuilder.AddForeignKey(
                name: "fk_clinics_stored_images_logo_image_id",
                schema: "tenancy",
                table: "clinics",
                column: "logo_image_id",
                principalSchema: "common",
                principalTable: "stored_images",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_users_stored_images_avatar_image_id",
                schema: "identity",
                table: "users",
                column: "avatar_image_id",
                principalSchema: "common",
                principalTable: "stored_images",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_clinics_stored_images_logo_image_id",
                schema: "tenancy",
                table: "clinics");

            migrationBuilder.DropForeignKey(
                name: "fk_users_stored_images_avatar_image_id",
                schema: "identity",
                table: "users");

            migrationBuilder.DropTable(
                name: "stored_images",
                schema: "common");

            migrationBuilder.DropIndex(
                name: "ix_users_avatar_image_id",
                schema: "identity",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_clinics_logo_image_id",
                schema: "tenancy",
                table: "clinics");

            migrationBuilder.DropColumn(
                name: "avatar_image_id",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "logo_image_id",
                schema: "tenancy",
                table: "clinics");
        }
    }
}
