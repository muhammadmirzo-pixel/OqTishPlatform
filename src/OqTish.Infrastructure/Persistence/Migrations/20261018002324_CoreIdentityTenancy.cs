using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OqTish.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoreIdentityTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.EnsureSchema(
                name: "staff");

            migrationBuilder.EnsureSchema(
                name: "tenancy");

            migrationBuilder.EnsureSchema(
                name: "common");

            migrationBuilder.EnsureSchema(
                name: "notify");

            migrationBuilder.CreateTable(
                name: "audit_log",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    actor_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: true),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    target_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ip_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    details = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                    table.CheckConstraint("ck_audit_log_actor_kind", "actor_kind IN ('User', 'System', 'Anonymous')");
                });

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    dedup_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    not_before = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                    table.CheckConstraint("ck_outbox_messages_attempts", "attempts >= 0");
                    table.CheckConstraint("ck_outbox_messages_status", "status IN ('Pending', 'Processing', 'Sent', 'Failed', 'Cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    security_stamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_status", "status IN ('Active', 'Suspended', 'Deleted')");
                });

            migrationBuilder.CreateTable(
                name: "auth_identities",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    normalized_identifier = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auth_identities", x => x.id);
                    table.CheckConstraint("ck_auth_identities_kind", "kind IN ('Phone', 'Telegram', 'EmailPassword')");
                    table.ForeignKey(
                        name: "fk_auth_identities_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "clinics",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    legal_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    verification_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clinics", x => x.id);
                    table.CheckConstraint("ck_clinics_verification_status", "verification_status IN ('Pending', 'Approved', 'Suspended', 'Rejected')");
                    table.ForeignKey(
                        name: "fk_clinics_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                schema: "common",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    response_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_keys", x => new { x.user_id, x.key });
                    table.CheckConstraint("ck_idempotency_keys_status", "status IN ('InProgress', 'Completed')");
                    table.ForeignKey(
                        name: "fk_idempotency_keys_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "otp_challenges",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    identifier_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ip_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_otp_challenges", x => x.id);
                    table.CheckConstraint("ck_otp_challenges_attempts", "attempts >= 0 AND attempts <= max_attempts");
                    table.CheckConstraint("ck_otp_challenges_expiry", "expires_at > created_at");
                    table.CheckConstraint("ck_otp_challenges_purpose", "purpose IN ('SignIn', 'LinkPhone', 'PasswordReset')");
                    table.ForeignKey(
                        name: "fk_otp_challenges_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "password_credentials",
                schema: "identity",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_password_credentials", x => x.user_id);
                    table.CheckConstraint("ck_password_credentials_failed_attempts", "failed_attempts >= 0");
                    table.ForeignKey(
                        name: "fk_password_credentials_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "patient_profiles",
                schema: "identity",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    contact_phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_anonymised = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_profiles", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_patient_profiles_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "platform_admin_grants",
                schema: "staff",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    granted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_admin_grants", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_admin_grants_users_granted_by_user_id",
                        column: x => x.granted_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_platform_admin_grants_users_revoked_by_user_id",
                        column: x => x.revoked_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_platform_admin_grants_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    security_stamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    auth_method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ip_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_sessions_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "telegram_link_tokens",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_telegram_link_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_telegram_link_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "telegram_links",
                schema: "notify",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_telegram_links", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_telegram_links_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "totp_credentials",
                schema: "identity",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    protected_secret = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    enabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_step = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_totp_credentials", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_totp_credentials_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "branches",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    district = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "Asia/Tashkent"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branches", x => x.id);
                    table.UniqueConstraint("ak_branches_clinic_id_id", x => new { x.clinic_id, x.id });
                    table.CheckConstraint("ck_branches_lat_lng_pair", "(latitude IS NULL) = (longitude IS NULL)");
                    table.CheckConstraint("ck_branches_latitude", "latitude IS NULL OR (latitude >= -90 AND latitude <= 90)");
                    table.CheckConstraint("ck_branches_longitude", "longitude IS NULL OR (longitude >= -180 AND longitude <= 180)");
                    table.ForeignKey(
                        name: "fk_branches_clinics_clinic_id",
                        column: x => x.clinic_id,
                        principalSchema: "tenancy",
                        principalTable: "clinics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "clinic_memberships",
                schema: "staff",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clinic_memberships", x => x.id);
                    table.UniqueConstraint("ak_clinic_memberships_clinic_id_id", x => new { x.clinic_id, x.id });
                    table.CheckConstraint("ck_clinic_memberships_role", "role IN ('ClinicOwner', 'ClinicAdmin', 'Receptionist', 'Doctor', 'FinanceManager')");
                    table.CheckConstraint("ck_clinic_memberships_scope", "scope IN ('Clinic', 'Branches')");
                    table.CheckConstraint("ck_clinic_memberships_status", "status IN ('Active', 'Suspended', 'Removed')");
                    table.ForeignKey(
                        name: "fk_clinic_memberships_clinics_clinic_id",
                        column: x => x.clinic_id,
                        principalSchema: "tenancy",
                        principalTable: "clinics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_clinic_memberships_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "staff_invitations",
                schema: "staff",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    phone_masked = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    branch_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    invited_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_invitations", x => x.id);
                    table.CheckConstraint("ck_staff_invitations_branches", "scope <> 'Branches' OR cardinality(branch_ids) > 0");
                    table.CheckConstraint("ck_staff_invitations_expiry", "expires_at > created_at");
                    table.CheckConstraint("ck_staff_invitations_role", "role IN ('ClinicOwner', 'ClinicAdmin', 'Receptionist', 'Doctor', 'FinanceManager')");
                    table.CheckConstraint("ck_staff_invitations_scope", "scope IN ('Clinic', 'Branches')");
                    table.ForeignKey(
                        name: "fk_staff_invitations_clinics_clinic_id",
                        column: x => x.clinic_id,
                        principalSchema: "tenancy",
                        principalTable: "clinics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_staff_invitations_users_accepted_by_user_id",
                        column: x => x.accepted_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_staff_invitations_users_invited_by_user_id",
                        column: x => x.invited_by_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "branch_booking_locks",
                schema: "tenancy",
                columns: table => new
                {
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branch_booking_locks", x => x.branch_id);
                    table.ForeignKey(
                        name: "fk_branch_booking_locks_branches_clinic_id_branch_id",
                        columns: x => new { x.clinic_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "branch_opening_hours",
                schema: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    weekday = table.Column<short>(type: "smallint", nullable: false),
                    opens = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    closes = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branch_opening_hours", x => x.id);
                    table.CheckConstraint("ck_branch_opening_hours_interval", "opens < closes");
                    table.CheckConstraint("ck_branch_opening_hours_weekday", "weekday BETWEEN 1 AND 7");
                    table.ForeignKey(
                        name: "fk_branch_opening_hours_branches_clinic_id_branch_id",
                        columns: x => new { x.clinic_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "branch_access",
                schema: "staff",
                columns: table => new
                {
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clinic_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branch_access", x => new { x.membership_id, x.branch_id });
                    table.ForeignKey(
                        name: "fk_branch_access_branches_clinic_id_branch_id",
                        columns: x => new { x.clinic_id, x.branch_id },
                        principalSchema: "tenancy",
                        principalTable: "branches",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_branch_access_clinic_memberships_clinic_id_membership_id",
                        columns: x => new { x.clinic_id, x.membership_id },
                        principalSchema: "staff",
                        principalTable: "clinic_memberships",
                        principalColumns: new[] { "clinic_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_action_occurred_at",
                schema: "audit",
                table: "audit_log",
                columns: new[] { "action", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_actor_user_id_occurred_at",
                schema: "audit",
                table: "audit_log",
                columns: new[] { "actor_user_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_clinic_id_occurred_at",
                schema: "audit",
                table: "audit_log",
                columns: new[] { "clinic_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_auth_identities_kind_normalized_identifier",
                schema: "identity",
                table: "auth_identities",
                columns: new[] { "kind", "normalized_identifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_auth_identities_user_id_kind",
                schema: "identity",
                table: "auth_identities",
                columns: new[] { "user_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branch_access_clinic_id_branch_id",
                schema: "staff",
                table: "branch_access",
                columns: new[] { "clinic_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_branch_access_clinic_id_membership_id",
                schema: "staff",
                table: "branch_access",
                columns: new[] { "clinic_id", "membership_id" });

            migrationBuilder.CreateIndex(
                name: "ix_branch_booking_locks_clinic_id_branch_id",
                schema: "tenancy",
                table: "branch_booking_locks",
                columns: new[] { "clinic_id", "branch_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branch_opening_hours_branch_id_weekday_opens",
                schema: "tenancy",
                table: "branch_opening_hours",
                columns: new[] { "branch_id", "weekday", "opens" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branch_opening_hours_clinic_id_branch_id",
                schema: "tenancy",
                table: "branch_opening_hours",
                columns: new[] { "clinic_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_branches_clinic_id_is_active",
                schema: "tenancy",
                table: "branches",
                columns: new[] { "clinic_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_branches_district",
                schema: "tenancy",
                table: "branches",
                column: "district");

            migrationBuilder.CreateIndex(
                name: "ix_clinic_memberships_clinic_id_user_id",
                schema: "staff",
                table: "clinic_memberships",
                columns: new[] { "clinic_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_clinic_memberships_user_id_status",
                schema: "staff",
                table: "clinic_memberships",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_clinics_created_by_user_id",
                schema: "tenancy",
                table: "clinics",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_clinics_verification_status",
                schema: "tenancy",
                table: "clinics",
                column: "verification_status");

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_created_at",
                schema: "common",
                table: "idempotency_keys",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_otp_challenges_created_at",
                schema: "identity",
                table: "otp_challenges",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_otp_challenges_identifier_hash_created_at",
                schema: "identity",
                table: "otp_challenges",
                columns: new[] { "identifier_hash", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_otp_challenges_ip_hash_created_at",
                schema: "identity",
                table: "otp_challenges",
                columns: new[] { "ip_hash", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_otp_challenges_user_id",
                schema: "identity",
                table: "otp_challenges",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dedup_key",
                schema: "notify",
                table: "outbox_messages",
                column: "dedup_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_due",
                schema: "notify",
                table: "outbox_messages",
                column: "next_attempt_at",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_platform_admin_grants_granted_by_user_id",
                schema: "staff",
                table: "platform_admin_grants",
                column: "granted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_admin_grants_revoked_by_user_id",
                schema: "staff",
                table: "platform_admin_grants",
                column: "revoked_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_admin_grants_user_id_permission",
                schema: "staff",
                table: "platform_admin_grants",
                columns: new[] { "user_id", "permission" },
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_token_hash",
                schema: "identity",
                table: "sessions",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessions_user_id_revoked_at",
                schema: "identity",
                table: "sessions",
                columns: new[] { "user_id", "revoked_at" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_invitations_accepted_by_user_id",
                schema: "staff",
                table: "staff_invitations",
                column: "accepted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_staff_invitations_clinic_id_created_at",
                schema: "staff",
                table: "staff_invitations",
                columns: new[] { "clinic_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_invitations_invited_by_user_id",
                schema: "staff",
                table: "staff_invitations",
                column: "invited_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_staff_invitations_phone_hash",
                schema: "staff",
                table: "staff_invitations",
                column: "phone_hash",
                filter: "accepted_at IS NULL AND revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_telegram_link_tokens_token_hash",
                schema: "notify",
                table: "telegram_link_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_telegram_link_tokens_user_id",
                schema: "notify",
                table: "telegram_link_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_telegram_links_telegram_user_id",
                schema: "notify",
                table: "telegram_links",
                column: "telegram_user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_status",
                schema: "identity",
                table: "users",
                column: "status");

            // --- Raw SQL (docs/DATABASE.md §5): triggers and grants EF cannot model. ---

            // Every branch gets its booking-lock row in the same statement (IRowLocks.LockBranchAsync).
            migrationBuilder.Sql("""
                CREATE FUNCTION tenancy.create_branch_booking_lock() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    INSERT INTO tenancy.branch_booking_locks (branch_id, clinic_id)
                    VALUES (NEW.id, NEW.clinic_id)
                    ON CONFLICT (branch_id) DO NOTHING;
                    RETURN NEW;
                END
                $$;

                CREATE TRIGGER trg_branches_booking_lock
                    AFTER INSERT ON tenancy.branches
                    FOR EACH ROW EXECUTE FUNCTION tenancy.create_branch_booking_lock();
                """);

            // audit.audit_log is append-only: trigger guard for everyone, plus no UPDATE/DELETE grants for the app role.
            migrationBuilder.Sql("""
                CREATE FUNCTION audit.prevent_audit_log_change() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'audit.audit_log is append-only (% is not allowed)', TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END
                $$;

                CREATE TRIGGER trg_audit_log_append_only
                    BEFORE UPDATE OR DELETE ON audit.audit_log
                    FOR EACH ROW EXECUTE FUNCTION audit.prevent_audit_log_change();

                CREATE TRIGGER trg_audit_log_no_truncate
                    BEFORE TRUNCATE ON audit.audit_log
                    FOR EACH STATEMENT EXECUTE FUNCTION audit.prevent_audit_log_change();

                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'oqtish_app') THEN
                        REVOKE UPDATE, DELETE, TRUNCATE ON audit.audit_log FROM oqtish_app;
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_audit_log_no_truncate ON audit.audit_log;
                DROP TRIGGER IF EXISTS trg_audit_log_append_only ON audit.audit_log;
                DROP FUNCTION IF EXISTS audit.prevent_audit_log_change();
                DROP TRIGGER IF EXISTS trg_branches_booking_lock ON tenancy.branches;
                DROP FUNCTION IF EXISTS tenancy.create_branch_booking_lock();
                """);

            migrationBuilder.DropTable(
                name: "audit_log",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "auth_identities",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "branch_access",
                schema: "staff");

            migrationBuilder.DropTable(
                name: "branch_booking_locks",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "branch_opening_hours",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "data_protection_keys",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "idempotency_keys",
                schema: "common");

            migrationBuilder.DropTable(
                name: "otp_challenges",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "password_credentials",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "patient_profiles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "platform_admin_grants",
                schema: "staff");

            migrationBuilder.DropTable(
                name: "sessions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "staff_invitations",
                schema: "staff");

            migrationBuilder.DropTable(
                name: "telegram_link_tokens",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "telegram_links",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "totp_credentials",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "clinic_memberships",
                schema: "staff");

            migrationBuilder.DropTable(
                name: "branches",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "clinics",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "users",
                schema: "identity");
        }
    }
}
