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
            migrationBuilder.CreateIndex(
                name: "ix_messages_space_id_created_at_id",
                table: "messages",
                columns: new[] { "space_id", "created_at", "id" },
                descending: new[] { false, true, true },
                filter: "position IS NOT NULL AND shelved_at IS NULL AND role IN ('user', 'assistant')");
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
