using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class PushNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bot_push_mutes",
                columns: table => new
                {
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audience_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    muted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bot_push_mutes", x => new { x.space_id, x.bot_id, x.audience_user_id });
                    table.ForeignKey(
                        name: "fk_bot_push_mutes_bots_space_id_bot_id",
                        columns: x => new { x.space_id, x.bot_id },
                        principalTable: "bots",
                        principalColumns: new[] { "space_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "push_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    public_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    auth_secret = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_push_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_push_subscriptions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_push_subscriptions_user_id_endpoint",
                table: "push_subscriptions",
                columns: new[] { "user_id", "endpoint" },
                unique: true);

            // A device is its person's in every space. A mute is one person's choice about one bot: its audience is
            // that person, so nobody else sees it and nobody can mute a bot for someone else.
            migrationBuilder.IsolateByUser("push_subscriptions");
            migrationBuilder.IsolateBySpace("bot_push_mutes", audience: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bot_push_mutes");

            migrationBuilder.DropTable(
                name: "push_subscriptions");
        }
    }
}
