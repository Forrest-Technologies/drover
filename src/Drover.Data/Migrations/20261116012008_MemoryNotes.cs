using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class MemoryNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "memories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audience_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    written_by_bot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    written_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_thread_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tainted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_memories", x => x.id);
                    table.CheckConstraint("ck_memories_source", "(source_thread_id IS NULL) = (source_message_id IS NULL) AND (written_by_bot_id IS NOT NULL OR source_message_id IS NULL)");
                    table.CheckConstraint("ck_memories_text", "char_length(btrim(text)) > 0");
                    table.CheckConstraint("ck_memories_writer", "(written_by_bot_id IS NULL) <> (written_by_user_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_memories_asp_net_users_written_by_user_id",
                        column: x => x.written_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_memories_bots_space_id_bot_id",
                        columns: x => new { x.space_id, x.bot_id },
                        principalTable: "bots",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_memories_bots_space_id_written_by_bot_id",
                        columns: x => new { x.space_id, x.written_by_bot_id },
                        principalTable: "bots",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_memories_space_id_bot_id_audience_user_id",
                table: "memories",
                columns: new[] { "space_id", "bot_id", "audience_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_memories_space_id_written_by_bot_id",
                table: "memories",
                columns: new[] { "space_id", "written_by_bot_id" });

            migrationBuilder.CreateIndex(
                name: "ix_memories_written_by_user_id",
                table: "memories",
                column: "written_by_user_id");

            // Notes are space-owned and private to the person they are about: row-level security keeps one space from
            // reading another's, and one member from reading another member's.
            migrationBuilder.IsolateBySpace("memories", audience: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "memories");
        }
    }
}
