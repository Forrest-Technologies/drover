using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <summary>
    /// <c>sys.sandboxes_to_sweep</c>: the sandboxes, in every space, that the server's sweep must act on. No schema
    /// change: a creating row's age is read from its id, a version 7 UUID whose first 48 bits are its creation time
    /// in milliseconds, so no column is added for it.
    /// </summary>
    public partial class SandboxSweep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // What the function reads, by column, and nothing else. No secret id: it cannot lead anyone to a token.
            migrationBuilder.Sql("GRANT SELECT (retire_after, expires_at) ON sandboxes TO drover_system;");

            // Retired sandboxes and interrupted creations must be cleared up whether or not their bot is ever used
            // again, so finding them has no tenant. It returns ids only; each sandbox is then handled inside its own
            // space's tenant scope.
            migrationBuilder.CreateSystemFunction(
                "sandboxes_to_sweep",
                [("at_time", "timestamp with time zone"), ("stuck_before", "timestamp with time zone"), ("max_rows", "integer")],
                "TABLE (space_id uuid, sandbox_id uuid)",
                """
                #variable_conflict use_column
                BEGIN
                  RETURN QUERY
                  WITH found AS (
                    SELECT s.space_id AS found_space, s.id AS found_sandbox
                    FROM public.sandboxes s
                    WHERE (s.state = 'retiring' AND (s.retire_after <= at_time OR s.expires_at > at_time))
                       OR (s.state = 'creating'
                           AND substr(s.id::text, 15, 1) = '7'
                           AND ('x' || translate(left(s.id::text, 13), '-', ''))::bit(48)::bigint
                               < (extract(epoch FROM stuck_before) * 1000)::bigint)
                    ORDER BY s.id
                    LIMIT greatest(max_rows, 0)),
                  logged AS (
                    INSERT INTO sys.audit (function_name, caller, row_count)
                    SELECT 'sandboxes_to_sweep', session_user, count(*)::integer FROM found)
                  SELECT found_space, found_sandbox FROM found;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSystemFunction("sandboxes_to_sweep", "timestamp with time zone, timestamp with time zone, integer");
            migrationBuilder.Sql("REVOKE SELECT (retire_after, expires_at) ON sandboxes FROM drover_system;");
        }
    }
}
