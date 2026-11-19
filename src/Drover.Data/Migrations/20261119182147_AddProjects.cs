using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "project_id",
                table: "threads",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    instructions = table.Column<string>(type: "character varying(32000)", maxLength: 32000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projects", x => x.id);
                    table.UniqueConstraint("ak_projects_space_id_id", x => new { x.space_id, x.id });
                    table.CheckConstraint("ck_projects_name", "char_length(btrim(name)) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_threads_space_id_project_id",
                table: "threads",
                columns: new[] { "space_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ix_projects_space_id_updated_at_id",
                table: "projects",
                columns: new[] { "space_id", "updated_at", "id" });

            migrationBuilder.AddForeignKey(
                name: "fk_threads_projects_space_id_project_id",
                table: "threads",
                columns: new[] { "space_id", "project_id" },
                principalTable: "projects",
                principalColumns: new[] { "space_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.IsolateBySpace("projects");

            // The application role updates threads by column. Moving a conversation into or out of a project writes project_id.
            migrationBuilder.Sql("GRANT UPDATE (project_id) ON threads TO drover_app;");

            // Removing a project preserves every conversation in it, including private ones the remover cannot see, so
            // clearing threads.project_id crosses audiences within the caller's own space through an audited sys function.
            migrationBuilder.Sql("GRANT SELECT (id, space_id, project_id) ON threads TO drover_system;");
            migrationBuilder.Sql("GRANT UPDATE (project_id) ON threads TO drover_system;");
            migrationBuilder.Sql("GRANT EXECUTE ON FUNCTION sys.everyone() TO drover_system;");
            migrationBuilder.CreateSystemFunction(
                "clear_project_threads",
                [("target_project_id", "uuid")],
                "integer",
                """
                #variable_conflict use_column
                DECLARE
                  cleared integer;
                BEGIN
                  WITH updated AS (
                    UPDATE public.threads
                    SET project_id = NULL
                    WHERE space_id = sys.current_space() AND project_id = target_project_id
                    RETURNING 1),
                  logged AS (
                    INSERT INTO sys.audit (function_name, caller, row_count)
                    SELECT 'clear_project_threads', session_user, count(*)::integer FROM updated)
                  SELECT count(*)::integer INTO cleared FROM updated;

                  RETURN cleared;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSystemFunction("clear_project_threads", "uuid");
            migrationBuilder.Sql("REVOKE EXECUTE ON FUNCTION sys.everyone() FROM drover_system;");
            migrationBuilder.Sql("REVOKE UPDATE (project_id) ON threads FROM drover_system;");
            migrationBuilder.Sql("REVOKE SELECT (id, space_id, project_id) ON threads FROM drover_system;");
            migrationBuilder.Sql("REVOKE UPDATE (project_id) ON threads FROM drover_app;");

            migrationBuilder.DropForeignKey(
                name: "fk_threads_projects_space_id_project_id",
                table: "threads");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropIndex(
                name: "ix_threads_space_id_project_id",
                table: "threads");

            migrationBuilder.DropColumn(
                name: "project_id",
                table: "threads");
        }
    }
}
