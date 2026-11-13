using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class AppliedMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The one column the function reads. The application role still has no privilege on the table.
            migrationBuilder.Sql("GRANT SELECT (migration_id) ON public.\"__EFMigrationsHistory\" TO drover_system;");

            // The status page says whether the database is at the version this build expects. The application role
            // may not read the migration history, and should not: this returns the name of the newest applied
            // migration and nothing else. No tenant data, no argument.
            migrationBuilder.CreateSystemFunction(
                "applied_migration",
                [],
                "text",
                """
                DECLARE
                  newest text;
                BEGIN
                  SELECT max(h.migration_id) INTO newest FROM public."__EFMigrationsHistory" h;
                  INSERT INTO sys.audit (function_name, caller, row_count) VALUES ('applied_migration', session_user, 1);
                  RETURN newest;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSystemFunction("applied_migration", string.Empty);
            migrationBuilder.Sql("REVOKE SELECT (migration_id) ON public.\"__EFMigrationsHistory\" FROM drover_system;");
        }
    }
}
