using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class AttachedFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "stored_file_bytes",
                table: "spaces",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                table: "files",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<int>(
                name: "height",
                table: "files",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "message_id",
                table: "files",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "preview",
                table: "files",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "thread_id",
                table: "files",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "width",
                table: "files",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_spaces_stored_file_bytes",
                table: "spaces",
                sql: "stored_file_bytes >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_files_space_id_message_id",
                table: "files",
                columns: new[] { "space_id", "message_id" });

            migrationBuilder.CreateIndex(
                name: "ix_files_space_id_thread_id_audience_user_id",
                table: "files",
                columns: new[] { "space_id", "thread_id", "audience_user_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_files_picture_size",
                table: "files",
                sql: "(width IS NULL) = (height IS NULL) AND (width IS NULL OR (width > 0 AND height > 0))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_files_size",
                table: "files",
                sql: "size >= 0");

            // Sharing a thread carries its new audience to its files, as to its messages.
            migrationBuilder.AddForeignKey(
                name: "fk_files_threads_space_id_thread_id_audience_user_id",
                table: "files",
                columns: new[] { "space_id", "thread_id", "audience_user_id" },
                principalTable: "threads",
                principalColumns: new[] { "space_id", "id", "audience_user_id" },
                onUpdate: ReferentialAction.Cascade,
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_files_threads_space_id_thread_id_audience_user_id",
                table: "files");

            migrationBuilder.DropCheckConstraint(
                name: "ck_spaces_stored_file_bytes",
                table: "spaces");

            migrationBuilder.DropIndex(
                name: "ix_files_space_id_message_id",
                table: "files");

            migrationBuilder.DropIndex(
                name: "ix_files_space_id_thread_id_audience_user_id",
                table: "files");

            migrationBuilder.DropCheckConstraint(
                name: "ck_files_picture_size",
                table: "files");

            migrationBuilder.DropCheckConstraint(
                name: "ck_files_size",
                table: "files");

            migrationBuilder.DropColumn(
                name: "stored_file_bytes",
                table: "spaces");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "files");

            migrationBuilder.DropColumn(
                name: "height",
                table: "files");

            migrationBuilder.DropColumn(
                name: "message_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "preview",
                table: "files");

            migrationBuilder.DropColumn(
                name: "thread_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "width",
                table: "files");
        }
    }
}
