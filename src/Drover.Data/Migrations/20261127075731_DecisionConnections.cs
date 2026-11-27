using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations;

/// <inheritdoc />
public partial class DecisionConnections : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_secrets_kind",
            table: "secrets");

        migrationBuilder.CreateTable(
            name: "decision_connections",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                space_id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                address = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                account = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                key_secret_id = table.Column<Guid>(type: "uuid", nullable: true),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                tested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                input_price_per_million = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_decision_connections", x => x.id);
                table.CheckConstraint("ck_decision_connections_status", "status IN ('pending', 'ready', 'failed', 'disabled')");
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_secrets_kind",
            table: "secrets",
            sql: "kind IN ('model_key', 'tool_header', 'oauth_tokens', 'oauth_client_secret', 'sandbox_provider_key', 'sandbox_token', 'push_key', 'sandbox_environment', 'decision_key')");

        migrationBuilder.CreateIndex(
            name: "ix_decision_connections_space_id",
            table: "decision_connections",
            column: "space_id",
            unique: true);

        // Space-owned: one space never reads or writes another's decision model, whatever the code above it does.
        migrationBuilder.IsolateBySpace("decision_connections");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "decision_connections");

        migrationBuilder.DropCheckConstraint(
            name: "ck_secrets_kind",
            table: "secrets");

        migrationBuilder.AddCheckConstraint(
            name: "ck_secrets_kind",
            table: "secrets",
            sql: "kind IN ('model_key', 'tool_header', 'oauth_tokens', 'oauth_client_secret', 'sandbox_provider_key', 'sandbox_token', 'push_key', 'sandbox_environment')");
    }
}
