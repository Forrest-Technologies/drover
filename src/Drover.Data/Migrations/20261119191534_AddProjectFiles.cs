using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "project_id",
                table: "files",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_files_space_id_project_id_created_at_id",
                table: "files",
                columns: new[] { "space_id", "project_id", "created_at", "id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_files_owner",
                table: "files",
                sql: "thread_id IS NULL OR project_id IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_files_project_audience",
                table: "files",
                sql: "project_id IS NULL OR (message_id IS NULL AND audience_user_id = sys.everyone())");

            migrationBuilder.AddForeignKey(
                name: "fk_files_projects_space_id_project_id",
                table: "files",
                columns: new[] { "space_id", "project_id" },
                principalTable: "projects",
                principalColumns: new[] { "space_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_files_projects_space_id_project_id",
                table: "files");

            migrationBuilder.DropIndex(
                name: "ix_files_space_id_project_id_created_at_id",
                table: "files");

            migrationBuilder.DropCheckConstraint(
                name: "ck_files_owner",
                table: "files");

            migrationBuilder.DropCheckConstraint(
                name: "ck_files_project_audience",
                table: "files");

            migrationBuilder.DropColumn(
                name: "project_id",
                table: "files");
        }
    }
}
