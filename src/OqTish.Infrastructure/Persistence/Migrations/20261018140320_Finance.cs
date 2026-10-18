using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OqTish.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Finance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "finance");

            migrationBuilder.CreateTable(
                name: "commission_rules",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    basis = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: true),
                    fixed_fee = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    rounding = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    contract_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commission_rules", x => x.id);
                    table.UniqueConstraint("ak_commission_rules_clinic_id_id", x => new { x.clinic_id, x.id });
                    table.CheckConstraint("ck_commission_rules_amount_by_basis", "(basis IN ('CollectedPayment', 'CompletedService') AND rate IS NOT NULL AND rate > 0 AND rate <= 1 AND fixed_fee IS NULL) OR (basis IN ('ConfirmedAppointment', 'NewPatient') AND fixed_fee IS NOT NULL AND fixed_fee > 0 AND rate IS NULL)");
                    table.CheckConstraint("ck_commission_rules_basis", "basis IN ('CollectedPayment', 'CompletedService', 'ConfirmedAppointment', 'NewPatient')");
                    table.CheckConstraint("ck_commission_rules_currency", "currency IN ('UZS')");
                    table.CheckConstraint("ck_commission_rules_range", "effective_to IS NULL OR effective_to > effective_from");
                    table.CheckConstraint("ck_commission_rules_rounding", "rounding IN ('AwayFromZero')");
                    table.ForeignKey(
                        name: "fk_commission_rules_clinics_clinic_id",
                        column: x => x.clinic_id,
                        principalSchema: "tenancy",
                        principalTable: "clinics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_commission_rules_users_approved_by_user_id",
                        column: x => x.approved_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_commission_rules_users_ended_by_user_id",
                        column: x => x.ended_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "daily_financial_summaries",
                schema: "finance",
                columns: table => new
                {
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    local_date = table.Column<DateOnly>(type: "date", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    visits = table.Column<int>(type: "integer", nullable: false),
                    charged = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    collected = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    refunded = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    prior_period_refunded = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    commission = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    ledger_watermark = table.Column<long>(type: "bigint", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_financial_summaries", x => new { x.branch_id, x.local_date });
                    table.CheckConstraint("ck_daily_financial_summaries_currency", "currency IN ('UZS')");
                    table.CheckConstraint("ck_daily_financial_summaries_visits", "visits >= 0");
                    table.ForeignKey(
                        name: "fk_daily_financial_summaries_branches_clinic_id_branch_id",
                        columns: x => new { x.clinic_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    collected_by = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    refund_of_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    external_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    failed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.UniqueConstraint("ak_payments_clinic_id_id", x => new { x.clinic_id, x.id });
                    table.CheckConstraint("ck_payments_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_payments_collected_by", "collected_by IN ('Clinic', 'Platform')");
                    table.CheckConstraint("ck_payments_currency", "currency IN ('UZS')");
                    table.CheckConstraint("ck_payments_direction", "direction IN ('In', 'Refund')");
                    table.CheckConstraint("ck_payments_failed_fields", "(status = 'Failed') = (failed_at IS NOT NULL)");
                    table.CheckConstraint("ck_payments_method", "method IN ('Cash', 'Card', 'Transfer')");
                    table.CheckConstraint("ck_payments_refund_link", "(direction = 'Refund') = (refund_of_payment_id IS NOT NULL)");
                    table.CheckConstraint("ck_payments_refund_recorded", "direction <> 'Refund' OR status = 'Recorded'");
                    table.CheckConstraint("ck_payments_status", "status IN ('Recorded', 'Failed')");
                    table.ForeignKey(
                        name: "fk_payments_appointments_clinic_id_appointment_id",
                        columns: x => new { x.clinic_id, x.appointment_id },
                        principalSchema: "booking",
                        principalTable: "appointments",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_branches_clinic_id_branch_id",
                        columns: x => new { x.clinic_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_payments_clinic_id_refund_of_payment_id",
                        columns: x => new { x.clinic_id, x.refund_of_payment_id },
                        principalSchema: "finance",
                        principalTable: "payments",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_users_failed_by_user_id",
                        column: x => x.failed_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_users_recorded_by_user_id",
                        column: x => x.recorded_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_completions",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    charged_amount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    completed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    posting_date_local = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_completions", x => x.id);
                    table.UniqueConstraint("ak_service_completions_clinic_id_id", x => new { x.clinic_id, x.id });
                    table.CheckConstraint("ck_service_completions_charged_amount", "charged_amount >= 0");
                    table.CheckConstraint("ck_service_completions_currency", "currency IN ('UZS')");
                    table.ForeignKey(
                        name: "fk_service_completions_appointments_clinic_id_appointment_id",
                        columns: x => new { x.clinic_id, x.appointment_id },
                        principalSchema: "booking",
                        principalTable: "appointments",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_service_completions_branches_clinic_id_branch_id",
                        columns: x => new { x.clinic_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_service_completions_users_completed_by_user_id",
                        column: x => x.completed_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_service_completions_users_patient_user_id",
                        column: x => x.patient_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "settlements",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    amount_due = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    payment_terms = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finalized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finalized_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    paid_recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    voided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settlements", x => x.id);
                    table.UniqueConstraint("ak_settlements_clinic_id_id", x => new { x.clinic_id, x.id });
                    table.CheckConstraint("ck_settlements_currency", "currency IN ('UZS')");
                    table.CheckConstraint("ck_settlements_finalized_fields", "status NOT IN ('Finalized', 'Paid') OR finalized_at IS NOT NULL");
                    table.CheckConstraint("ck_settlements_paid_fields", "(status = 'Paid') = (paid_at IS NOT NULL)");
                    table.CheckConstraint("ck_settlements_period_month", "extract(day from period_start) = 1 AND period_end = (period_start + interval '1 month')::date");
                    table.CheckConstraint("ck_settlements_status", "status IN ('Draft', 'Finalized', 'Paid', 'Voided')");
                    table.CheckConstraint("ck_settlements_voided_fields", "(status = 'Voided') = (voided_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_settlements_clinics_clinic_id",
                        column: x => x.clinic_id,
                        principalSchema: "tenancy",
                        principalTable: "clinics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_settlements_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_allocations",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_completion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_allocations", x => x.id);
                    table.CheckConstraint("ck_payment_allocations_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_payment_allocations_currency", "currency IN ('UZS')");
                    table.ForeignKey(
                        name: "fk_payment_allocations_appointments_clinic_id_appointment_id",
                        columns: x => new { x.clinic_id, x.appointment_id },
                        principalSchema: "booking",
                        principalTable: "appointments",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_allocations_payments_clinic_id_payment_id",
                        columns: x => new { x.clinic_id, x.payment_id },
                        principalSchema: "finance",
                        principalTable: "payments",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_allocations_service_completions_clinic_id_service_c",
                        columns: x => new { x.clinic_id, x.service_completion_id },
                        principalSchema: "finance",
                        principalTable: "service_completions",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_entries",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entry_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_event_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    posting_date_local = table.Column<DateOnly>(type: "date", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reverses_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    service_completion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    settlement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_entries", x => x.id);
                    table.UniqueConstraint("ak_journal_entries_clinic_id_id", x => new { x.clinic_id, x.id });
                    table.CheckConstraint("ck_journal_entries_entry_type", "entry_type IN ('ServiceCompleted', 'PaymentRecorded', 'PaymentRefunded', 'PaymentReversed', 'Commission', 'CommissionReversal', 'SettlementPaid')");
                    table.ForeignKey(
                        name: "fk_journal_entries_appointments_clinic_id_appointment_id",
                        columns: x => new { x.clinic_id, x.appointment_id },
                        principalSchema: "booking",
                        principalTable: "appointments",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_entries_branches_clinic_id_branch_id",
                        columns: x => new { x.clinic_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_entries_clinics_clinic_id",
                        column: x => x.clinic_id,
                        principalSchema: "tenancy",
                        principalTable: "clinics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_entries_journal_entries_clinic_id_reverses_entry_id",
                        columns: x => new { x.clinic_id, x.reverses_entry_id },
                        principalSchema: "finance",
                        principalTable: "journal_entries",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_entries_payments_clinic_id_payment_id",
                        columns: x => new { x.clinic_id, x.payment_id },
                        principalSchema: "finance",
                        principalTable: "payments",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_entries_service_completions_clinic_id_service_compl",
                        columns: x => new { x.clinic_id, x.service_completion_id },
                        principalSchema: "finance",
                        principalTable: "service_completions",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_entries_settlements_clinic_id_settlement_id",
                        columns: x => new { x.clinic_id, x.settlement_id },
                        principalSchema: "finance",
                        principalTable: "settlements",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_entries_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledger_lines",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    party = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    commission_rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rate_snapshot = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: true),
                    basis_amount_snapshot = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_lines", x => x.id);
                    table.UniqueConstraint("ak_ledger_lines_clinic_id_id", x => new { x.clinic_id, x.id });
                    table.CheckConstraint("ck_ledger_lines_account", "account IN ('PatientReceivable', 'ClinicCollectedCash', 'ClinicCollectedCard', 'ClinicCollectedTransfer', 'ClinicServiceRevenue', 'ClinicRefundsPaid', 'PlatformCommissionReceivable', 'PlatformCommissionRevenue', 'PlatformCollectedFunds', 'PlatformPayableToClinic', 'SettlementClearing')");
                    table.CheckConstraint("ck_ledger_lines_amount_nonzero", "amount <> 0");
                    table.CheckConstraint("ck_ledger_lines_currency", "currency IN ('UZS')");
                    table.CheckConstraint("ck_ledger_lines_party", "party IN ('Clinic', 'Patient', 'Platform')");
                    table.CheckConstraint("ck_ledger_lines_rule_snapshot", "commission_rule_id IS NOT NULL OR (rate_snapshot IS NULL AND basis_amount_snapshot IS NULL)");
                    table.ForeignKey(
                        name: "fk_ledger_lines_commission_rules_clinic_id_commission_rule_id",
                        columns: x => new { x.clinic_id, x.commission_rule_id },
                        principalSchema: "finance",
                        principalTable: "commission_rules",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ledger_lines_journal_entries_clinic_id_journal_entry_id",
                        columns: x => new { x.clinic_id, x.journal_entry_id },
                        principalSchema: "finance",
                        principalTable: "journal_entries",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "settlement_items",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    settlement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settlement_items", x => x.id);
                    table.CheckConstraint("ck_settlement_items_currency", "currency IN ('UZS')");
                    table.ForeignKey(
                        name: "fk_settlement_items_ledger_lines_clinic_id_ledger_line_id",
                        columns: x => new { x.clinic_id, x.ledger_line_id },
                        principalSchema: "finance",
                        principalTable: "ledger_lines",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_settlement_items_settlements_clinic_id_settlement_id",
                        columns: x => new { x.clinic_id, x.settlement_id },
                        principalSchema: "finance",
                        principalTable: "settlements",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_commission_rules_approved_by_user_id",
                schema: "finance",
                table: "commission_rules",
                column: "approved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_commission_rules_clinic_id_basis_effective_from",
                schema: "finance",
                table: "commission_rules",
                columns: new[] { "clinic_id", "basis", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_commission_rules_ended_by_user_id",
                schema: "finance",
                table: "commission_rules",
                column: "ended_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_daily_financial_summaries_clinic_id_branch_id",
                schema: "finance",
                table: "daily_financial_summaries",
                columns: new[] { "clinic_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_daily_financial_summaries_clinic_id_local_date",
                schema: "finance",
                table: "daily_financial_summaries",
                columns: new[] { "clinic_id", "local_date" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_appointment_id",
                schema: "finance",
                table: "journal_entries",
                column: "appointment_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_clinic_id_appointment_id",
                schema: "finance",
                table: "journal_entries",
                columns: new[] { "clinic_id", "appointment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_clinic_id_branch_id_posting_date_local",
                schema: "finance",
                table: "journal_entries",
                columns: new[] { "clinic_id", "branch_id", "posting_date_local" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_clinic_id_entry_type_posting_date_local",
                schema: "finance",
                table: "journal_entries",
                columns: new[] { "clinic_id", "entry_type", "posting_date_local" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_clinic_id_payment_id",
                schema: "finance",
                table: "journal_entries",
                columns: new[] { "clinic_id", "payment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_clinic_id_reverses_entry_id",
                schema: "finance",
                table: "journal_entries",
                columns: new[] { "clinic_id", "reverses_entry_id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_clinic_id_service_completion_id",
                schema: "finance",
                table: "journal_entries",
                columns: new[] { "clinic_id", "service_completion_id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_clinic_id_settlement_id",
                schema: "finance",
                table: "journal_entries",
                columns: new[] { "clinic_id", "settlement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_created_by_user_id",
                schema: "finance",
                table: "journal_entries",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_payment_id",
                schema: "finance",
                table: "journal_entries",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_reverses_entry_id",
                schema: "finance",
                table: "journal_entries",
                column: "reverses_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_sequence",
                schema: "finance",
                table: "journal_entries",
                column: "sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_source_event_key",
                schema: "finance",
                table: "journal_entries",
                column: "source_event_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ledger_lines_clinic_id_account",
                schema: "finance",
                table: "ledger_lines",
                columns: new[] { "clinic_id", "account" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_lines_clinic_id_commission_rule_id",
                schema: "finance",
                table: "ledger_lines",
                columns: new[] { "clinic_id", "commission_rule_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_lines_clinic_id_journal_entry_id",
                schema: "finance",
                table: "ledger_lines",
                columns: new[] { "clinic_id", "journal_entry_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_lines_journal_entry_id",
                schema: "finance",
                table: "ledger_lines",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_appointment_id",
                schema: "finance",
                table: "payment_allocations",
                column: "appointment_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_clinic_id_appointment_id",
                schema: "finance",
                table: "payment_allocations",
                columns: new[] { "clinic_id", "appointment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_clinic_id_payment_id",
                schema: "finance",
                table: "payment_allocations",
                columns: new[] { "clinic_id", "payment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_clinic_id_service_completion_id",
                schema: "finance",
                table: "payment_allocations",
                columns: new[] { "clinic_id", "service_completion_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_payment_id",
                schema: "finance",
                table: "payment_allocations",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_appointment_id",
                schema: "finance",
                table: "payments",
                column: "appointment_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_clinic_id_appointment_id",
                schema: "finance",
                table: "payments",
                columns: new[] { "clinic_id", "appointment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_clinic_id_branch_id_recorded_at",
                schema: "finance",
                table: "payments",
                columns: new[] { "clinic_id", "branch_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_clinic_id_refund_of_payment_id",
                schema: "finance",
                table: "payments",
                columns: new[] { "clinic_id", "refund_of_payment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_failed_by_user_id",
                schema: "finance",
                table: "payments",
                column: "failed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_recorded_by_user_id",
                schema: "finance",
                table: "payments",
                column: "recorded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_refund_of_payment_id",
                schema: "finance",
                table: "payments",
                column: "refund_of_payment_id");

            migrationBuilder.CreateIndex(
                name: "ux_payments_clinic_idempotency_key",
                schema: "finance",
                table: "payments",
                columns: new[] { "clinic_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_service_completions_appointment_id",
                schema: "finance",
                table: "service_completions",
                column: "appointment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_service_completions_branch_id_posting_date_local",
                schema: "finance",
                table: "service_completions",
                columns: new[] { "branch_id", "posting_date_local" });

            migrationBuilder.CreateIndex(
                name: "ix_service_completions_clinic_id_appointment_id",
                schema: "finance",
                table: "service_completions",
                columns: new[] { "clinic_id", "appointment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_service_completions_clinic_id_branch_id",
                schema: "finance",
                table: "service_completions",
                columns: new[] { "clinic_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_service_completions_clinic_id_patient_user_id",
                schema: "finance",
                table: "service_completions",
                columns: new[] { "clinic_id", "patient_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_service_completions_completed_by_user_id",
                schema: "finance",
                table: "service_completions",
                column: "completed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_completions_patient_user_id",
                schema: "finance",
                table: "service_completions",
                column: "patient_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_settlement_items_clinic_id_ledger_line_id",
                schema: "finance",
                table: "settlement_items",
                columns: new[] { "clinic_id", "ledger_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_settlement_items_clinic_id_settlement_id",
                schema: "finance",
                table: "settlement_items",
                columns: new[] { "clinic_id", "settlement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_settlement_items_ledger_line_id",
                schema: "finance",
                table: "settlement_items",
                column: "ledger_line_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_settlement_items_settlement_id",
                schema: "finance",
                table: "settlement_items",
                column: "settlement_id");

            migrationBuilder.CreateIndex(
                name: "ix_settlements_created_by_user_id",
                schema: "finance",
                table: "settlements",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_settlements_status_period_start",
                schema: "finance",
                table: "settlements",
                columns: new[] { "status", "period_start" });

            migrationBuilder.CreateIndex(
                name: "ux_settlements_clinic_period_active",
                schema: "finance",
                table: "settlements",
                columns: new[] { "clinic_id", "period_start" },
                unique: true,
                filter: "status <> 'Voided'");

            // Commission rules: the effective range is derived from effective_from/effective_to (half-open) and a GiST
            // exclusion constraint forbids overlapping ranges per (clinic, basis). Rules are never edited: only
            // effective_to may be set or shortened ("end"), and rows are never deleted.
            migrationBuilder.Sql("""
                ALTER TABLE finance.commission_rules
                  ADD COLUMN effective tstzrange GENERATED ALWAYS AS (tstzrange(effective_from, effective_to, '[)')) STORED;
                ALTER TABLE finance.commission_rules
                  ADD CONSTRAINT ex_commission_rules_no_overlap EXCLUDE USING gist (clinic_id WITH =, basis WITH =, effective WITH &&);

                CREATE FUNCTION finance.guard_commission_rule_change() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'finance.commission_rules rows are never deleted' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF (NEW.id, NEW.clinic_id, NEW.basis, NEW.rate, NEW.fixed_fee, NEW.currency, NEW.rounding, NEW.effective_from,
                        NEW.contract_reference, NEW.approved_by_user_id, NEW.created_at)
                       IS DISTINCT FROM
                       (OLD.id, OLD.clinic_id, OLD.basis, OLD.rate, OLD.fixed_fee, OLD.currency, OLD.rounding, OLD.effective_from,
                        OLD.contract_reference, OLD.approved_by_user_id, OLD.created_at) THEN
                        RAISE EXCEPTION 'finance.commission_rules: only effective_to can change (end the rule)' USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    IF NEW.effective_to IS NULL OR (OLD.effective_to IS NOT NULL AND NEW.effective_to > OLD.effective_to) THEN
                        RAISE EXCEPTION 'finance.commission_rules: a rule can only be ended or shortened' USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NEW;
                END
                $$;

                CREATE TRIGGER trg_commission_rules_only_end
                    BEFORE UPDATE OR DELETE ON finance.commission_rules
                    FOR EACH ROW EXECUTE FUNCTION finance.guard_commission_rule_change();
                """);

            // The ledger is append-only (ADR-007): trigger guard for every role (also the owner), plus no
            // UPDATE/DELETE/TRUNCATE grants for the app role — same pattern as audit.audit_log.
            migrationBuilder.Sql("""
                CREATE FUNCTION finance.prevent_ledger_change() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'finance.% is append-only (% is not allowed)', TG_TABLE_NAME, TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END
                $$;

                CREATE TRIGGER trg_journal_entries_append_only
                    BEFORE UPDATE OR DELETE ON finance.journal_entries
                    FOR EACH ROW EXECUTE FUNCTION finance.prevent_ledger_change();
                CREATE TRIGGER trg_journal_entries_no_truncate
                    BEFORE TRUNCATE ON finance.journal_entries
                    FOR EACH STATEMENT EXECUTE FUNCTION finance.prevent_ledger_change();
                CREATE TRIGGER trg_ledger_lines_append_only
                    BEFORE UPDATE OR DELETE ON finance.ledger_lines
                    FOR EACH ROW EXECUTE FUNCTION finance.prevent_ledger_change();
                CREATE TRIGGER trg_ledger_lines_no_truncate
                    BEFORE TRUNCATE ON finance.ledger_lines
                    FOR EACH STATEMENT EXECUTE FUNCTION finance.prevent_ledger_change();
                CREATE TRIGGER trg_commission_rules_no_truncate
                    BEFORE TRUNCATE ON finance.commission_rules
                    FOR EACH STATEMENT EXECUTE FUNCTION finance.prevent_ledger_change();

                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'oqtish_app') THEN
                        REVOKE UPDATE, DELETE, TRUNCATE ON finance.journal_entries FROM oqtish_app;
                        REVOKE UPDATE, DELETE, TRUNCATE ON finance.ledger_lines FROM oqtish_app;
                        REVOKE DELETE, TRUNCATE ON finance.commission_rules FROM oqtish_app;
                    END IF;
                END
                $$;
                """);

            // Double entry: at commit, every journal entry must have >= 2 lines that sum to zero per currency. Deferred
            // constraint triggers on both tables (an entry without lines is caught too).
            migrationBuilder.Sql("""
                CREATE FUNCTION finance.check_journal_entry_balanced() RETURNS trigger
                LANGUAGE plpgsql AS $$
                DECLARE
                    entry uuid;
                    line_count int;
                    unbalanced int;
                BEGIN
                    IF TG_TABLE_NAME = 'journal_entries' THEN
                        entry := NEW.id;
                    ELSE
                        entry := NEW.journal_entry_id;
                    END IF;
                    SELECT count(*) INTO line_count FROM finance.ledger_lines WHERE journal_entry_id = entry;
                    IF line_count < 2 THEN
                        RAISE EXCEPTION 'journal entry % has % ledger line(s); at least 2 are required', entry, line_count
                            USING ERRCODE = 'check_violation';
                    END IF;
                    SELECT count(*) INTO unbalanced FROM (
                        SELECT currency FROM finance.ledger_lines WHERE journal_entry_id = entry GROUP BY currency HAVING sum(amount) <> 0) u;
                    IF unbalanced > 0 THEN
                        RAISE EXCEPTION 'journal entry % does not balance to zero per currency', entry USING ERRCODE = 'check_violation';
                    END IF;
                    RETURN NULL;
                END
                $$;

                CREATE CONSTRAINT TRIGGER trg_journal_entries_balanced
                    AFTER INSERT ON finance.journal_entries
                    DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION finance.check_journal_entry_balanced();
                CREATE CONSTRAINT TRIGGER trg_ledger_lines_balanced
                    AFTER INSERT ON finance.ledger_lines
                    DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION finance.check_journal_entry_balanced();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_ledger_lines_balanced ON finance.ledger_lines;
                DROP TRIGGER IF EXISTS trg_journal_entries_balanced ON finance.journal_entries;
                DROP TRIGGER IF EXISTS trg_ledger_lines_no_truncate ON finance.ledger_lines;
                DROP TRIGGER IF EXISTS trg_ledger_lines_append_only ON finance.ledger_lines;
                DROP TRIGGER IF EXISTS trg_journal_entries_no_truncate ON finance.journal_entries;
                DROP TRIGGER IF EXISTS trg_journal_entries_append_only ON finance.journal_entries;
                DROP TRIGGER IF EXISTS trg_commission_rules_no_truncate ON finance.commission_rules;
                DROP TRIGGER IF EXISTS trg_commission_rules_only_end ON finance.commission_rules;
                DROP FUNCTION IF EXISTS finance.check_journal_entry_balanced();
                DROP FUNCTION IF EXISTS finance.prevent_ledger_change();
                DROP FUNCTION IF EXISTS finance.guard_commission_rule_change();
                ALTER TABLE finance.commission_rules DROP CONSTRAINT IF EXISTS ex_commission_rules_no_overlap;
                """);
            migrationBuilder.DropTable(
                name: "daily_financial_summaries",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payment_allocations",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "settlement_items",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "ledger_lines",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "commission_rules",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "journal_entries",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "service_completions",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "settlements",
                schema: "finance");
        }
    }
}
