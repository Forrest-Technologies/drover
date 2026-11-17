using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConversationSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "summary",
                table: "threads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "summary_through_position",
                table: "threads",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "summary_updated_at",
                table: "threads",
                type: "timestamp with time zone",
                nullable: true);

            // The application role updates threads by column. The server writes the summary; a person never does.
            migrationBuilder.Sql("GRANT UPDATE (summary, summary_through_position, summary_updated_at) ON threads TO drover_app;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_threads_summary",
                table: "threads",
                sql: "(summary IS NULL) = (summary_through_position IS NULL) AND (summary IS NULL OR is_main)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_threads_summary",
                table: "threads");

            migrationBuilder.DropColumn(
                name: "summary",
                table: "threads");

            migrationBuilder.DropColumn(
                name: "summary_through_position",
                table: "threads");

            migrationBuilder.DropColumn(
                name: "summary_updated_at",
                table: "threads");
        }
    }
}
