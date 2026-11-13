using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class BotRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bot_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    checkpoint = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    effect = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    conditions = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bot_rules", x => x.id);
                    table.CheckConstraint("ck_bot_rules_checkpoint", "checkpoint IN ('before_tool', 'after_tool', 'before_reply', 'message_arrives')");
                    table.CheckConstraint("ck_bot_rules_effect", "effect IN ('redact', 'ask', 'block', 'stop')");
                    table.CheckConstraint("ck_bot_rules_position", "position >= 0");
                    table.ForeignKey(
                        name: "fk_bot_rules_asp_net_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bot_rules_bots_space_id_bot_id",
                        columns: x => new { x.space_id, x.bot_id },
                        principalTable: "bots",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bot_rules_created_by_user_id",
                table: "bot_rules",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_bot_rules_space_id_bot_id",
                table: "bot_rules",
                columns: new[] { "space_id", "bot_id" });

            // Rules are space-owned: row-level security keeps one space from reading or writing another's.
            migrationBuilder.IsolateBySpace("bot_rules");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bot_rules");
        }
    }
}
