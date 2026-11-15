using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class MainThreads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_main",
                table: "threads",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // The application role updates threads by column. Every column but the owner stays updatable.
            migrationBuilder.Sql("GRANT UPDATE (is_main) ON threads TO drover_app;");

            // Each person's main thread with each bot is their open chat thread with the latest activity. The policies
            // apply to the table's owner too, and a migration runs with no tenant, so it would see no row: the owner
            // reads past them for this one statement, and they are forced again before the migration's transaction
            // commits. Nothing here names a space or reaches a row the statement does not mean to change.
            migrationBuilder.Sql(
                """
                ALTER TABLE threads NO FORCE ROW LEVEL SECURITY;

                UPDATE threads
                SET is_main = true
                WHERE id IN (
                  SELECT DISTINCT ON (space_id, bot_id, owner_user_id) id
                  FROM threads
                  WHERE origin = 'chat' AND archived_at IS NULL
                  ORDER BY space_id, bot_id, owner_user_id, last_activity_at DESC, id DESC);

                ALTER TABLE threads FORCE ROW LEVEL SECURITY;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_threads_space_id_bot_id_owner_user_id",
                table: "threads",
                columns: new[] { "space_id", "bot_id", "owner_user_id" },
                unique: true,
                filter: "is_main");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_threads_space_id_bot_id_owner_user_id",
                table: "threads");

            migrationBuilder.DropColumn(
                name: "is_main",
                table: "threads");
        }
    }
}
