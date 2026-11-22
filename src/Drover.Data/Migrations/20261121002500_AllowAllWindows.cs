using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowAllWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "decided_by_window_id",
                table: "tool_calls",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "allow_all_windows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audience_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    started_from = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    tool_call_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_allow_all_windows", x => x.id);
                    table.CheckConstraint("ck_allow_all_windows_ended", "(ended_at IS NULL AND ended_reason IS NULL) OR (ended_at IS NOT NULL AND ended_reason IS NOT NULL)");
                    table.CheckConstraint("ck_allow_all_windows_ended_reason", "ended_reason IN ('expired', 'work_done', 'asked_again', 'bot_paused', 'bot_removed', 'member_left')");
                    table.CheckConstraint("ck_allow_all_windows_kind", "kind IN ('minutes_30', 'hour_1', 'until_done', 'until_off')");
                    table.CheckConstraint("ck_allow_all_windows_ends_at", "(kind IN ('minutes_30', 'hour_1')) = (ends_at IS NOT NULL)");
                    table.CheckConstraint("ck_allow_all_windows_started_from", "started_from IN ('bot', 'approval')");
                    table.CheckConstraint("ck_allow_all_windows_source", "(started_from = 'approval') = (tool_call_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_allow_all_windows_bots_space_id_bot_id",
                        columns: x => new { x.space_id, x.bot_id },
                        principalTable: "bots",
                        principalColumns: ["space_id", "id"],
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_allow_all_windows_one_open",
                table: "allow_all_windows",
                columns: ["space_id", "bot_id", "audience_user_id"],
                unique: true,
                filter: "ended_at IS NULL");

            // A window belongs to one space and is visible only to the person it acts for. It is never shared, so the
            // shared audience is refused as well.
            migrationBuilder.IsolateBySpace("allow_all_windows", audience: true);
            migrationBuilder.IsolateToOnePerson("allow_all_windows");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "allow_all_windows");

            migrationBuilder.DropColumn(
                name: "decided_by_window_id",
                table: "tool_calls");
        }
    }
}
