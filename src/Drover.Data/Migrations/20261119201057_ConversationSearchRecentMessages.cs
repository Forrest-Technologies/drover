using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConversationSearchRecentMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Built concurrently so writes to messages, the largest table, are not blocked while the index is built.
            // A concurrent build that fails leaves an invalid index behind, so any earlier attempt is dropped first.
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS ix_messages_space_id_created_at_id;",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY ix_messages_space_id_created_at_id ON messages (space_id, created_at DESC, id DESC) WHERE position IS NOT NULL AND shelved_at IS NULL AND role IN ('user', 'assistant');",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_messages_space_id_created_at_id",
                table: "messages");
        }
    }
}
