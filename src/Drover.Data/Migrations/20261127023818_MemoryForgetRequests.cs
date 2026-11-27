using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations;

/// <inheritdoc />
public partial class _20261127023818_MemoryForgetRequests : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "memory_forget_requests",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                space_id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                person_id = table.Column<Guid>(type: "uuid", nullable: false),
                bot_id = table.Column<Guid>(type: "uuid", nullable: true),
                thread_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_memory_forget_requests", x => x.id);
                table.CheckConstraint("ck_memory_forget_requests_kind", "kind IN ('conversation', 'person')");
                table.CheckConstraint("ck_memory_forget_requests_scope", "(kind = 'conversation' AND bot_id IS NOT NULL AND thread_id IS NOT NULL) OR (kind = 'person' AND bot_id IS NULL AND thread_id IS NULL)");
            });

        migrationBuilder.CreateIndex(
            name: "ix_memory_forget_requests_space_id_attempted_at_created_at_id",
            table: "memory_forget_requests",
            columns: new[] { "space_id", "attempted_at", "created_at", "id" });

        // Space-owned durable forget queue: isolated by space, and read across spaces only by ids for hourly retries.
        migrationBuilder.IsolateBySpace("memory_forget_requests");

        // Column-level SELECT only: never exposes person, bot, or thread ids to the cross-space discovery function.
        migrationBuilder.Sql("GRANT SELECT (id, space_id, attempted_at, created_at) ON memory_forget_requests TO drover_system;");

        // Retrying refused memory forgets has no tenant: finds pending rows across all spaces, taking turns across
        // spaces so one space cannot starve others, and returns space and request ids only.
        migrationBuilder.CreateSystemFunction(
            "memory_forget_requests_due",
            [("not_tried_since", "timestamp with time zone"), ("max_rows", "integer")],
            "TABLE (space_id uuid, request_id uuid)",
            """
            #variable_conflict use_column
            BEGIN
              RETURN QUERY
              WITH due AS (
                SELECT r.space_id AS found_space, r.id AS found_request,
                       r.attempted_at AS tried_at, r.created_at AS made_at,
                       row_number() OVER (PARTITION BY r.space_id ORDER BY r.attempted_at NULLS FIRST, r.created_at, r.id) AS turn
                FROM public.memory_forget_requests r
                WHERE r.attempted_at IS NULL OR r.attempted_at < not_tried_since),
              found AS (
                SELECT found_space, found_request
                FROM due
                ORDER BY turn, tried_at NULLS FIRST, made_at, found_request
                LIMIT greatest(max_rows, 0)),
              logged AS (
                INSERT INTO sys.audit (function_name, caller, row_count)
                SELECT 'memory_forget_requests_due', session_user, count(*)::integer FROM found)
              SELECT found_space, found_request FROM found;
            END;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropSystemFunction("memory_forget_requests_due", "timestamp with time zone, integer");

        migrationBuilder.DropTable(
            name: "memory_forget_requests");
    }
}
