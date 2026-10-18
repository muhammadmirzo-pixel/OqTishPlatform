using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OqTish.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CatalogScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.EnsureSchema(
                name: "scheduling");

            migrationBuilder.AddColumn<decimal>(
                name: "rating_average",
                schema: "tenancy",
                table: "clinics",
                type: "numeric(3,2)",
                precision: 3,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "review_count",
                schema: "tenancy",
                table: "clinics",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "doctors",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctors", x => x.id);
                    table.UniqueConstraint("ak_doctors_clinic_id_id", x => new { x.clinic_id, x.id });
                    table.ForeignKey(
                        name: "fk_doctors_clinics_clinic_id",
                        column: x => x.clinic_id,
                        principalSchema: "tenancy",
                        principalTable: "clinics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_doctors_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "services",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_services", x => x.id);
                    table.UniqueConstraint("ak_services_clinic_id_id", x => new { x.clinic_id, x.id });
                    table.ForeignKey(
                        name: "fk_services_clinics_clinic_id",
                        column: x => x.clinic_id,
                        principalSchema: "tenancy",
                        principalTable: "clinics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "doctor_branches",
                schema: "scheduling",
                columns: table => new
                {
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_branches", x => new { x.doctor_id, x.branch_id });
                    table.UniqueConstraint("ak_doctor_branches_clinic_id_doctor_id_branch_id", x => new { x.clinic_id, x.doctor_id, x.branch_id });
                    table.ForeignKey(
                        name: "fk_doctor_branches_branches_clinic_id_branch_id",
                        columns: x => new { x.clinic_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_doctor_branches_doctors_clinic_id_doctor_id",
                        columns: x => new { x.clinic_id, x.doctor_id },
                        principalSchema: "scheduling",
                        principalTable: "doctors",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "branch_services",
                schema: "catalog",
                columns: table => new
                {
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    price_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branch_services", x => new { x.branch_id, x.service_id });
                    table.UniqueConstraint("ak_branch_services_clinic_id_branch_id_service_id", x => new { x.clinic_id, x.branch_id, x.service_id });
                    table.CheckConstraint("ck_branch_services_currency", "currency IN ('UZS')");
                    table.CheckConstraint("ck_branch_services_duration", "duration_minutes BETWEEN 5 AND 480");
                    table.CheckConstraint("ck_branch_services_price", "price >= 0");
                    table.CheckConstraint("ck_branch_services_price_kind", "price_kind IN ('Fixed', 'From')");
                    table.ForeignKey(
                        name: "fk_branch_services_branches_clinic_id_branch_id",
                        columns: x => new { x.clinic_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_branch_services_services_clinic_id_service_id",
                        columns: x => new { x.clinic_id, x.service_id },
                        principalSchema: "catalog",
                        principalTable: "services",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "doctor_services",
                schema: "scheduling",
                columns: table => new
                {
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_services", x => new { x.doctor_id, x.service_id });
                    table.UniqueConstraint("ak_doctor_services_clinic_id_doctor_id_service_id", x => new { x.clinic_id, x.doctor_id, x.service_id });
                    table.ForeignKey(
                        name: "fk_doctor_services_doctors_clinic_id_doctor_id",
                        columns: x => new { x.clinic_id, x.doctor_id },
                        principalSchema: "scheduling",
                        principalTable: "doctors",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_doctor_services_services_clinic_id_service_id",
                        columns: x => new { x.clinic_id, x.service_id },
                        principalSchema: "catalog",
                        principalTable: "services",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "working_hours",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    weekday = table.Column<short>(type: "smallint", nullable: false),
                    start_local = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_local = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_working_hours", x => x.id);
                    table.CheckConstraint("ck_working_hours_interval", "start_local < end_local");
                    table.CheckConstraint("ck_working_hours_weekday", "weekday BETWEEN 1 AND 7");
                    table.ForeignKey(
                        name: "fk_working_hours_doctor_branches_clinic_id_doctor_id_branch_id",
                        columns: x => new { x.clinic_id, x.doctor_id, x.branch_id },
                        principalSchema: "scheduling",
                        principalTable: "doctor_branches",
                        principalColumns: new[] { "clinic_id", "doctor_id", "branch_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_clinics_rating_average",
                schema: "tenancy",
                table: "clinics",
                sql: "rating_average IS NULL OR (rating_average >= 1 AND rating_average <= 5)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_clinics_review_count",
                schema: "tenancy",
                table: "clinics",
                sql: "review_count >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_branch_services_clinic_id_service_id",
                schema: "catalog",
                table: "branch_services",
                columns: new[] { "clinic_id", "service_id" });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_branches_clinic_id_branch_id",
                schema: "scheduling",
                table: "doctor_branches",
                columns: new[] { "clinic_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_services_clinic_id_service_id",
                schema: "scheduling",
                table: "doctor_services",
                columns: new[] { "clinic_id", "service_id" });

            migrationBuilder.CreateIndex(
                name: "ix_doctors_clinic_id_user_id",
                schema: "scheduling",
                table: "doctors",
                columns: new[] { "clinic_id", "user_id" },
                unique: true,
                filter: "user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_doctors_user_id",
                schema: "scheduling",
                table: "doctors",
                column: "user_id",
                filter: "user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_services_category",
                schema: "catalog",
                table: "services",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_services_clinic_id_is_active",
                schema: "catalog",
                table: "services",
                columns: new[] { "clinic_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_working_hours_clinic_id_branch_id_weekday",
                schema: "scheduling",
                table: "working_hours",
                columns: new[] { "clinic_id", "branch_id", "weekday" });

            migrationBuilder.CreateIndex(
                name: "ix_working_hours_clinic_id_doctor_id_branch_id",
                schema: "scheduling",
                table: "working_hours",
                columns: new[] { "clinic_id", "doctor_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_working_hours_doctor_id_weekday",
                schema: "scheduling",
                table: "working_hours",
                columns: new[] { "doctor_id", "weekday" });

            // Same-weekday intervals of one doctor never overlap, across branches (DATABASE.md §2.2).
            migrationBuilder.Sql("""
                CREATE TYPE scheduling.timerange AS RANGE (subtype = time);
                ALTER TABLE scheduling.working_hours
                  ADD CONSTRAINT ex_working_hours_no_overlap
                  EXCLUDE USING gist (doctor_id WITH =, weekday WITH =, scheduling.timerange(start_local, end_local, '[)') WITH &&);
                """);

            // Haversine great-circle distance in km (OD-16), mapped to GeoFunctions.DistanceKm.
            migrationBuilder.Sql("""
                CREATE FUNCTION tenancy.distance_km(lat1 numeric, lng1 numeric, lat2 numeric, lng2 numeric)
                RETURNS numeric
                LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE AS $$
                  SELECT round((2 * 6371 * asin(least(1, sqrt(
                      power(sin(radians((lat2 - lat1)::double precision) / 2), 2)
                    + cos(radians(lat1::double precision)) * cos(radians(lat2::double precision))
                    * power(sin(radians((lng2 - lng1)::double precision) / 2), 2)))))::numeric, 2)
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS tenancy.distance_km(numeric, numeric, numeric, numeric);
                ALTER TABLE scheduling.working_hours DROP CONSTRAINT IF EXISTS ex_working_hours_no_overlap;
                DROP TYPE IF EXISTS scheduling.timerange;
                """);

            migrationBuilder.DropTable(
                name: "branch_services",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "doctor_services",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "working_hours",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "services",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "doctor_branches",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "doctors",
                schema: "scheduling");

            migrationBuilder.DropCheckConstraint(
                name: "ck_clinics_rating_average",
                schema: "tenancy",
                table: "clinics");

            migrationBuilder.DropCheckConstraint(
                name: "ck_clinics_review_count",
                schema: "tenancy",
                table: "clinics");

            migrationBuilder.DropColumn(
                name: "rating_average",
                schema: "tenancy",
                table: "clinics");

            migrationBuilder.DropColumn(
                name: "review_count",
                schema: "tenancy",
                table: "clinics");
        }
    }
}
