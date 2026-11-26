using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations;

/// <inheritdoc />
public partial class PersonalModelCredentials : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "model_connection_credentials",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                space_id = table.Column<Guid>(type: "uuid", nullable: false),
                audience_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                model_connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                secret_id = table.Column<Guid>(type: "uuid", nullable: false),
                account_label = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                renewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_model_connection_credentials", x => x.id);
                table.CheckConstraint("ck_model_connection_credentials_one_person", "audience_user_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("ck_model_connection_credentials_status", "status IN ('active', 'needs_sign_in')");
                table.ForeignKey(
                    name: "fk_model_connection_credentials_model_connections_space_id_mod",
                    columns: x => new { x.space_id, x.model_connection_id },
                    principalTable: "model_connections",
                    principalColumns: new[] { "space_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_model_connection_credentials_space_id_model_connection_id_a",
            table: "model_connection_credentials",
            columns: new[] { "space_id", "model_connection_id", "audience_user_id" },
            unique: true);

        // A person's own sign-in: space-owned, and readable only inside a transaction running for that one person.
        // The shared audience is refused twice over, by the check above and by the one-person policy.
        migrationBuilder.IsolateBySpace("model_connection_credentials", audience: true);
        migrationBuilder.IsolateToOnePerson("model_connection_credentials");

        // What the function reads, by column, and nothing else. No secret id: it cannot lead anyone to a token.
        migrationBuilder.Sql("GRANT SELECT (id, space_id, audience_user_id, model_connection_id, status, expires_at, renewed_at) ON model_connection_credentials TO drover_system;");

        // Renewing sign-ins that sat unused has no tenant: it must find them in every space. It returns ids only, and
        // each sign-in is then renewed inside its own space's tenant scope, as the person it belongs to. A sign-in
        // with no end is left out: there is nothing to renew. The spaces take turns, one from each before a second
        // from any, so one space cannot take every pass.
        migrationBuilder.CreateSystemFunction(
            "model_sign_ins_due",
            [("not_renewed_since", "timestamp with time zone"), ("max_rows", "integer")],
            "TABLE (space_id uuid, audience_user_id uuid, model_connection_id uuid, credential_id uuid)",
            """
            #variable_conflict use_column
            BEGIN
              RETURN QUERY
              WITH due AS (
                SELECT c.space_id AS found_space, c.audience_user_id AS found_audience, c.model_connection_id AS found_connection, c.id AS found_credential,
                       c.renewed_at AS last_renewed,
                       row_number() OVER (PARTITION BY c.space_id ORDER BY c.renewed_at NULLS FIRST, c.id) AS turn
                FROM public.model_connection_credentials c
                WHERE c.status = 'active' AND c.expires_at IS NOT NULL
                  AND (c.renewed_at IS NULL OR c.renewed_at < not_renewed_since)),
              found AS (
                SELECT found_space, found_audience, found_connection, found_credential
                FROM due
                ORDER BY turn, last_renewed NULLS FIRST, found_credential
                LIMIT greatest(max_rows, 0)),
              logged AS (
                INSERT INTO sys.audit (function_name, caller, row_count)
                SELECT 'model_sign_ins_due', session_user, count(*)::integer FROM found)
              SELECT found_space, found_audience, found_connection, found_credential FROM found;
            END;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropSystemFunction("model_sign_ins_due", "timestamp with time zone, integer");

        migrationBuilder.DropTable(
            name: "model_connection_credentials");
    }
}
