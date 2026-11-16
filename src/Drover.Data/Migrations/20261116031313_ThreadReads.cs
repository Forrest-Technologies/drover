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
                    audience_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    thread_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seen_through_position = table.Column<int>(type: "integer", nullable: false),
                    seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_thread_reads", x => new { x.space_id, x.thread_id, x.audience_user_id });
                    table.CheckConstraint("ck_thread_reads_position", "seen_through_position >= 0");
                });

            // A marker's audience is its person: the database shows it to them alone and lets nobody write one for
            // someone else. The thread is a plain id, like runs.thread_id: a key to the thread would have to carry
            // the thread's audience, which for a shared thread is everyone.
            migrationBuilder.IsolateBySpace("thread_reads", audience: true);

            // Every thread that exists now counts as seen by every member who can see it (its owner, or each member
            // of the space when it is shared), so an upgrade shows no old reply as new and no old run as failed. The
            // policies apply to the tables' owner too, and a migration runs with no tenant, so it would see no row:
            // the owner reads past them for this one statement, and they are forced again before the migration's
            // transaction commits. Nothing here names a space or reaches a row it does not mean to write.
            migrationBuilder.Sql(
                """
                ALTER TABLE threads NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE messages NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE memberships NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE thread_reads NO FORCE ROW LEVEL SECURITY;

                INSERT INTO thread_reads (space_id, thread_id, audience_user_id, seen_through_position, seen_at)
                SELECT t.space_id, t.id, m.user_id,
                       COALESCE((SELECT max(x.position) FROM messages x WHERE x.space_id = t.space_id AND x.thread_id = t.id), 0),
                       now()
                FROM threads t
                JOIN memberships m ON m.space_id = t.space_id
                WHERE t.audience_user_id IN (sys.everyone(), m.user_id);

                ALTER TABLE thread_reads FORCE ROW LEVEL SECURITY;
                ALTER TABLE memberships FORCE ROW LEVEL SECURITY;
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
