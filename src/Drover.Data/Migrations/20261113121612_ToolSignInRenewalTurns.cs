using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class ToolSignInRenewalTurns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "renewed_at",
                table: "tool_connection_credentials",
                type: "timestamp with time zone",
                nullable: true);


            // The function reads one more column, and the system role reads by column: it is granted that one only.
            migrationBuilder.Sql("GRANT SELECT (renewed_at) ON tool_connection_credentials TO drover_system;");
            migrationBuilder.DropSystemFunction("tool_sign_ins_running_out", "timestamp with time zone, timestamp with time zone, integer");

            // As before, with two changes. A sign-in renewed since a given moment is left out: tokens that last less
            // than the lead would otherwise be due again at every pass. And the spaces take turns, one sign-in from
            // each before a second from any: ordered by end alone, one space with more sign-ins running out than a
            // pass holds kept every other space's waiting until theirs had run out.
            migrationBuilder.CreateSystemFunction(
                "tool_sign_ins_running_out",
                [("before", "timestamp with time zone"), ("not_tried_since", "timestamp with time zone"), ("not_renewed_since", "timestamp with time zone"), ("max_rows", "integer")],
                "TABLE (space_id uuid, audience_user_id uuid, tool_connection_id uuid, credential_id uuid)",
                """
                #variable_conflict use_column
                BEGIN
                  RETURN QUERY
                  WITH due AS (
                    SELECT c.space_id AS found_space, c.audience_user_id AS found_audience, c.tool_connection_id AS found_connection, c.id AS found_credential,
                           c.renew_attempted_at AS tried_at, c.expires_at AS runs_out_at,
                           row_number() OVER (PARTITION BY c.space_id ORDER BY c.renew_attempted_at NULLS FIRST, c.expires_at, c.id) AS turn
                    FROM public.tool_connection_credentials c
                    JOIN public.tool_connections t ON t.id = c.tool_connection_id AND t.space_id = c.space_id
                    WHERE t.auth_kind = 'oauth' AND t.status <> 'disabled'
                      AND c.status = 'active' AND c.expires_at IS NOT NULL AND c.expires_at < before
                      AND (c.renew_attempted_at IS NULL OR c.renew_attempted_at < not_tried_since)
                      AND (c.renewed_at IS NULL OR c.renewed_at < not_renewed_since)),
                  found AS (
                    SELECT found_space, found_audience, found_connection, found_credential
                    FROM due
                    ORDER BY turn, tried_at NULLS FIRST, runs_out_at, found_credential
                    LIMIT greatest(max_rows, 0)),
                  logged AS (
                    INSERT INTO sys.audit (function_name, caller, row_count)
                    SELECT 'tool_sign_ins_running_out', session_user, count(*)::integer FROM found)
                  SELECT found_space, found_audience, found_connection, found_credential FROM found;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSystemFunction("tool_sign_ins_running_out", "timestamp with time zone, timestamp with time zone, timestamp with time zone, integer");
            migrationBuilder.CreateSystemFunction(
                "tool_sign_ins_running_out",
                [("before", "timestamp with time zone"), ("not_tried_since", "timestamp with time zone"), ("max_rows", "integer")],
                "TABLE (space_id uuid, audience_user_id uuid, tool_connection_id uuid, credential_id uuid)",
                """
                #variable_conflict use_column
                BEGIN
                  RETURN QUERY
                  WITH found AS (
                    SELECT c.space_id AS found_space, c.audience_user_id AS found_audience, c.tool_connection_id AS found_connection, c.id AS found_credential
                    FROM public.tool_connection_credentials c
                    JOIN public.tool_connections t ON t.id = c.tool_connection_id AND t.space_id = c.space_id
                    WHERE t.auth_kind = 'oauth' AND t.status <> 'disabled'
                      AND c.status = 'active' AND c.expires_at IS NOT NULL AND c.expires_at < before
                      AND (c.renew_attempted_at IS NULL OR c.renew_attempted_at < not_tried_since)
                    ORDER BY c.renew_attempted_at NULLS FIRST, c.expires_at
                    LIMIT greatest(max_rows, 0)),
                  logged AS (
                    INSERT INTO sys.audit (function_name, caller, row_count)
                    SELECT 'tool_sign_ins_running_out', session_user, count(*)::integer FROM found)
                  SELECT found_space, found_audience, found_connection, found_credential FROM found;
                END;
                """);
            migrationBuilder.Sql("REVOKE SELECT (renewed_at) ON tool_connection_credentials FROM drover_system;");

            migrationBuilder.DropColumn(
                name: "renewed_at",
                table: "tool_connection_credentials");
        }
    }
}
