using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class CoreSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    target_kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    data = table.Column<JsonElement>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                    table.CheckConstraint("ck_audit_log_actor_kind", "actor_kind IN ('user', 'bot', 'system')");
                });

            migrationBuilder.CreateTable(
                name: "model_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    base_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    auth_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    credential_scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    secret_id = table.Column<Guid>(type: "uuid", nullable: true),
                    default_model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    balance = table.Column<string>(type: "jsonb", nullable: true),
                    last_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_model_connections", x => x.id);
                    table.UniqueConstraint("ak_model_connections_space_id_id", x => new { x.space_id, x.id });
                    table.CheckConstraint("ck_model_connections_auth_kind", "auth_kind IN ('none', 'api_key', 'sign_in')");
                    table.CheckConstraint("ck_model_connections_credential_scope", "credential_scope IN ('space', 'per_user')");
                    table.CheckConstraint("ck_model_connections_status", "status IN ('pending', 'ready', 'failed', 'disabled')");
                });

            migrationBuilder.CreateTable(
                name: "secrets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audience_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_secrets", x => x.id);
                    table.UniqueConstraint("ak_secrets_space_id_id_audience_user_id", x => new { x.space_id, x.id, x.audience_user_id });
                    table.CheckConstraint("ck_secrets_kind", "kind IN ('model_key', 'tool_header', 'oauth_tokens', 'oauth_client_secret', 'sandbox_provider_key', 'sandbox_token', 'push_key')");
                    table.ForeignKey(
                        name: "fk_secrets_asp_net_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tool_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    transport = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    auth_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    credential_scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    oauth = table.Column<string>(type: "jsonb", nullable: true),
                    trust = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    protocol_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    negotiated_protocol = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_listed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tool_connections", x => x.id);
                    table.UniqueConstraint("ak_tool_connections_space_id_id", x => new { x.space_id, x.id });
                    table.CheckConstraint("ck_tool_connections_auth_kind", "auth_kind IN ('none', 'header', 'oauth')");
                    table.CheckConstraint("ck_tool_connections_credential_scope", "credential_scope IN ('space', 'per_user')");
                    table.CheckConstraint("ck_tool_connections_protocol_mode", "protocol_mode IN ('automatic', 'stateless_only', 'session_only')");
                    table.CheckConstraint("ck_tool_connections_status", "status IN ('pending', 'ready', 'failed', 'disabled')");
                    table.CheckConstraint("ck_tool_connections_transport", "transport IN ('streamable_http', 'sse')");
                    table.CheckConstraint("ck_tool_connections_trust", "trust IN ('trusted', 'untrusted')");
                });

            migrationBuilder.CreateTable(
                name: "bots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_bot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    handle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    avatar = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    description_for_bots = table.Column<string>(type: "text", nullable: false),
                    instructions = table.Column<string>(type: "text", nullable: false),
                    model_connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    model_settings = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    memory_settings = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    sandbox_policy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sandbox_settings = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    depth = table.Column<int>(type: "integer", nullable: false),
                    build_limits = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_bot_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bots", x => x.id);
                    table.UniqueConstraint("ak_bots_space_id_id", x => new { x.space_id, x.id });
                    table.CheckConstraint("ck_bots_depth", "depth >= 0");
                    table.CheckConstraint("ck_bots_sandbox_policy", "sandbox_policy IN ('none', 'private', 'subtree', 'named')");
                    table.CheckConstraint("ck_bots_status", "status IN ('active', 'paused', 'removed')");
                    table.ForeignKey(
                        name: "fk_bots_asp_net_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bots_bots_space_id_created_by_bot_id",
                        columns: x => new { x.space_id, x.created_by_bot_id },
                        principalTable: "bots",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bots_bots_space_id_parent_bot_id",
                        columns: x => new { x.space_id, x.parent_bot_id },
                        principalTable: "bots",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bots_model_connections_space_id_model_connection_id",
                        columns: x => new { x.space_id, x.model_connection_id },
                        principalTable: "model_connections",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "model_catalog",
                columns: table => new
                {
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    capabilities = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    pricing = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    privacy_mode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    traits = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_model_catalog", x => new { x.space_id, x.model_connection_id, x.model });
                    table.CheckConstraint("ck_model_catalog_state", "state IN ('available', 'missing')");
                    table.ForeignKey(
                        name: "fk_model_catalog_model_connections_space_id_model_connection_id",
                        columns: x => new { x.space_id, x.model_connection_id },
                        principalTable: "model_connections",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "oauth_flows",
                columns: table => new
                {
                    state_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audience_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tool_connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oauth_flows", x => x.state_hash);
                    table.ForeignKey(
                        name: "fk_oauth_flows_tool_connections_space_id_tool_connection_id",
                        columns: x => new { x.space_id, x.tool_connection_id },
                        principalTable: "tool_connections",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tool_connection_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audience_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tool_connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secret_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tool_connection_credentials", x => x.id);
                    table.CheckConstraint("ck_tool_connection_credentials_status", "status IN ('active', 'needs_reconnect', 'expired')");
                    table.ForeignKey(
                        name: "fk_tool_connection_credentials_secrets_space_id_secret_id_audi",
                        columns: x => new { x.space_id, x.secret_id, x.audience_user_id },
                        principalTable: "secrets",
                        principalColumns: new[] { "space_id", "id", "audience_user_id" },
                        onUpdate: ReferentialAction.Cascade,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tool_connection_credentials_tool_connections_space_id_tool_",
                        columns: x => new { x.space_id, x.tool_connection_id },
                        principalTable: "tool_connections",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tools",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    tool_connection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "text", nullable: false),
                    input_schema = table.Column<JsonElement>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    annotations = table.Column<JsonElement>(type: "jsonb", nullable: true),
                    definition_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    sensitivity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tools", x => x.id);
                    table.UniqueConstraint("ak_tools_space_id_id", x => new { x.space_id, x.id });
                    table.CheckConstraint("ck_tools_sensitivity", "sensitivity IN ('read', 'write', 'critical')");
                    table.CheckConstraint("ck_tools_source_kind", "source_kind IN ('builtin', 'mcp')");
                    table.CheckConstraint("ck_tools_state", "state IN ('available', 'missing')");
                    table.ForeignKey(
                        name: "fk_tools_tool_connections_space_id_tool_connection_id",
                        columns: x => new { x.space_id, x.tool_connection_id },
                        principalTable: "tool_connections",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bot_ancestors",
                columns: table => new
                {
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ancestor_bot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    descendant_bot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    distance = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bot_ancestors", x => new { x.space_id, x.descendant_bot_id, x.ancestor_bot_id });
                    table.CheckConstraint("ck_bot_ancestors_distance", "distance >= 0");
                    table.ForeignKey(
                        name: "fk_bot_ancestors_bots_space_id_ancestor_bot_id",
                        columns: x => new { x.space_id, x.ancestor_bot_id },
                        principalTable: "bots",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bot_ancestors_bots_space_id_descendant_bot_id",
                        columns: x => new { x.space_id, x.descendant_bot_id },
                        principalTable: "bots",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bot_sandbox_shares",
                columns: table => new
                {
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shared_with_bot_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bot_sandbox_shares", x => new { x.space_id, x.bot_id, x.shared_with_bot_id });
                    table.ForeignKey(
                        name: "fk_bot_sandbox_shares_bots_space_id_bot_id",
                        columns: x => new { x.space_id, x.bot_id },
                        principalTable: "bots",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bot_sandbox_shares_bots_space_id_shared_with_bot_id",
                        columns: x => new { x.space_id, x.shared_with_bot_id },
                        principalTable: "bots",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bot_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cron = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prompt = table.Column<string>(type: "text", nullable: false),
                    thread_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bot_schedules", x => x.id);
                    table.CheckConstraint("ck_bot_schedules_thread_mode", "thread_mode IN ('new_thread', 'same_thread')");
                    table.ForeignKey(
                        name: "fk_bot_schedules_asp_net_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bot_schedules_bots_space_id_bot_id",
                        columns: x => new { x.space_id, x.bot_id },
                        principalTable: "bots",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audience_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_bot_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_files", x => x.id);
                    table.ForeignKey(
                        name: "fk_files_asp_net_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_files_bots_space_id_created_by_bot_id",
                        columns: x => new { x.space_id, x.created_by_bot_id },
                        principalTable: "bots",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    thread_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    principal_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    trigger_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    root_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_tool_call_id = table.Column<Guid>(type: "uuid", nullable: true),
                    depth = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status_reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    error = table.Column<string>(type: "jsonb", nullable: true),
                    tainted = table.Column<bool>(type: "boolean", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    lease_owner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_event_seq = table.Column<long>(type: "bigint", nullable: false),
                    usage = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    subtree_usage = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runs", x => x.id);
                    table.UniqueConstraint("ak_runs_space_id_id", x => new { x.space_id, x.id });
                    table.CheckConstraint("ck_runs_status", "status IN ('queued', 'running', 'waiting', 'completed', 'failed', 'cancelled')");
                    table.CheckConstraint("ck_runs_trigger", "trigger IN ('message', 'schedule', 'delegation')");
                    table.ForeignKey(
                        name: "fk_runs_asp_net_users_principal_user_id",
                        column: x => x.principal_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_runs_bots_space_id_bot_id",
                        columns: x => new { x.space_id, x.bot_id },
                        principalTable: "bots",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_runs_runs_space_id_parent_run_id",
                        columns: x => new { x.space_id, x.parent_run_id },
                        principalTable: "runs",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_runs_runs_space_id_root_run_id",
                        columns: x => new { x.space_id, x.root_run_id },
                        principalTable: "runs",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sandboxes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_bot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider_sandbox_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    access_secret_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retire_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sandboxes", x => x.id);
                    table.UniqueConstraint("ak_sandboxes_space_id_id", x => new { x.space_id, x.id });
                    table.CheckConstraint("ck_sandboxes_policy", "policy IN ('none', 'private', 'subtree', 'named')");
                    table.CheckConstraint("ck_sandboxes_state", "state IN ('creating', 'running', 'paused', 'retiring', 'destroyed')");
                    table.ForeignKey(
                        name: "fk_sandboxes_bots_space_id_owner_bot_id",
                        columns: x => new { x.space_id, x.owner_bot_id },
                        principalTable: "bots",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "threads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audience_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_threads", x => x.id);
                    table.UniqueConstraint("ak_threads_space_id_id_audience_user_id", x => new { x.space_id, x.id, x.audience_user_id });
                    table.CheckConstraint("ck_threads_audience", "audience_user_id IN (sys.everyone(), owner_user_id)");
                    table.CheckConstraint("ck_threads_origin", "origin IN ('chat', 'schedule', 'delegation')");
                    table.ForeignKey(
                        name: "fk_threads_asp_net_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_threads_bots_space_id_bot_id",
                        columns: x => new { x.space_id, x.bot_id },
                        principalTable: "bots",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tool_grants",
                columns: table => new
                {
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tool_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effect = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    review_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    granted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    granted_by_bot_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tool_grants", x => new { x.space_id, x.bot_id, x.tool_id });
                    table.CheckConstraint("ck_tool_grants_effect", "effect IN ('deny', 'ask', 'allow')");
                    table.CheckConstraint("ck_tool_grants_review_state", "review_state IN ('current', 'needs_review')");
                    table.ForeignKey(
                        name: "fk_tool_grants_asp_net_users_granted_by_user_id",
                        column: x => x.granted_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tool_grants_bots_space_id_bot_id",
                        columns: x => new { x.space_id, x.bot_id },
                        principalTable: "bots",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tool_grants_bots_space_id_granted_by_bot_id",
                        columns: x => new { x.space_id, x.granted_by_bot_id },
                        principalTable: "bots",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tool_grants_tools_space_id_tool_id",
                        columns: x => new { x.space_id, x.tool_id },
                        principalTable: "tools",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "usage_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    root_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    cached_tokens = table.Column<long>(type: "bigint", nullable: false),
                    cost = table.Column<decimal>(type: "numeric", nullable: false),
                    currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_usage_records_bots_space_id_bot_id",
                        columns: x => new { x.space_id, x.bot_id },
                        principalTable: "bots",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_usage_records_model_connections_space_id_model_connection_id",
                        columns: x => new { x.space_id, x.model_connection_id },
                        principalTable: "model_connections",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_usage_records_runs_space_id_root_run_id",
                        columns: x => new { x.space_id, x.root_run_id },
                        principalTable: "runs",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_usage_records_runs_space_id_run_id",
                        columns: x => new { x.space_id, x.run_id },
                        principalTable: "runs",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agent_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audience_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    thread_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<JsonElement>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    framework_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_agent_sessions_bots_space_id_bot_id",
                        columns: x => new { x.space_id, x.bot_id },
                        principalTable: "bots",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_agent_sessions_threads_space_id_thread_id_audience_user_id",
                        columns: x => new { x.space_id, x.thread_id, x.audience_user_id },
                        principalTable: "threads",
                        principalColumns: new[] { "space_id", "id", "audience_user_id" },
                        onUpdate: ReferentialAction.Cascade,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audience_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    thread_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    author_bot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parts = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                    table.CheckConstraint("ck_messages_role", "role IN ('system', 'user', 'assistant', 'tool')");
                    table.ForeignKey(
                        name: "fk_messages_asp_net_users_author_user_id",
                        column: x => x.author_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_messages_bots_space_id_author_bot_id",
                        columns: x => new { x.space_id, x.author_bot_id },
                        principalTable: "bots",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_messages_runs_space_id_run_id",
                        columns: x => new { x.space_id, x.run_id },
                        principalTable: "runs",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_messages_threads_space_id_thread_id_audience_user_id",
                        columns: x => new { x.space_id, x.thread_id, x.audience_user_id },
                        principalTable: "threads",
                        principalColumns: new[] { "space_id", "id", "audience_user_id" },
                        onUpdate: ReferentialAction.Cascade,
                        onDelete: ReferentialAction.Restrict);
                });

            // Range-partitioned by month, which EF Core cannot express. Written out in RunEventTable.
            migrationBuilder.CreateRunEventsTable(new DateOnly(2026, 11, 1), 14);

            migrationBuilder.CreateTable(
                name: "tool_calls",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audience_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    thread_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tool_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tool_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    arguments = table.Column<JsonElement>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    result = table.Column<JsonElement>(type: "jsonb", nullable: true),
                    result_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_error = table.Column<bool>(type: "boolean", nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    child_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sandbox_id = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tool_calls", x => x.id);
                    table.CheckConstraint("ck_tool_calls_status", "status IN ('pending_approval', 'approved', 'denied', 'running', 'completed', 'failed', 'interrupted')");
                    table.ForeignKey(
                        name: "fk_tool_calls_asp_net_users_decided_by_user_id",
                        column: x => x.decided_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tool_calls_runs_space_id_child_run_id",
                        columns: x => new { x.space_id, x.child_run_id },
                        principalTable: "runs",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tool_calls_runs_space_id_run_id",
                        columns: x => new { x.space_id, x.run_id },
                        principalTable: "runs",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tool_calls_sandboxes_space_id_sandbox_id",
                        columns: x => new { x.space_id, x.sandbox_id },
                        principalTable: "sandboxes",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tool_calls_threads_space_id_thread_id_audience_user_id",
                        columns: x => new { x.space_id, x.thread_id, x.audience_user_id },
                        principalTable: "threads",
                        principalColumns: new[] { "space_id", "id", "audience_user_id" },
                        onUpdate: ReferentialAction.Cascade,
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tool_calls_tools_space_id_tool_id",
                        columns: x => new { x.space_id, x.tool_id },
                        principalTable: "tools",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_sessions_space_id_bot_id",
                table: "agent_sessions",
                columns: new[] { "space_id", "bot_id" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_sessions_space_id_thread_id_audience_user_id",
                table: "agent_sessions",
                columns: new[] { "space_id", "thread_id", "audience_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_space_id_created_at",
                table: "audit_log",
                columns: new[] { "space_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bot_ancestors_space_id_ancestor_bot_id",
                table: "bot_ancestors",
                columns: new[] { "space_id", "ancestor_bot_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bot_sandbox_shares_space_id_shared_with_bot_id",
                table: "bot_sandbox_shares",
                columns: new[] { "space_id", "shared_with_bot_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bot_schedules_owner_user_id",
                table: "bot_schedules",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_bot_schedules_space_id_bot_id",
                table: "bot_schedules",
                columns: new[] { "space_id", "bot_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bots_created_by_user_id",
                table: "bots",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_bots_space_id_created_by_bot_id",
                table: "bots",
                columns: new[] { "space_id", "created_by_bot_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bots_space_id_model_connection_id",
                table: "bots",
                columns: new[] { "space_id", "model_connection_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bots_space_id_parent_bot_id",
                table: "bots",
                columns: new[] { "space_id", "parent_bot_id" });

            migrationBuilder.CreateIndex(
                name: "ix_files_created_by_user_id",
                table: "files",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_files_space_id_created_by_bot_id",
                table: "files",
                columns: new[] { "space_id", "created_by_bot_id" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_author_user_id",
                table: "messages",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_messages_space_id_author_bot_id",
                table: "messages",
                columns: new[] { "space_id", "author_bot_id" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_space_id_run_id",
                table: "messages",
                columns: new[] { "space_id", "run_id" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_space_id_thread_id_audience_user_id",
                table: "messages",
                columns: new[] { "space_id", "thread_id", "audience_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_space_id_thread_id_position",
                table: "messages",
                columns: new[] { "space_id", "thread_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_oauth_flows_expires_at",
                table: "oauth_flows",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_flows_space_id_tool_connection_id",
                table: "oauth_flows",
                columns: new[] { "space_id", "tool_connection_id" });

            migrationBuilder.CreateIndex(
                name: "ix_run_events_space_id_run_id",
                table: "run_events",
                columns: new[] { "space_id", "run_id" });

            migrationBuilder.CreateIndex(
                name: "ix_run_events_space_id_thread_id_audience_user_id",
                table: "run_events",
                columns: new[] { "space_id", "thread_id", "audience_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_runs_principal_user_id",
                table: "runs",
                column: "principal_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_runs_space_id_bot_id",
                table: "runs",
                columns: new[] { "space_id", "bot_id" });

            migrationBuilder.CreateIndex(
                name: "ix_runs_space_id_parent_run_id",
                table: "runs",
                columns: new[] { "space_id", "parent_run_id" });

            migrationBuilder.CreateIndex(
                name: "ix_runs_space_id_root_run_id",
                table: "runs",
                columns: new[] { "space_id", "root_run_id" });

            migrationBuilder.CreateIndex(
                name: "ix_runs_status_lease_expires_at",
                table: "runs",
                columns: new[] { "status", "lease_expires_at" },
                filter: "status IN ('queued', 'running', 'waiting')");

            migrationBuilder.CreateIndex(
                name: "ix_sandboxes_space_id_owner_bot_id",
                table: "sandboxes",
                columns: new[] { "space_id", "owner_bot_id" });

            migrationBuilder.CreateIndex(
                name: "ix_secrets_created_by_user_id",
                table: "secrets",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_threads_owner_user_id",
                table: "threads",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_threads_space_id_bot_id",
                table: "threads",
                columns: new[] { "space_id", "bot_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tool_calls_decided_by_user_id",
                table: "tool_calls",
                column: "decided_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tool_calls_space_id_child_run_id",
                table: "tool_calls",
                columns: new[] { "space_id", "child_run_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tool_calls_space_id_run_id",
                table: "tool_calls",
                columns: new[] { "space_id", "run_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tool_calls_space_id_sandbox_id",
                table: "tool_calls",
                columns: new[] { "space_id", "sandbox_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tool_calls_space_id_status",
                table: "tool_calls",
                columns: new[] { "space_id", "status" },
                filter: "status = 'pending_approval'");

            migrationBuilder.CreateIndex(
                name: "ix_tool_calls_space_id_thread_id_audience_user_id",
                table: "tool_calls",
                columns: new[] { "space_id", "thread_id", "audience_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tool_calls_space_id_tool_id",
                table: "tool_calls",
                columns: new[] { "space_id", "tool_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tool_connection_credentials_space_id_secret_id_audience_use",
                table: "tool_connection_credentials",
                columns: new[] { "space_id", "secret_id", "audience_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tool_connection_credentials_space_id_tool_connection_id",
                table: "tool_connection_credentials",
                columns: new[] { "space_id", "tool_connection_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tool_connections_space_id_slug",
                table: "tool_connections",
                columns: new[] { "space_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tool_grants_granted_by_user_id",
                table: "tool_grants",
                column: "granted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tool_grants_space_id_granted_by_bot_id",
                table: "tool_grants",
                columns: new[] { "space_id", "granted_by_bot_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tool_grants_space_id_tool_id",
                table: "tool_grants",
                columns: new[] { "space_id", "tool_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tools_space_id_tool_connection_id_name",
                table: "tools",
                columns: new[] { "space_id", "tool_connection_id", "name" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_usage_records_space_id_bot_id",
                table: "usage_records",
                columns: new[] { "space_id", "bot_id" });

            migrationBuilder.CreateIndex(
                name: "ix_usage_records_space_id_model_connection_id",
                table: "usage_records",
                columns: new[] { "space_id", "model_connection_id" });

            migrationBuilder.CreateIndex(
                name: "ix_usage_records_space_id_root_run_id",
                table: "usage_records",
                columns: new[] { "space_id", "root_run_id" });

            migrationBuilder.CreateIndex(
                name: "ix_usage_records_space_id_run_id",
                table: "usage_records",
                columns: new[] { "space_id", "run_id" });

            // Row-level security. Every table is space-owned; a table with an audience also gets the restrictive policy.
            foreach (var table in new[]
            {
                "bots", "bot_ancestors", "bot_sandbox_shares", "bot_schedules", "tool_grants", "model_connections",
                "model_catalog", "tool_connections", "tools", "runs", "usage_records", "sandboxes", "audit_log",
            })
            {
                migrationBuilder.IsolateBySpace(table);
            }

            foreach (var table in new[]
            {
                "tool_connection_credentials", "oauth_flows", "threads", "messages", "run_events", "tool_calls",
                "agent_sessions", "files", "secrets",
            })
            {
                migrationBuilder.IsolateBySpace(table, audience: true);
            }

            // Special cases, each covered by a named test in PrivilegeTests and ThreadSharingTests.
            // The application role may append to the audit log and read it, and nothing more.
            migrationBuilder.Sql("REVOKE UPDATE, DELETE ON audit_log FROM drover_app;");

            // A thread's owner never changes, so one member cannot take over another's thread: no privilege on the column.
            migrationBuilder.Sql("REVOKE UPDATE ON threads FROM drover_app;");
            migrationBuilder.Sql(
                """
                GRANT UPDATE (id, space_id, audience_user_id, bot_id, title, origin, last_activity_at, archived_at)
                  ON threads TO drover_app;
                """);

            // The effective grant of a bot, one row per tool: the minimum of its own grant and every ancestor's. It runs
            // as the caller, under row-level security, so it sees only the current space. A tool that the bot or any
            // ancestor has no grant for is absent. A grant that needs review counts as ask unless it already denies.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION effective_grants(target_bot_id uuid)
                RETURNS TABLE (tool_id uuid, effect text)
                LANGUAGE sql STABLE
                AS $function$
                  SELECT g.tool_id,
                         (ARRAY['deny', 'ask', 'allow'])[
                           min(CASE WHEN g.review_state = 'needs_review' THEN LEAST(ranked.rank, 1) ELSE ranked.rank END) + 1]
                  FROM bot_ancestors a
                  JOIN tool_grants g ON g.space_id = a.space_id AND g.bot_id = a.ancestor_bot_id
                  CROSS JOIN LATERAL (SELECT CASE g.effect WHEN 'deny' THEN 0 WHEN 'ask' THEN 1 ELSE 2 END AS rank) ranked
                  WHERE a.descendant_bot_id = target_bot_id
                  GROUP BY g.tool_id
                  HAVING count(*) = (SELECT count(*) FROM bot_ancestors WHERE descendant_bot_id = target_bot_id)
                $function$;
                REVOKE ALL ON FUNCTION effective_grants(uuid) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION effective_grants(uuid) TO drover_app;
                """);

            // Every sys function writes one row here, and the application role cannot read or change it.
            migrationBuilder.Sql(
                """
                CREATE TABLE sys.audit (
                  id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                  function_name text NOT NULL,
                  caller text NOT NULL,
                  called_at timestamp with time zone NOT NULL DEFAULT now(),
                  row_count integer NOT NULL
                );
                """);

            // drover_system gets exactly what its functions read or change, table by table.
            migrationBuilder.Sql(
                """
                GRANT INSERT ON sys.audit TO drover_system;
                GRANT SELECT, UPDATE ON runs TO drover_system;
                GRANT SELECT ON tool_calls, users, memberships, spaces TO drover_system;
                GRANT SELECT, DELETE ON oauth_flows, sandboxes TO drover_system;
                """);

            // Lease runnable root runs across spaces. Returns ids only; the worker then opens a tenant scope.
            migrationBuilder.CreateSystemFunction(
                "claim_runs",
                [("worker", "text"), ("max_runs", "integer")],
                "TABLE (run_id uuid, space_id uuid, principal_user_id uuid)",
                """
                #variable_conflict use_column
                BEGIN
                  RETURN QUERY
                  WITH picked AS (
                    SELECT r.id
                    FROM runs r
                    WHERE r.parent_run_id IS NULL
                      AND (r.status = 'queued'
                        OR (r.status = 'running' AND r.lease_expires_at < now())
                        OR (r.status = 'waiting' AND NOT EXISTS (
                              SELECT 1
                              FROM tool_calls tc
                              JOIN runs tree ON tree.space_id = tc.space_id AND tree.id = tc.run_id
                              WHERE tc.space_id = r.space_id AND tree.root_run_id = r.id AND tc.status = 'pending_approval')))
                    ORDER BY r.id
                    LIMIT greatest(max_runs, 0)
                    FOR UPDATE OF r SKIP LOCKED),
                  claimed AS (
                    UPDATE runs
                    SET attempt = CASE WHEN runs.status = 'running' THEN runs.attempt + 1 ELSE runs.attempt END,
                        status = 'running',
                        status_reason = NULL,
                        lease_owner = worker,
                        lease_expires_at = now() + interval '45 seconds',
                        started_at = coalesce(runs.started_at, now())
                    FROM picked
                    WHERE runs.id = picked.id
                    RETURNING runs.id AS claimed_run, runs.space_id AS claimed_space, runs.principal_user_id AS claimed_user),
                  logged AS (
                    INSERT INTO sys.audit (function_name, caller, row_count)
                    SELECT 'claim_runs', session_user, count(*)::integer FROM claimed)
                  SELECT claimed_run, claimed_space, claimed_user FROM claimed;
                END;
                """);

            // Sign-in happens before any space is known, so the lookup cannot run in a tenant scope.
            migrationBuilder.CreateSystemFunction(
                "find_user_for_sign_in",
                [("sign_in_email", "text")],
                """
                TABLE (user_id uuid, user_name text, display_name text, password_hash text, security_stamp text,
                  email_confirmed boolean, two_factor_enabled boolean, lockout_end timestamp with time zone,
                  lockout_enabled boolean, access_failed_count integer)
                """,
                """
                #variable_conflict use_column
                BEGIN
                  RETURN QUERY
                  WITH found AS (
                    SELECT u.id AS found_id, u.user_name::text AS found_name, u.display_name::text AS found_display,
                           u.password_hash AS found_hash, u.security_stamp AS found_stamp, u.email_confirmed AS found_confirmed,
                           u.two_factor_enabled AS found_two_factor, u.lockout_end AS found_lockout_end,
                           u.lockout_enabled AS found_lockout_enabled, u.access_failed_count AS found_failures
                    FROM users u
                    WHERE u.normalized_email = upper(sign_in_email)
                    ORDER BY u.id),
                  logged AS (
                    INSERT INTO sys.audit (function_name, caller, row_count)
                    SELECT 'find_user_for_sign_in', session_user, count(*)::integer FROM found)
                  SELECT found_id, found_name, found_display, found_hash, found_stamp, found_confirmed, found_two_factor,
                         found_lockout_end, found_lockout_enabled, found_failures
                  FROM found;
                END;
                """);

            // After sign-in and before a space is chosen: which spaces the person can pick from.
            migrationBuilder.CreateSystemFunction(
                "memberships_for_user",
                [("target_user", "uuid")],
                "TABLE (space_id uuid, kind text, name text, slug text, role text)",
                """
                #variable_conflict use_column
                BEGIN
                  RETURN QUERY
                  WITH found AS (
                    SELECT s.id AS found_space, s.kind::text AS found_kind, s.name::text AS found_name,
                           s.slug::text AS found_slug, m.role::text AS found_role
                    FROM memberships m
                    JOIN spaces s ON s.id = m.space_id
                    WHERE m.user_id = target_user
                    ORDER BY s.name, s.id),
                  logged AS (
                    INSERT INTO sys.audit (function_name, caller, row_count)
                    SELECT 'memberships_for_user', session_user, count(*)::integer FROM found)
                  SELECT found_space, found_kind, found_name, found_slug, found_role FROM found;
                END;
                """);

            // An OAuth callback arrives with no tenant: map the hash of its state to the space and user that started it.
            migrationBuilder.CreateSystemFunction(
                "resolve_oauth_state",
                [("hash", "text")],
                "TABLE (space_id uuid, user_id uuid, tool_connection_id uuid)",
                """
                #variable_conflict use_column
                BEGIN
                  RETURN QUERY
                  WITH found AS (
                    SELECT f.space_id AS found_space, f.audience_user_id AS found_user, f.tool_connection_id AS found_connection
                    FROM oauth_flows f
                    WHERE f.state_hash = hash AND f.expires_at > now()),
                  logged AS (
                    INSERT INTO sys.audit (function_name, caller, row_count)
                    SELECT 'resolve_oauth_state', session_user, count(*)::integer FROM found)
                  SELECT found_space, found_user, found_connection FROM found;
                END;
                """);

            // Cleanup across spaces: expired sign-in flows, and sandboxes that were destroyed and that no tool call mentions.
            migrationBuilder.CreateSystemFunction(
                "purge_expired",
                [("max_rows", "integer")],
                "integer",
                """
                DECLARE
                  flows integer;
                  boxes integer;
                BEGIN
                  WITH gone AS (
                    DELETE FROM oauth_flows
                    WHERE state_hash IN (
                      SELECT f.state_hash FROM oauth_flows f
                      WHERE f.expires_at < now()
                      ORDER BY f.expires_at
                      LIMIT greatest(max_rows, 0))
                    RETURNING 1)
                  SELECT count(*) INTO flows FROM gone;

                  WITH gone AS (
                    DELETE FROM sandboxes
                    WHERE id IN (
                      SELECT s.id FROM sandboxes s
                      WHERE s.state = 'destroyed'
                        AND NOT EXISTS (SELECT 1 FROM tool_calls tc WHERE tc.space_id = s.space_id AND tc.sandbox_id = s.id)
                      ORDER BY s.id
                      LIMIT greatest(max_rows, 0))
                    RETURNING 1)
                  SELECT count(*) INTO boxes FROM gone;

                  INSERT INTO sys.audit (function_name, caller, row_count) VALUES ('purge_expired', session_user, flows + boxes);
                  RETURN flows + boxes;
                END;
                """);

            // Counts for the status page and the first-run check. No tenant data.
            migrationBuilder.CreateSystemFunction(
                "health",
                [],
                """
                TABLE (queued_runs bigint, running_runs bigint, waiting_runs bigint, expired_leases bigint,
                  oldest_lease_expires_at timestamp with time zone, user_count bigint)
                """,
                """
                #variable_conflict use_column
                BEGIN
                  RETURN QUERY
                  WITH counts AS (
                    SELECT count(*) FILTER (WHERE r.status = 'queued') AS queued,
                           count(*) FILTER (WHERE r.status = 'running') AS running,
                           count(*) FILTER (WHERE r.status = 'waiting') AS waiting,
                           count(*) FILTER (WHERE r.status = 'running' AND r.lease_expires_at < now()) AS expired,
                           min(r.lease_expires_at) FILTER (WHERE r.status = 'running') AS oldest
                    FROM runs r),
                  people AS (SELECT count(*) AS total FROM users),
                  logged AS (
                    INSERT INTO sys.audit (function_name, caller, row_count) SELECT 'health', session_user, 1)
                  SELECT counts.queued, counts.running, counts.waiting, counts.expired, counts.oldest, people.total
                  FROM counts, people;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSystemFunction("health", string.Empty);
            migrationBuilder.DropSystemFunction("purge_expired", "integer");
            migrationBuilder.DropSystemFunction("resolve_oauth_state", "text");
            migrationBuilder.DropSystemFunction("memberships_for_user", "uuid");
            migrationBuilder.DropSystemFunction("find_user_for_sign_in", "text");
            migrationBuilder.DropSystemFunction("claim_runs", "text, integer");
            migrationBuilder.Sql("DROP TABLE sys.audit;");
            migrationBuilder.Sql("DROP FUNCTION effective_grants(uuid);");

            migrationBuilder.DropTable(
                name: "agent_sessions");

            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "bot_ancestors");

            migrationBuilder.DropTable(
                name: "bot_sandbox_shares");

            migrationBuilder.DropTable(
                name: "bot_schedules");

            migrationBuilder.DropTable(
                name: "files");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "model_catalog");

            migrationBuilder.DropTable(
                name: "oauth_flows");

            migrationBuilder.DropTable(
                name: "run_events");

            migrationBuilder.DropTable(
                name: "tool_calls");

            migrationBuilder.DropTable(
                name: "tool_connection_credentials");

            migrationBuilder.DropTable(
                name: "tool_grants");

            migrationBuilder.DropTable(
                name: "usage_records");

            migrationBuilder.DropTable(
                name: "sandboxes");

            migrationBuilder.DropTable(
                name: "threads");

            migrationBuilder.DropTable(
                name: "secrets");

            migrationBuilder.DropTable(
                name: "tools");

            migrationBuilder.DropTable(
                name: "runs");

            migrationBuilder.DropTable(
                name: "tool_connections");

            migrationBuilder.DropTable(
                name: "bots");

            migrationBuilder.DropTable(
                name: "model_connections");
        }
    }
}
