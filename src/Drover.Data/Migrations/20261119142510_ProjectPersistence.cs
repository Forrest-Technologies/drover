using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProjectPersistence : Migration
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
                    instructions = table.Column<string>(type: "character varying(32000)", maxLength: 32000, nullable: false)
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

            migrationBuilder.Sql(
                """
                ALTER TABLE threads
                  ADD CONSTRAINT fk_threads_projects_space_id_project_id
                  FOREIGN KEY (space_id, project_id)
                  REFERENCES projects (space_id, id)
                  ON DELETE SET NULL (project_id);
                """);

            // Project definitions are shared inside one space. Row-level security keeps every other space out.
            migrationBuilder.IsolateBySpace("projects");

            // Only a conversation owner may change its project. The view's owner is still subject to the forced
            // thread policies, and its check option keeps the owner predicate true after an update.
            migrationBuilder.Sql(
                """
                CREATE VIEW owned_thread_projects
                WITH (security_barrier = true)
                AS
                  SELECT id, project_id
                  FROM threads
                  WHERE owner_user_id = sys.current_user_id()
                WITH LOCAL CHECK OPTION;

                REVOKE ALL ON owned_thread_projects FROM PUBLIC;
                GRANT SELECT (id), UPDATE (project_id) ON owned_thread_projects TO drover_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW owned_thread_projects;");

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
