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

            // Enforce conversation ownership for project_id changes at the database boundary: a shared thread is
            // visible and writable to every space member, so row-level security alone cannot stop another member from
            // moving someone else's thread between projects. sys.clear_project_threads runs as drover_system when an
            // owner or admin removes a project.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION public.enforce_thread_project_owner()
                RETURNS trigger
                LANGUAGE plpgsql
                SET search_path = pg_catalog, public, pg_temp
                AS $function$
                BEGIN
                  IF NEW.project_id IS DISTINCT FROM OLD.project_id AND current_user <> 'drover_system' THEN
                    IF OLD.owner_user_id IS DISTINCT FROM sys.current_user_id() THEN
                      RAISE EXCEPTION 'Only the thread owner may change its project membership.'
                        USING ERRCODE = 'insufficient_privilege';
                    END IF;
                  END IF;
                  RETURN NEW;
                END;
                $function$;
                REVOKE ALL ON FUNCTION public.enforce_thread_project_owner() FROM PUBLIC;

                CREATE TRIGGER trg_threads_project_owner
                  BEFORE UPDATE OF project_id ON public.threads
                  FOR EACH ROW
                  EXECUTE FUNCTION public.enforce_thread_project_owner();
                """);

            // Removing a project preserves every conversation in it, including private ones the remover cannot see, so
            // clearing threads.project_id crosses audiences within the caller's own space through an audited sys
            // function that checks owner or admin membership and returns no private row count.
            migrationBuilder.Sql("GRANT SELECT (id, space_id, project_id) ON threads TO drover_system;");
            migrationBuilder.Sql("GRANT UPDATE (project_id) ON threads TO drover_system;");
            migrationBuilder.Sql("GRANT EXECUTE ON FUNCTION sys.everyone() TO drover_system;");
            migrationBuilder.CreateSystemFunction(
                "clear_project_threads",
                [("target_project_id", "uuid")],
                "void",
                """
                #variable_conflict use_column
                BEGIN
                  IF NOT EXISTS (
                    SELECT 1
                    FROM public.memberships m
                    WHERE m.space_id = sys.current_space()
                      AND m.user_id = sys.current_user_id()
                      AND m.role IN ('owner', 'admin')) THEN
                    RAISE EXCEPTION 'Only space owners and admins may clear project threads.'
                      USING ERRCODE = 'insufficient_privilege';
                  END IF;

                  WITH updated AS (
                    UPDATE public.threads
                    SET project_id = NULL
                    WHERE space_id = sys.current_space() AND project_id = target_project_id
                    RETURNING 1)
                  INSERT INTO sys.audit (function_name, caller, row_count)
                  SELECT 'clear_project_threads', session_user, count(*)::integer FROM updated;
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
            migrationBuilder.Sql("DROP TRIGGER trg_threads_project_owner ON public.threads;");
            migrationBuilder.Sql("DROP FUNCTION public.enforce_thread_project_owner();");
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
