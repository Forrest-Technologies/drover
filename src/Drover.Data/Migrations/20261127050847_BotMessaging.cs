using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations;

/// <inheritdoc />
public partial class _20261127050847_BotMessaging : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "is_bot_request",
            table: "runs",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "accepts_bot_requests",
            table: "bots",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        // The migration owner seeds one catalog row per space and gives existing bots the same grant as new ones.
        // Forced policies are restored before commit; the catalog and grant always join on space_id.
        migrationBuilder.Sql(
            """
            ALTER TABLE bots NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE tools NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE tool_grants NO FORCE ROW LEVEL SECURITY;

            INSERT INTO tools (id, space_id, source_kind, name, title, description, input_schema,
                               definition_hash, sensitivity, state, first_seen_at, last_seen_at)
            SELECT gen_random_uuid(), space_id, 'builtin', 'message_bot', 'Message a bot',
                   'Sends a request to another bot in this space and returns its reply. Use it to ask questions, review drafts, or get advice. The recipient sees the request and uses its own instructions. It can reply or ask another bot for advice.',
                   $schema${
              "type": "object",
              "properties": {
                "bot_id": { "type": "string", "format": "uuid", "description": "The id of a bot listed in this tool's description." },
                "task": { "type": "string", "description": "The task, in full: the bot sees nothing else of this conversation." }
              },
              "required": ["bot_id", "task"]
            }$schema$::jsonb,
                   'E62D1220A242451BD22F2E7965A517DF466FD1D0AA88BB232EE3700ADB87BF76', 'write', 'available', now(), now()
            FROM (SELECT DISTINCT space_id FROM bots) AS spaces_with_bots
            ON CONFLICT DO NOTHING;

            INSERT INTO tool_grants (space_id, bot_id, tool_id, effect, review_state)
            SELECT bots.space_id, bots.id, tools.id, 'allow', 'current'
            FROM bots
            JOIN tools ON tools.space_id = bots.space_id AND tools.source_kind = 'builtin' AND tools.name = 'message_bot'
            ON CONFLICT DO NOTHING;

            ALTER TABLE tool_grants FORCE ROW LEVEL SECURITY;
            ALTER TABLE tools FORCE ROW LEVEL SECURITY;
            ALTER TABLE bots FORCE ROW LEVEL SECURITY;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep catalog rows so stored tool calls can still refer to them after a rollback.
        migrationBuilder.Sql(
            """
            ALTER TABLE tools NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE tool_grants NO FORCE ROW LEVEL SECURITY;
            DELETE FROM tool_grants AS grants USING tools
            WHERE grants.space_id = tools.space_id AND grants.tool_id = tools.id
              AND tools.source_kind = 'builtin' AND tools.name = 'message_bot';
            ALTER TABLE tool_grants FORCE ROW LEVEL SECURITY;
            ALTER TABLE tools FORCE ROW LEVEL SECURITY;
            """);

        migrationBuilder.DropColumn(
            name: "is_bot_request",
            table: "runs");

        migrationBuilder.DropColumn(
            name: "accepts_bot_requests",
            table: "bots");
    }
}
