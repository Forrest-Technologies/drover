using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class AgentSessionStateKeepsKeyOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<JsonElement>(
                name: "state",
                table: "agent_sessions",
                type: "json",
                nullable: false,
                defaultValueSql: "'{}'::json",
                oldClrType: typeof(JsonElement),
                oldType: "jsonb",
                oldDefaultValueSql: "'{}'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<JsonElement>(
                name: "state",
                table: "agent_sessions",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb",
                oldClrType: typeof(JsonElement),
                oldType: "json",
                oldDefaultValueSql: "'{}'::json");
        }
    }
}
