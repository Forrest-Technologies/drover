using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExtensionInstalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "extension_installs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    package = table.Column<JsonElement>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    bot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    installed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    installed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extension_installs", x => x.id);
                    table.ForeignKey(
                        name: "fk_extension_installs_asp_net_users_installed_by_user_id",
                        column: x => x.installed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_extension_installs_bots_space_id_bot_id",
                        columns: x => new { x.space_id, x.bot_id },
                        principalTable: "bots",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_extension_installs_installed_by_user_id",
                table: "extension_installs",
                column: "installed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_extension_installs_space_id_bot_id",
                table: "extension_installs",
                columns: new[] { "space_id", "bot_id" });

            migrationBuilder.CreateIndex(
                name: "ix_extension_installs_space_id_package_id",
                table: "extension_installs",
                columns: new[] { "space_id", "package_id" },
                unique: true);

            // What a space has imported is the space's own: no other space sees or changes it.
            migrationBuilder.IsolateBySpace("extension_installs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "extension_installs");
        }
    }
}
