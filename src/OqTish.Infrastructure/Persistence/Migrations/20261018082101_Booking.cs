using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OqTish.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Booking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reports");

            migrationBuilder.EnsureSchema(
                name: "booking");

            migrationBuilder.EnsureSchema(
                name: "reviews");

            migrationBuilder.CreateTable(
                name: "abuse_reports",
                schema: "reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    resolution = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_abuse_reports", x => x.id);
                    table.CheckConstraint("ck_abuse_reports_status", "status IN ('Open', 'Resolved')");
                    table.CheckConstraint("ck_abuse_reports_target_type", "target_type IN ('Clinic', 'Review')");
                    table.ForeignKey(
                        name: "fk_abuse_reports_clinics_clinic_id",
                        column: x => x.clinic_id,
                        principalSchema: "tenancy",
                        principalTable: "clinics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_abuse_reports_users_reporter_user_id",
                        column: x => x.reporter_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "appointments",
                schema: "booking",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    quoted_price = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    price_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    charged_amount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: true),
                    contact_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    contact_phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointments", x => x.id);
                    table.UniqueConstraint("ak_appointments_clinic_id_id", x => new { x.clinic_id, x.id });
                    table.CheckConstraint("ck_appointments_charged_amount", "(charged_amount IS NULL OR charged_amount >= 0) AND ((status = 'Completed') = (charged_amount IS NOT NULL))");
                    table.CheckConstraint("ck_appointments_currency", "currency IN ('UZS')");
                    table.CheckConstraint("ck_appointments_doctor_required", "status NOT IN ('Confirmed', 'Completed', 'NoShow') OR doctor_id IS NOT NULL");
                    table.CheckConstraint("ck_appointments_interval", "ends_at > starts_at");
                    table.CheckConstraint("ck_appointments_pending_deadline", "status <> 'Pending' OR expires_at IS NOT NULL");
                    table.CheckConstraint("ck_appointments_price_kind", "price_kind IN ('Fixed', 'From')");
                    table.CheckConstraint("ck_appointments_quoted_price", "quoted_price >= 0");
                    table.CheckConstraint("ck_appointments_source", "source IN ('Patient', 'WalkIn')");
                    table.CheckConstraint("ck_appointments_source_patient", "(source = 'Patient') = (patient_user_id IS NOT NULL)");
                    table.CheckConstraint("ck_appointments_status", "status IN ('Pending', 'Confirmed', 'Rejected', 'CancelledByPatient', 'CancelledByClinic', 'Expired', 'Completed', 'NoShow')");
                    table.ForeignKey(
                        name: "fk_appointments_branch_services_clinic_id_branch_id_service_id",
                        columns: x => new { x.clinic_id, x.branch_id, x.service_id },
                        principalSchema: "catalog",
                        principalTable: "branch_services",
                        principalColumns: new[] { "clinic_id", "branch_id", "service_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_branches_clinic_id_branch_id",
                        columns: x => new { x.clinic_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_doctor_branches_clinic_id_doctor_id_branch_id",
                        columns: x => new { x.clinic_id, x.doctor_id, x.branch_id },
                        principalSchema: "scheduling",
                        principalTable: "doctor_branches",
                        principalColumns: new[] { "clinic_id", "doctor_id", "branch_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_doctor_services_clinic_id_doctor_id_service_id",
                        columns: x => new { x.clinic_id, x.doctor_id, x.service_id },
                        principalSchema: "scheduling",
                        principalTable: "doctor_services",
                        principalColumns: new[] { "clinic_id", "doctor_id", "service_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_users_patient_user_id",
                        column: x => x.patient_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "appointment_status_history",
                schema: "booking",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    to_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointment_status_history", x => x.id);
                    table.CheckConstraint("ck_appointment_status_history_actor_kind", "actor_kind IN ('Patient', 'Staff', 'System')");
                    table.CheckConstraint("ck_appointment_status_history_from_status", "from_status IS NULL OR from_status IN ('Pending', 'Confirmed', 'Rejected', 'CancelledByPatient', 'CancelledByClinic', 'Expired', 'Completed', 'NoShow')");
                    table.CheckConstraint("ck_appointment_status_history_to_status", "to_status IN ('Pending', 'Confirmed', 'Rejected', 'CancelledByPatient', 'CancelledByClinic', 'Expired', 'Completed', 'NoShow')");
                    table.ForeignKey(
                        name: "fk_appointment_status_history_appointments_clinic_id_appointme",
                        columns: x => new { x.clinic_id, x.appointment_id },
                        principalSchema: "booking",
                        principalTable: "appointments",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "doctor_time_blocks",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_time_blocks", x => x.id);
                    table.CheckConstraint("ck_doctor_time_blocks_interval", "ends_at > starts_at");
                    table.CheckConstraint("ck_doctor_time_blocks_kind", "kind IN ('Hold', 'Appointment', 'TimeOff')");
                    table.CheckConstraint("ck_doctor_time_blocks_kind_refs", "(kind = 'TimeOff' AND appointment_id IS NULL AND branch_id IS NULL) OR (kind <> 'TimeOff' AND appointment_id IS NOT NULL AND branch_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_doctor_time_blocks_appointments_clinic_id_appointment_id",
                        columns: x => new { x.clinic_id, x.appointment_id },
                        principalSchema: "booking",
                        principalTable: "appointments",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_doctor_time_blocks_doctor_branches_clinic_id_doctor_id_bran",
                        columns: x => new { x.clinic_id, x.doctor_id, x.branch_id },
                        principalSchema: "scheduling",
                        principalTable: "doctor_branches",
                        principalColumns: new[] { "clinic_id", "doctor_id", "branch_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_doctor_time_blocks_doctors_clinic_id_doctor_id",
                        columns: x => new { x.clinic_id, x.doctor_id },
                        principalSchema: "scheduling",
                        principalTable: "doctors",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reviews",
                schema: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rating = table.Column<short>(type: "smallint", nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    moderation_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    moderation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    moderated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    moderated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reviews", x => x.id);
                    table.CheckConstraint("ck_reviews_moderation_status", "moderation_status IN ('Pending', 'Published', 'Hidden')");
                    table.CheckConstraint("ck_reviews_rating", "rating BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_reviews_text_length", "char_length(text) <= 1000");
                    table.ForeignKey(
                        name: "fk_reviews_appointments_clinic_id_appointment_id",
                        columns: x => new { x.clinic_id, x.appointment_id },
                        principalSchema: "booking",
                        principalTable: "appointments",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reviews_branches_clinic_id_branch_id",
                        columns: x => new { x.clinic_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reviews_users_patient_user_id",
                        column: x => x.patient_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_abuse_reports_clinic_id",
                schema: "reports",
                table: "abuse_reports",
                column: "clinic_id");

            migrationBuilder.CreateIndex(
                name: "ix_abuse_reports_status_created_at",
                schema: "reports",
                table: "abuse_reports",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_abuse_reports_open_per_reporter_target",
                schema: "reports",
                table: "abuse_reports",
                columns: new[] { "reporter_user_id", "target_type", "target_id" },
                unique: true,
                filter: "status = 'Open'");

            migrationBuilder.CreateIndex(
                name: "ix_appointment_status_history_appointment_id_occurred_at",
                schema: "booking",
                table: "appointment_status_history",
                columns: new[] { "appointment_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_appointment_status_history_clinic_id_appointment_id",
                schema: "booking",
                table: "appointment_status_history",
                columns: new[] { "clinic_id", "appointment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_branch_id_starts_at",
                schema: "booking",
                table: "appointments",
                columns: new[] { "branch_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_clinic_id_branch_id_service_id",
                schema: "booking",
                table: "appointments",
                columns: new[] { "clinic_id", "branch_id", "service_id" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_clinic_id_doctor_id_branch_id",
                schema: "booking",
                table: "appointments",
                columns: new[] { "clinic_id", "doctor_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_clinic_id_doctor_id_service_id",
                schema: "booking",
                table: "appointments",
                columns: new[] { "clinic_id", "doctor_id", "service_id" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_clinic_id_status_starts_at",
                schema: "booking",
                table: "appointments",
                columns: new[] { "clinic_id", "status", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_doctor_id_starts_at",
                schema: "booking",
                table: "appointments",
                columns: new[] { "doctor_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_patient_user_id_created_at",
                schema: "booking",
                table: "appointments",
                columns: new[] { "patient_user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_pending_expires_at",
                schema: "booking",
                table: "appointments",
                column: "expires_at",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_time_blocks_appointment_id",
                schema: "scheduling",
                table: "doctor_time_blocks",
                column: "appointment_id",
                unique: true,
                filter: "appointment_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_time_blocks_clinic_id_appointment_id",
                schema: "scheduling",
                table: "doctor_time_blocks",
                columns: new[] { "clinic_id", "appointment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_time_blocks_clinic_id_branch_id_starts_at",
                schema: "scheduling",
                table: "doctor_time_blocks",
                columns: new[] { "clinic_id", "branch_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_time_blocks_clinic_id_doctor_id_branch_id",
                schema: "scheduling",
                table: "doctor_time_blocks",
                columns: new[] { "clinic_id", "doctor_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_time_blocks_doctor_id_starts_at",
                schema: "scheduling",
                table: "doctor_time_blocks",
                columns: new[] { "doctor_id", "starts_at" },
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_appointment_id",
                schema: "reviews",
                table: "reviews",
                column: "appointment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reviews_clinic_id_appointment_id",
                schema: "reviews",
                table: "reviews",
                columns: new[] { "clinic_id", "appointment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_clinic_id_branch_id",
                schema: "reviews",
                table: "reviews",
                columns: new[] { "clinic_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_clinic_id_moderation_status_created_at",
                schema: "reviews",
                table: "reviews",
                columns: new[] { "clinic_id", "moderation_status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_moderation_status_created_at",
                schema: "reviews",
                table: "reviews",
                columns: new[] { "moderation_status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_patient_user_id",
                schema: "reviews",
                table: "reviews",
                column: "patient_user_id");

            // Doctor time blocks: the period is derived from starts_at/ends_at (half-open), and the GiST
            // exclusion constraint guarantees that a doctor never has two overlapping active blocks (ADR-004).
            migrationBuilder.Sql("""
                ALTER TABLE scheduling.doctor_time_blocks
                  ADD COLUMN period tstzrange GENERATED ALWAYS AS (tstzrange(starts_at, ends_at, '[)')) STORED;
                ALTER TABLE scheduling.doctor_time_blocks
                  ADD CONSTRAINT ck_doctor_time_blocks_period_valid CHECK (NOT isempty(period) AND lower_inc(period) AND NOT upper_inc(period)),
                  ADD CONSTRAINT ex_doctor_time_blocks_no_overlap EXCLUDE USING gist (doctor_id WITH =, period WITH &&) WHERE (is_active);
                """);

            // booking.appointment_status_history is append-only: trigger guard for everyone, plus no
            // UPDATE/DELETE/TRUNCATE grants for the app role (same pattern as audit.audit_log).
            migrationBuilder.Sql("""
                CREATE FUNCTION booking.prevent_status_history_change() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'booking.appointment_status_history is append-only (% is not allowed)', TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END
                $$;

                CREATE TRIGGER trg_appointment_status_history_append_only
                    BEFORE UPDATE OR DELETE ON booking.appointment_status_history
                    FOR EACH ROW EXECUTE FUNCTION booking.prevent_status_history_change();

                CREATE TRIGGER trg_appointment_status_history_no_truncate
                    BEFORE TRUNCATE ON booking.appointment_status_history
                    FOR EACH STATEMENT EXECUTE FUNCTION booking.prevent_status_history_change();

                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'oqtish_app') THEN
                        REVOKE UPDATE, DELETE, TRUNCATE ON booking.appointment_status_history FROM oqtish_app;
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_appointment_status_history_no_truncate ON booking.appointment_status_history;
                DROP TRIGGER IF EXISTS trg_appointment_status_history_append_only ON booking.appointment_status_history;
                DROP FUNCTION IF EXISTS booking.prevent_status_history_change();
                ALTER TABLE scheduling.doctor_time_blocks DROP CONSTRAINT IF EXISTS ex_doctor_time_blocks_no_overlap;
                """);

            migrationBuilder.DropTable(
                name: "abuse_reports",
                schema: "reports");

            migrationBuilder.DropTable(
                name: "appointment_status_history",
                schema: "booking");

            migrationBuilder.DropTable(
                name: "doctor_time_blocks",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "reviews",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "appointments",
                schema: "booking");
        }
    }
}
