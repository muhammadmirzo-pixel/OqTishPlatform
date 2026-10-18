using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OqTish.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "failed_attempts",
                schema: "identity",
                table: "totp_credentials",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "locked_until",
                schema: "identity",
                table: "totp_credentials",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "failed_attempts",
                schema: "identity",
                table: "totp_credentials");

            migrationBuilder.DropColumn(
                name: "locked_until",
                schema: "identity",
                table: "totp_credentials");
        }
    }
}
