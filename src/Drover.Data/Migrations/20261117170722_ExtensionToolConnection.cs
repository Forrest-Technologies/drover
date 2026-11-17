using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExtensionToolConnection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "tool_connection_id",
                table: "extension_installs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_extension_installs_space_id_tool_connection_id",
                table: "extension_installs",
                columns: new[] { "space_id", "tool_connection_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_extension_installs_tool_connections_space_id_tool_connectio",
                table: "extension_installs",
                columns: new[] { "space_id", "tool_connection_id" },
                principalTable: "tool_connections",
                principalColumns: new[] { "space_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_extension_installs_tool_connections_space_id_tool_connectio",
                table: "extension_installs");

            migrationBuilder.DropIndex(
                name: "ix_extension_installs_space_id_tool_connection_id",
                table: "extension_installs");

            migrationBuilder.DropColumn(
                name: "tool_connection_id",
                table: "extension_installs");
        }
    }
}
