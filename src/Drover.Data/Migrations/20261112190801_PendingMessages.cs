using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class PendingMessages : Migration
    {
        // A waiting run is claimable once every approval it asked for has an answer.
        private const string AnsweredOnly = """
            (r.status = 'waiting' AND NOT EXISTS (
                              SELECT 1
                              FROM public.tool_calls tc
                              JOIN public.runs tree ON tree.space_id = tc.space_id AND tree.id = tc.run_id
                              WHERE tc.space_id = r.space_id AND tree.root_run_id = r.id AND tc.status = 'pending_approval'))
            """;

        // A waiting run somebody asked to stop is claimable too. A space admin can stop a run whose private thread
        // they cannot see, and so cannot end it themselves: a worker ends it, as the run's own person.
        private const string AnsweredOrCancelled = """
            (r.status = 'waiting' AND (r.cancel_requested_at IS NOT NULL OR NOT EXISTS (
                              SELECT 1
                              FROM public.tool_calls tc
                              JOIN public.runs tree ON tree.space_id = tc.space_id AND tree.id = tc.run_id
                              WHERE tc.space_id = r.space_id AND tree.root_run_id = r.id AND tc.status = 'pending_approval')))
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "position",
                table: "messages",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<bool>(
                name: "steers",
                table: "messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // The claim now reads one more column, and the system role reads by column: it is granted that one only.
            migrationBuilder.Sql("GRANT SELECT (cancel_requested_at) ON runs TO drover_system;");
            migrationBuilder.DropSystemFunction("claim_runs", "text, integer");
            CreateClaimRuns(migrationBuilder, AnsweredOrCancelled);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSystemFunction("claim_runs", "text, integer");
            CreateClaimRuns(migrationBuilder, AnsweredOnly);
            migrationBuilder.Sql("REVOKE SELECT (cancel_requested_at) ON runs FROM drover_system;");

            // A pending message has no place in a schema where every message has a position.
            migrationBuilder.Sql("DELETE FROM messages WHERE position IS NULL;");

            migrationBuilder.DropColumn(
                name: "steers",
                table: "messages");

            migrationBuilder.AlterColumn<int>(
                name: "position",
                table: "messages",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }

        // The same function as in CoreSchema, with the rule for waiting runs passed in. Ids only, audited, as before.
        private static void CreateClaimRuns(MigrationBuilder migrationBuilder, string waitingRule) =>
            migrationBuilder.CreateSystemFunction(
                "claim_runs",
                [("worker", "text"), ("max_runs", "integer")],
                "TABLE (run_id uuid, space_id uuid, principal_user_id uuid)",
                $"""
                #variable_conflict use_column
                BEGIN
                  RETURN QUERY
                  WITH picked AS (
                    SELECT r.id
                    FROM public.runs r
                    WHERE r.parent_run_id IS NULL
                      AND (r.status = 'queued'
                        OR (r.status = 'running' AND r.lease_expires_at < now())
                        OR {waitingRule.Trim()})
                    ORDER BY r.id
                    LIMIT greatest(max_runs, 0)
                    FOR UPDATE OF r SKIP LOCKED),
                  claimed AS (
                    UPDATE public.runs
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
    }
}
