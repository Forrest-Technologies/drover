using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class AppliedMigrationFunction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The application role has no privilege on EF Core's bookkeeping, so the status page cannot see which
            // migration was applied. This reads that one column and returns the newest id, nothing else.
            migrationBuilder.Sql("""GRANT SELECT (migration_id) ON "__EFMigrationsHistory" TO drover_system;""");
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
            migrationBuilder.Sql("""REVOKE SELECT (migration_id) ON "__EFMigrationsHistory" FROM drover_system;""");
        }
    }
}
