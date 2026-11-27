using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations;

/// <inheritdoc />
public partial class ExtensionsWithoutBots : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_extension_installs_bots_space_id_bot_id",
            table: "extension_installs");

        migrationBuilder.DropIndex(
            name: "ix_extension_installs_space_id_bot_id",
            table: "extension_installs");

        migrationBuilder.DropColumn(
            name: "bot_id",
            table: "extension_installs");

        migrationBuilder.AddColumn<string>(
            name: "settings",
            table: "extension_installs",
            type: "jsonb",
            nullable: false,
            defaultValueSql: "'{}'::jsonb");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "settings",
            table: "extension_installs");

        migrationBuilder.AddColumn<Guid>(
            name: "bot_id",
            table: "extension_installs",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_extension_installs_space_id_bot_id",
            table: "extension_installs",
            columns: new[] { "space_id", "bot_id" });

        migrationBuilder.AddForeignKey(
            name: "fk_extension_installs_bots_space_id_bot_id",
            table: "extension_installs",
            columns: new[] { "space_id", "bot_id" },
            principalTable: "bots",
            principalColumns: new[] { "space_id", "id" },
            onDelete: ReferentialAction.Restrict);
    }
}
