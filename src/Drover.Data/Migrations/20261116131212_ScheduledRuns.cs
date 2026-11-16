using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <summary>
    /// Schedules fire. <c>sys.claim_due_schedules</c> claims the due ones across spaces; each schedule records what
    /// happened the last time it was due; a message a schedule sent names it.
    /// </summary>
    public partial class ScheduledRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "schedule_id",
                table: "messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_outcome",
                table: "bot_schedules",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_run_at",
                table: "bot_schedules",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "last_run_id",
                table: "bot_schedules",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_bot_schedules_next_run_at",
                table: "bot_schedules",
                column: "next_run_at",
                filter: "enabled AND next_run_at IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bot_schedules_last_outcome",
                table: "bot_schedules",
                sql: "last_outcome IN ('started', 'waiting_for_reply', 'bot_paused', 'bot_removed', 'owner_left')");

            // What the function reads and changes, by column, and nothing else: never the prompt, never the cron.
            migrationBuilder.Sql("GRANT SELECT (id, space_id, owner_user_id, enabled, next_run_at) ON bot_schedules TO drover_system;");
            migrationBuilder.Sql("GRANT UPDATE (next_run_at) ON bot_schedules TO drover_system;");

            // A schedule is due whether or not anyone is signed in, so finding the due ones has no tenant. The claim
            // and the move of next_run_at are one statement, so two servers never claim one schedule for one due
            // time. PostgreSQL cannot read a cron expression, so the function moves next_run_at only as far as the
            // caller's claim; the caller writes the real next time inside the schedule's own tenant scope, and checks
            // that next_run_at still holds its claim before it sends anything. A claim is between one second and one
            // hour long, so no caller can hold a schedule back for longer. Ids only.
            migrationBuilder.CreateSystemFunction(
                "claim_due_schedules",
                [("at_time", "timestamp with time zone"), ("claim_until", "timestamp with time zone"), ("max_rows", "integer")],
                "TABLE (space_id uuid, schedule_id uuid, owner_user_id uuid)",
                """
                #variable_conflict use_column
                BEGIN
                  RETURN QUERY
                  WITH picked AS (
                    SELECT s.id
                    FROM public.bot_schedules s
                    WHERE s.enabled
                      AND s.next_run_at <= at_time
                      AND claim_until >= at_time + interval '1 second'
                      AND claim_until <= at_time + interval '1 hour'
                    ORDER BY s.next_run_at, s.id
                    LIMIT greatest(max_rows, 0)
                    FOR UPDATE OF s SKIP LOCKED),
                  claimed AS (
                    UPDATE public.bot_schedules
                    SET next_run_at = claim_until
                    FROM picked
                    WHERE bot_schedules.id = picked.id
                    RETURNING bot_schedules.space_id AS claimed_space, bot_schedules.id AS claimed_schedule, bot_schedules.owner_user_id AS claimed_owner),
                  logged AS (
                    INSERT INTO sys.audit (function_name, caller, row_count)
                    SELECT 'claim_due_schedules', session_user, count(*)::integer FROM claimed)
                  SELECT claimed_space, claimed_schedule, claimed_owner FROM claimed;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSystemFunction("claim_due_schedules", "timestamp with time zone, timestamp with time zone, integer");
            migrationBuilder.Sql("REVOKE UPDATE (next_run_at) ON bot_schedules FROM drover_system;");
            migrationBuilder.Sql("REVOKE SELECT (id, space_id, owner_user_id, enabled, next_run_at) ON bot_schedules FROM drover_system;");

            migrationBuilder.DropIndex(
                name: "ix_bot_schedules_next_run_at",
                table: "bot_schedules");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bot_schedules_last_outcome",
                table: "bot_schedules");

            migrationBuilder.DropColumn(
                name: "schedule_id",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "last_outcome",
                table: "bot_schedules");

            migrationBuilder.DropColumn(
                name: "last_run_at",
                table: "bot_schedules");

            migrationBuilder.DropColumn(
                name: "last_run_id",
                table: "bot_schedules");
        }
    }
}
