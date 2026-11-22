using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class CredentialBroker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_secrets_kind",
                table: "secrets");

            migrationBuilder.AddColumn<byte[]>(
                name: "broker_token_hash",
                table: "sandboxes",
                type: "bytea",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "broker_authorities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    certificate = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    secret_id = table.Column<Guid>(type: "uuid", nullable: false),
                    not_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broker_authorities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sandbox_secret_entries",
                columns: table => new
                {
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sandbox_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sandbox_secret_id = table.Column<Guid>(type: "uuid", nullable: false),
                    placeholder_hash = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sandbox_secret_entries", x => new { x.space_id, x.sandbox_id, x.sandbox_secret_id });
                    table.ForeignKey(
                        name: "fk_sandbox_secret_entries_sandbox_secrets_space_id_sandbox_sec",
                        columns: x => new { x.space_id, x.sandbox_secret_id },
                        principalTable: "sandbox_secrets",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sandbox_secret_entries_sandboxes_space_id_sandbox_id",
                        columns: x => new { x.space_id, x.sandbox_id },
                        principalTable: "sandboxes",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_secrets_kind",
                table: "secrets",
                sql: "kind IN ('model_key', 'tool_header', 'oauth_tokens', 'oauth_client_secret', 'sandbox_provider_key', 'sandbox_token', 'push_key', 'sandbox_environment', 'broker_authority')");

            migrationBuilder.CreateIndex(
                name: "ix_sandboxes_broker_token_hash",
                table: "sandboxes",
                column: "broker_token_hash",
                unique: true,
                filter: "broker_token_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_broker_authorities_secret_id",
                table: "broker_authorities",
                column: "secret_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_broker_authorities_space_id_not_after",
                table: "broker_authorities",
                columns: new[] { "space_id", "not_after" });

            migrationBuilder.CreateIndex(
                name: "ix_sandbox_secret_entries_space_id_sandbox_id_placeholder_hash",
                table: "sandbox_secret_entries",
                columns: new[] { "space_id", "sandbox_id", "placeholder_hash" },
                filter: "placeholder_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sandbox_secret_entries_space_id_sandbox_secret_id",
                table: "sandbox_secret_entries",
                columns: new[] { "space_id", "sandbox_secret_id" });

            // Broker data is space-owned: row-level security keeps one space from reading or writing another's.
            migrationBuilder.IsolateBySpace("broker_authorities");
            migrationBuilder.IsolateBySpace("sandbox_secret_entries");

            // A sandbox's request to the broker carries no space, so finding the sandbox a token belongs to has no
            // tenant. It reads the hash and the state, and returns ids only; the broker then reads the sandbox inside
            // that space's own tenant scope.
            migrationBuilder.Sql("GRANT SELECT (id, space_id, state, broker_token_hash) ON sandboxes TO drover_system;");
            migrationBuilder.CreateSystemFunction(
                "broker_sandbox_for_token",
                [("token_hash", "bytea")],
                "TABLE (space_id uuid, sandbox_id uuid)",
                """
                #variable_conflict use_column
                BEGIN
                  RETURN QUERY
                  WITH found AS (
                    SELECT s.space_id AS found_space, s.id AS found_sandbox
                    FROM public.sandboxes s
                    WHERE s.broker_token_hash = token_hash
                      AND s.state IN ('creating', 'running', 'paused')
                    LIMIT 1),
                  logged AS (
                    INSERT INTO sys.audit (function_name, caller, row_count)
                    SELECT 'broker_sandbox_for_token', session_user, count(*)::integer FROM found)
                  SELECT found_space, found_sandbox FROM found;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSystemFunction("broker_sandbox_for_token", "bytea");
            migrationBuilder.Sql("REVOKE SELECT (broker_token_hash) ON sandboxes FROM drover_system;");

            migrationBuilder.DropTable(
                name: "broker_authorities");

            migrationBuilder.DropTable(
                name: "sandbox_secret_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_secrets_kind",
                table: "secrets");

            migrationBuilder.DropIndex(
                name: "ix_sandboxes_broker_token_hash",
                table: "sandboxes");

            migrationBuilder.DropColumn(
                name: "broker_token_hash",
                table: "sandboxes");

            migrationBuilder.AddCheckConstraint(
                name: "ck_secrets_kind",
                table: "secrets",
                sql: "kind IN ('model_key', 'tool_header', 'oauth_tokens', 'oauth_client_secret', 'sandbox_provider_key', 'sandbox_token', 'push_key', 'sandbox_environment')");
        }
    }
}
