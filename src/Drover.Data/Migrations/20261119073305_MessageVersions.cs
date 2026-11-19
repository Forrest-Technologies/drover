using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class MessageVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_messages_space_id_thread_id_position",
                table: "messages");

            migrationBuilder.AddColumn<Guid>(
                name: "parent_id",
                table: "messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "shelved_at",
                table: "messages",
                type: "timestamp with time zone",
                nullable: true);

            // Every message that has a position follows the one before it: the conversation so far is its one
            // version. The policies apply to the table's owner too, and a migration runs with no tenant, so it would
            // see no row: the owner reads past them for this one statement, and they are forced again before the
            // migration's transaction commits. Each message is linked within its own thread and space only.
            migrationBuilder.Sql(
                """
                ALTER TABLE messages NO FORCE ROW LEVEL SECURITY;

                UPDATE messages m
                SET parent_id = line.previous_id
                FROM (
                  SELECT id, lag(id) OVER (PARTITION BY space_id, thread_id ORDER BY position) AS previous_id
                  FROM messages
                  WHERE position IS NOT NULL) line
                WHERE m.id = line.id AND line.previous_id IS NOT NULL;

                ALTER TABLE messages FORCE ROW LEVEL SECURITY;
                """);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_messages_space_id_id_audience_user_id",
                table: "messages",
                columns: new[] { "space_id", "id", "audience_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_space_id_parent_id_audience_user_id",
                table: "messages",
                columns: new[] { "space_id", "parent_id", "audience_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_space_id_thread_id_position",
                table: "messages",
                columns: new[] { "space_id", "thread_id", "position" },
                unique: true,
                filter: "shelved_at IS NULL");

            // Sharing a thread carries its new audience to its messages, and so to the link between two of them.
            migrationBuilder.AddForeignKey(
                name: "fk_messages_messages_space_id_parent_id_audience_user_id",
                table: "messages",
                columns: new[] { "space_id", "parent_id", "audience_user_id" },
                principalTable: "messages",
                principalColumns: new[] { "space_id", "id", "audience_user_id" },
                onUpdate: ReferentialAction.Cascade,
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_messages_messages_space_id_parent_id_audience_user_id",
                table: "messages");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_messages_space_id_id_audience_user_id",
                table: "messages");

            migrationBuilder.DropIndex(
                name: "ix_messages_space_id_parent_id_audience_user_id",
                table: "messages");

            migrationBuilder.DropIndex(
                name: "ix_messages_space_id_thread_id_position",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "parent_id",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "shelved_at",
                table: "messages");

            migrationBuilder.CreateIndex(
                name: "ix_messages_space_id_thread_id_position",
                table: "messages",
                columns: new[] { "space_id", "thread_id", "position" },
                unique: true);
        }
    }
}
