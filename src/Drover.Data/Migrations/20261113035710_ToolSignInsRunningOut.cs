using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class ToolSignInsRunningOut : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // What the function reads, by column, and nothing else. No secret id: it cannot lead anyone to a token.
            migrationBuilder.Sql("GRANT SELECT (id, space_id, audience_user_id, tool_connection_id, status, expires_at) ON tool_connection_credentials TO drover_system;");
            migrationBuilder.Sql("GRANT SELECT (id, space_id, auth_kind, status) ON tool_connections TO drover_system;");

            // Renewing sign-ins ahead of time has no tenant: it must find them in every space. It returns ids only,
            // and each sign-in is then renewed inside its own space's tenant scope, as the person it belongs to.
            migrationBuilder.CreateSystemFunction(
                "tool_sign_ins_running_out",
                [("before", "timestamp with time zone"), ("max_rows", "integer")],
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
                    ORDER BY c.expires_at
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
            migrationBuilder.DropSystemFunction("tool_sign_ins_running_out", "timestamp with time zone, integer");
            migrationBuilder.Sql("REVOKE SELECT (id, space_id, auth_kind, status) ON tool_connections FROM drover_system;");
            migrationBuilder.Sql("REVOKE SELECT (id, space_id, audience_user_id, tool_connection_id, status, expires_at) ON tool_connection_credentials FROM drover_system;");
        }
    }
}
