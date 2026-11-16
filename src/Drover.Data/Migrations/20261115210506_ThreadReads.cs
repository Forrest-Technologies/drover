using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class ThreadReads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "thread_reads",
                columns: table => new
                {
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    thread_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audience_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seen_through_position = table.Column<int>(type: "integer", nullable: false),
                    seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_thread_reads", x => new { x.space_id, x.thread_id, x.user_id });
                    table.CheckConstraint("ck_thread_reads_position", "seen_through_position >= 0");
                    table.ForeignKey(
                        name: "fk_thread_reads_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_thread_reads_threads_space_id_thread_id_audience_user_id",
                        columns: x => new { x.space_id, x.thread_id, x.audience_user_id },
                        principalTable: "threads",
                        principalColumns: new[] { "space_id", "id", "audience_user_id" },
                        onUpdate: ReferentialAction.Cascade,
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_thread_reads_space_id_thread_id_audience_user_id",
                table: "thread_reads",
                columns: new[] { "space_id", "thread_id", "audience_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_thread_reads_user_id",
                table: "thread_reads",
                column: "user_id");

            // A marker has its thread's audience: a private thread's marker is as private as the thread.
            migrationBuilder.IsolateBySpace("thread_reads", audience: true);

            // Every thread that exists now counts as seen by its owner, so nothing said before this change reads as
            // new. The policies apply to the tables' owner too, and a migration runs with no tenant, so it would see
            // no row: the owner reads past them for this one statement, and they are forced again before the
            // migration's transaction commits. Nothing here names a space or reaches a row it does not mean to write.
            migrationBuilder.Sql(
                """
                ALTER TABLE threads NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE messages NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE thread_reads NO FORCE ROW LEVEL SECURITY;

                INSERT INTO thread_reads (space_id, thread_id, user_id, audience_user_id, seen_through_position, seen_at)
                SELECT t.space_id, t.id, t.owner_user_id, t.audience_user_id,
                       COALESCE((SELECT max(m.position) FROM messages m WHERE m.space_id = t.space_id AND m.thread_id = t.id), 0),
                       now()
                FROM threads t;

                ALTER TABLE thread_reads FORCE ROW LEVEL SECURITY;
                ALTER TABLE messages FORCE ROW LEVEL SECURITY;
                ALTER TABLE threads FORCE ROW LEVEL SECURITY;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "thread_reads");
        }
    }
}
