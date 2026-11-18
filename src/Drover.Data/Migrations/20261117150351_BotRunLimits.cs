using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class BotRunLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "run_limits",
                table: "bots",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "run_limits",
                table: "bots");
        }
    }
}
