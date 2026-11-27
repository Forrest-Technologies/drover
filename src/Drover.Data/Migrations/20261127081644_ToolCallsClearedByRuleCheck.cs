using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations;

/// <inheritdoc />
public partial class ToolCallsClearedByRuleCheck : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "approval_request_id",
            table: "tool_calls",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "cleared_at",
            table: "tool_calls",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "model_call_id",
            table: "tool_calls",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "approval_request_id",
            table: "tool_calls");

        migrationBuilder.DropColumn(
            name: "cleared_at",
            table: "tool_calls");

        migrationBuilder.DropColumn(
            name: "model_call_id",
            table: "tool_calls");
    }
}
