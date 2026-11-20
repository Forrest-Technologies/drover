using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class SandboxSecrets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_secrets_kind",
                table: "secrets");

            migrationBuilder.CreateTable(
                name: "sandbox_secrets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    hosts = table.Column<List<string>>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    allow_in_query = table.Column<bool>(type: "boolean", nullable: false),
                    allow_in_body = table.Column<bool>(type: "boolean", nullable: false),
                    scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    secret_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    rotated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rotated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sandbox_secrets", x => x.id);
                    table.UniqueConstraint("ak_sandbox_secrets_space_id_id", x => new { x.space_id, x.id });
                    table.CheckConstraint("ck_sandbox_secrets_binding", "(mode = 'brokered' AND cardinality(hosts) > 0) OR (mode = 'raw' AND cardinality(hosts) = 0 AND scope <> 'project')");
                    table.CheckConstraint("ck_sandbox_secrets_mode", "mode IN ('brokered', 'raw')");
                    table.CheckConstraint("ck_sandbox_secrets_name", "name ~ '^[A-Z_][A-Z0-9_]{0,63}$'");
                    table.CheckConstraint("ck_sandbox_secrets_project", "(scope = 'project') = (project_id IS NOT NULL)");
                    table.CheckConstraint("ck_sandbox_secrets_scope", "scope IN ('space', 'bots', 'project')");
                    table.ForeignKey(
                        name: "fk_sandbox_secrets_asp_net_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sandbox_secrets_asp_net_users_rotated_by_user_id",
                        column: x => x.rotated_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sandbox_secrets_projects_space_id_project_id",
                        columns: x => new { x.space_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sandbox_secret_bots",
                columns: table => new
                {
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sandbox_secret_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sandbox_secret_bots", x => new { x.space_id, x.sandbox_secret_id, x.bot_id });
                    table.ForeignKey(
                        name: "fk_sandbox_secret_bots_bots_space_id_bot_id",
                        columns: x => new { x.space_id, x.bot_id },
                        principalTable: "bots",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sandbox_secret_bots_sandbox_secrets_space_id_sandbox_secret",
                        columns: x => new { x.space_id, x.sandbox_secret_id },
                        principalTable: "sandbox_secrets",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_secrets_kind",
                table: "secrets",
                sql: "kind IN ('model_key', 'tool_header', 'oauth_tokens', 'oauth_client_secret', 'sandbox_provider_key', 'sandbox_token', 'push_key', 'sandbox_environment')");

            migrationBuilder.CreateIndex(
                name: "ix_sandbox_secret_bots_space_id_bot_id_name",
                table: "sandbox_secret_bots",
                columns: new[] { "space_id", "bot_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sandbox_secrets_created_by_user_id",
                table: "sandbox_secrets",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_sandbox_secrets_rotated_by_user_id",
                table: "sandbox_secrets",
                column: "rotated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_sandbox_secrets_secret_id",
                table: "sandbox_secrets",
                column: "secret_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_sandbox_secrets_project_name",
                table: "sandbox_secrets",
                columns: new[] { "space_id", "project_id", "name" },
                unique: true,
                filter: "scope = 'project'");

            migrationBuilder.CreateIndex(
                name: "ux_sandbox_secrets_space_name",
                table: "sandbox_secrets",
                columns: new[] { "space_id", "name" },
                unique: true,
                filter: "scope = 'space'");

            // Sandbox secrets are space-owned: row-level security keeps one space from reading or writing another's.
            migrationBuilder.IsolateBySpace("sandbox_secrets");
            migrationBuilder.IsolateBySpace("sandbox_secret_bots");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sandbox_secret_bots");

            migrationBuilder.DropTable(
                name: "sandbox_secrets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_secrets_kind",
                table: "secrets");

            migrationBuilder.AddCheckConstraint(
                name: "ck_secrets_kind",
                table: "secrets",
                sql: "kind IN ('model_key', 'tool_header', 'oauth_tokens', 'oauth_client_secret', 'sandbox_provider_key', 'sandbox_token', 'push_key')");
        }
    }
}
