using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class ToolCallDefinitionHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tool_definition_hash",
                table: "tool_calls",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tool_definition_hash",
                table: "tool_calls");
        }
    }
}
