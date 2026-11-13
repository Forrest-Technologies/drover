using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class ForgetToolCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // What the function needs beyond what it already reads, and the two deletes, by table.
            migrationBuilder.Sql("GRANT SELECT (secret_id) ON tool_connection_credentials TO drover_system;");
            migrationBuilder.Sql("GRANT SELECT (id, space_id) ON secrets TO drover_system;");
            migrationBuilder.Sql("GRANT DELETE ON tool_connection_credentials, secrets TO drover_system;");

            // It reads the space from the transaction, as the other functions read the user.
            migrationBuilder.Sql("GRANT EXECUTE ON FUNCTION sys.current_space() TO drover_system;");

            // Removing a connection must take every sign-in to it with it. A person's own tokens are invisible to
            // anybody else's transaction, by design, so the remover cannot delete them: this crosses audiences
            // within one space, and nothing else. It deletes only rows of the caller's own space, which it reads
            // from the transaction, and returns how many sign-ins it forgot.
            migrationBuilder.CreateSystemFunction(
                "forget_tool_credentials",
                [("connection_id", "uuid")],
                "integer",
                """
                #variable_conflict use_column
                DECLARE
                  removed integer;
                BEGIN
                  WITH of_this_space AS (
                    SELECT c.id AS credential_id, c.secret_id AS secret_id
                    FROM public.tool_connection_credentials c
                    WHERE c.tool_connection_id = connection_id AND c.space_id = sys.current_space()),
                  credentials_gone AS (
                    DELETE FROM public.tool_connection_credentials
                    WHERE id IN (SELECT credential_id FROM of_this_space)
                    RETURNING 1),
                  secrets_gone AS (
                    DELETE FROM public.secrets
                    WHERE space_id = sys.current_space() AND id IN (SELECT secret_id FROM of_this_space)
                    RETURNING 1),
                  logged AS (
                    INSERT INTO sys.audit (function_name, caller, row_count)
                    SELECT 'forget_tool_credentials', session_user, count(*)::integer FROM credentials_gone)
                  SELECT count(*)::integer INTO removed FROM credentials_gone;

                  RETURN removed;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSystemFunction("forget_tool_credentials", "uuid");
            migrationBuilder.Sql("REVOKE EXECUTE ON FUNCTION sys.current_space() FROM drover_system;");
            migrationBuilder.Sql("REVOKE DELETE ON tool_connection_credentials, secrets FROM drover_system;");
            migrationBuilder.Sql("REVOKE SELECT (id, space_id) ON secrets FROM drover_system;");
            migrationBuilder.Sql("REVOKE SELECT (secret_id) ON tool_connection_credentials FROM drover_system;");
        }
    }
}
