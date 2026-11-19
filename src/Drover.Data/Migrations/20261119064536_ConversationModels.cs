using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConversationModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "model",
                table: "threads",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // The application role updates threads by column. A person who may write in a conversation chooses its model.
            migrationBuilder.Sql("GRANT UPDATE (model) ON threads TO drover_app;");

            migrationBuilder.AddColumn<string>(
                name: "model",
                table: "messages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE UPDATE (model) ON threads FROM drover_app;");

            migrationBuilder.DropColumn(
                name: "model",
                table: "threads");

            migrationBuilder.DropColumn(
                name: "model",
                table: "messages");
        }
    }
}
