using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserAppearance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_appearance",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    theme_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    mode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    accent = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: false,
                        defaultValue: "theme"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_appearance", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_user_appearance_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.IsolateByUser("user_appearance");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_appearance");
        }
    }
}
