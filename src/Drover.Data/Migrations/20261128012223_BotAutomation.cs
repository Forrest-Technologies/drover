using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations;

/// <inheritdoc />
public partial class _20261128012223_BotAutomation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "managing_bot_id",
            table: "bot_rules",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_bot_rules_space_id_managing_bot_id",
            table: "bot_rules",
            columns: new[] { "space_id", "managing_bot_id" });

        migrationBuilder.AddForeignKey(
            name: "fk_bot_rules_bots_space_id_managing_bot_id",
            table: "bot_rules",
            columns: new[] { "space_id", "managing_bot_id" },
            principalTable: "bots",
            principalColumns: new[] { "space_id", "id" },
            onDelete: ReferentialAction.Restrict);

        // Keep these definitions with the migration so an existing space can use its new grants on the first run.
        // The catalog later refreshes these rows normally. Preserve every existing grant and known explicit removal.
        migrationBuilder.Sql(
            """
            ALTER TABLE bots NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE tools NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE tool_grants NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE audit_log NO FORCE ROW LEVEL SECURITY;

            CREATE TEMP TABLE bot_automation_defaults ON COMMIT DROP AS
            SELECT *
            FROM jsonb_to_recordset($tools$
            [
                {"name":"routine.list","title":"List routines","description":"Lists scheduled prompts for the person you are working for. Leave bot_id out for your own routines. The result also lists bots beneath the selected bot.","input_schema":{"type":"object","properties":{"bot_id":{"type":"string","description":"The id of a bot beneath you, as returned by the list tool."}},"required":[]},"definition_hash":"F40E5D339FD3056086868ED4601942D57E4B99DF16A0BA2690F1B2D128CD6067","sensitivity":"read","effect":"allow"},
                {"name":"routine.manage_own","title":"Manage your routines","description":"Adds, replaces, or removes one of your own scheduled prompts for the person you are working for. Use routine_list to find existing routines before changing them.","input_schema":{"type":"object","properties":{"operation":{"type":"string","enum":["add","change","remove"]},"id":{"type":"string","description":"The id returned by the list tool. Required to change or remove."},"routine":{"type":"object","description":"The full routine to add or replace. Omit to remove.","properties":{"cron":{"type":"string","description":"Five-field cron, for example 0 6 * * * for 6 AM daily."},"time_zone":{"type":"string","description":"An IANA time zone, for example America/New_York."},"prompt":{"type":"string","description":"The full prompt sent to the bot each time."},"thread_mode":{"type":"string","enum":["new_thread","same_thread"]},"enabled":{"type":"boolean"}},"required":["cron","time_zone","prompt","thread_mode","enabled"]}},"required":["operation"]},"definition_hash":"14A2F0F33AD0ECEF437B96F3E0D24F2EFDCB036D7240495BC47241F6CBAC95CB","sensitivity":"write","effect":"allow"},
                {"name":"routine.manage_descendant","title":"Manage a descendant's routines","description":"Adds, replaces, or removes a scheduled prompt for a bot beneath you. The routine runs for the person you are working for. Use routine_list first to find the bot and routine.","input_schema":{"type":"object","properties":{"bot_id":{"type":"string","description":"The id of a bot beneath you, as returned by the list tool."},"operation":{"type":"string","enum":["add","change","remove"]},"id":{"type":"string","description":"The id returned by the list tool. Required to change or remove."},"routine":{"type":"object","description":"The full routine to add or replace. Omit to remove.","properties":{"cron":{"type":"string","description":"Five-field cron, for example 0 6 * * * for 6 AM daily."},"time_zone":{"type":"string","description":"An IANA time zone, for example America/New_York."},"prompt":{"type":"string","description":"The full prompt sent to the bot each time."},"thread_mode":{"type":"string","enum":["new_thread","same_thread"]},"enabled":{"type":"boolean"}},"required":["cron","time_zone","prompt","thread_mode","enabled"]}},"required":["bot_id","operation"]},"definition_hash":"1BF0C732857014D009F7B5A166ED8327D9FBEFE56C56C2C2C7F48CF62E17F2BA","sensitivity":"write","effect":"ask"},
                {"name":"rule.list","title":"List rules","description":"Lists rules a bot follows, including inherited rules, and says which you may change. Leave bot_id out to list your own. The result lists the bots beneath the selected bot.","input_schema":{"type":"object","properties":{"bot_id":{"type":"string","description":"The id of a bot beneath you, as returned by the list tool."}},"required":[]},"definition_hash":"17941A9A984B9268C0A2E2B2598822B5D5E7C634D976693DF1B3CDBC29153D1B","sensitivity":"read","effect":"allow"},
                {"name":"rule.manage_own","title":"Manage your rules","description":"Adds, replaces, or removes rules you write for yourself. Rules also apply to your descendants. Rules can only restrict behavior.","input_schema":{"type":"object","properties":{"operation":{"type":"string","enum":["add","change","remove"]},"id":{"type":"string","description":"The id returned by the list tool. Required to change or remove."},"rule":{"type":"object","description":"The full rule to add or replace. Omit to remove.","properties":{"name":{"type":"string"},"enabled":{"type":"boolean"},"checkpoint":{"type":"string","enum":["before_tool","after_tool","before_reply","message_arrives"]},"effect":{"type":"string","enum":["redact","ask","block","stop"]},"conditions":{"type":"array","description":"Every condition must match. An empty list means this rule always applies at the checkpoint.","items":{"type":"object","properties":{"kind":{"type":"string","enum":["tool_is","tool_kind","text_contains","text_matches","text_holds","not_in_list","time_is","run_exceeds","judged"]},"tools":{"type":"array","items":{"type":"string"}},"tool_kinds":{"type":"array","items":{"type":"string","enum":["read","write","critical"]}},"argument":{"type":"string"},"entries":{"type":"array","items":{"type":"string"}},"pattern":{"type":"string"},"builtin":{"type":"string","enum":["email_address","card_number","key"]},"from":{"type":"string"},"to":{"type":"string"},"weekdays":{"type":"array","items":{"type":"string"}},"time_zone":{"type":"string"},"counter":{"type":"string","enum":["tool_calls","tokens"]},"limit":{"type":"integer"},"statement":{"type":"string"},"min_confidence":{"type":"number"}},"required":["kind"]}}},"required":["name","enabled","checkpoint","effect","conditions"]}},"required":["operation"]},"definition_hash":"BD1E5ED4FA45627F3A465D4530EA410A1061A919CFC4183044AF28355897A11C","sensitivity":"write","effect":"allow"},
                {"name":"rule.add_for_descendant","title":"Add a rule for a descendant","description":"Adds a rule that restricts a bot beneath you and its descendants. Use rule_list to find the bot and its current rules.","input_schema":{"type":"object","properties":{"bot_id":{"type":"string","description":"The id of a bot beneath you, as returned by the list tool."},"operation":{"type":"string","enum":["add"]},"id":{"type":"string","description":"The id returned by the list tool. Required to change or remove."},"rule":{"type":"object","description":"The full rule to add or replace. Omit to remove.","properties":{"name":{"type":"string"},"enabled":{"type":"boolean"},"checkpoint":{"type":"string","enum":["before_tool","after_tool","before_reply","message_arrives"]},"effect":{"type":"string","enum":["redact","ask","block","stop"]},"conditions":{"type":"array","description":"Every condition must match. An empty list means this rule always applies at the checkpoint.","items":{"type":"object","properties":{"kind":{"type":"string","enum":["tool_is","tool_kind","text_contains","text_matches","text_holds","not_in_list","time_is","run_exceeds","judged"]},"tools":{"type":"array","items":{"type":"string"}},"tool_kinds":{"type":"array","items":{"type":"string","enum":["read","write","critical"]}},"argument":{"type":"string"},"entries":{"type":"array","items":{"type":"string"}},"pattern":{"type":"string"},"builtin":{"type":"string","enum":["email_address","card_number","key"]},"from":{"type":"string"},"to":{"type":"string"},"weekdays":{"type":"array","items":{"type":"string"}},"time_zone":{"type":"string"},"counter":{"type":"string","enum":["tool_calls","tokens"]},"limit":{"type":"integer"},"statement":{"type":"string"},"min_confidence":{"type":"number"}},"required":["kind"]}}},"required":["name","enabled","checkpoint","effect","conditions"]}},"required":["bot_id","operation"]},"definition_hash":"3B5A4F70A4CF1BC7AD65C4D8488E66C340A80934E9D6D0A1D1FFFDE4B518422C","sensitivity":"write","effect":"allow"},
                {"name":"rule.change_for_descendant","title":"Change a descendant's rules","description":"Replaces or removes bot-authored rules on a bot beneath you. Use rule_list to find a rule you may change.","input_schema":{"type":"object","properties":{"bot_id":{"type":"string","description":"The id of a bot beneath you, as returned by the list tool."},"operation":{"type":"string","enum":["change","remove"]},"id":{"type":"string","description":"The id returned by the list tool. Required to change or remove."},"rule":{"type":"object","description":"The full rule to add or replace. Omit to remove.","properties":{"name":{"type":"string"},"enabled":{"type":"boolean"},"checkpoint":{"type":"string","enum":["before_tool","after_tool","before_reply","message_arrives"]},"effect":{"type":"string","enum":["redact","ask","block","stop"]},"conditions":{"type":"array","description":"Every condition must match. An empty list means this rule always applies at the checkpoint.","items":{"type":"object","properties":{"kind":{"type":"string","enum":["tool_is","tool_kind","text_contains","text_matches","text_holds","not_in_list","time_is","run_exceeds","judged"]},"tools":{"type":"array","items":{"type":"string"}},"tool_kinds":{"type":"array","items":{"type":"string","enum":["read","write","critical"]}},"argument":{"type":"string"},"entries":{"type":"array","items":{"type":"string"}},"pattern":{"type":"string"},"builtin":{"type":"string","enum":["email_address","card_number","key"]},"from":{"type":"string"},"to":{"type":"string"},"weekdays":{"type":"array","items":{"type":"string"}},"time_zone":{"type":"string"},"counter":{"type":"string","enum":["tool_calls","tokens"]},"limit":{"type":"integer"},"statement":{"type":"string"},"min_confidence":{"type":"number"}},"required":["kind"]}}},"required":["name","enabled","checkpoint","effect","conditions"]}},"required":["bot_id","operation"]},"definition_hash":"F8E239507AC34F6A2E49D3EFD765AD68B56FD1C7798887B47A579DE70378162F","sensitivity":"write","effect":"ask"},
                {"name":"memory.save","title":"Save a note","description":"Saves a short note about the person you work for, so you have it in every later conversation with them. One fact or preference to a note, in your own words. When a note you already have changes, change it with memory_change instead of saving another. Never save a password, key, or other secret.","input_schema":{"type":"object","properties":{"text":{"type":"string","description":"The note, in your own words: one fact or preference about the person, in a sentence or two."}},"required":["text"]},"definition_hash":"C768A40C64FF6FD4EA7797AF28060C63E9F9365A2E49DA39A4D27E0AE727B202","sensitivity":"write","effect":"allow"},
                {"name":"memory.change","title":"Change a note","description":"Rewrites one of your notes about the person you work for, when what it says has changed or to merge two notes into one. The note is replaced by the new text.","input_schema":{"type":"object","properties":{"note_id":{"type":"string","format":"uuid","description":"The id of one of your notes, as memory_list gives it."},"text":{"type":"string","description":"The note, in your own words: one fact or preference about the person, in a sentence or two."}},"required":["note_id","text"]},"definition_hash":"FAE858AE066387E92912FA5EA7A45C0C816642A4DFE7D404BF18A0DF0409F7A2","sensitivity":"write","effect":"allow"},
                {"name":"memory.forget","title":"Remove a note","description":"Removes one of your notes about the person you work for, when it no longer holds or the person asks you to forget it.","input_schema":{"type":"object","properties":{"note_id":{"type":"string","format":"uuid","description":"The id of one of your notes, as memory_list gives it."}},"required":["note_id"]},"definition_hash":"BC8B8D4F0A4ADBDA665C6B5399C86327A0C9B9977918672B2D2AC7DA2AC6DCBD","sensitivity":"write","effect":"allow"},
                {"name":"memory.list","title":"List notes","description":"Lists every note you keep about the person you work for, oldest first, with each one's id, and how many characters they use of what they may hold.","input_schema":{"type":"object","properties":{}},"definition_hash":"C4311EE86C9DFE09D395302BB186E7931B5B1434D4AB1C82E4F771C31FF1312F","sensitivity":"read","effect":"allow"}
            ]
            $tools$::jsonb) AS defaults(name text, title text, description text, input_schema jsonb,
                                        definition_hash text, sensitivity text, effect text);

            INSERT INTO tools (id, space_id, source_kind, name, title, description, input_schema,
                               definition_hash, sensitivity, state, first_seen_at, last_seen_at)
            SELECT gen_random_uuid(), spaces.space_id, 'builtin', defaults.name, defaults.title,
                   defaults.description, defaults.input_schema, defaults.definition_hash, defaults.sensitivity,
                   'available', now(), now()
            FROM (SELECT DISTINCT space_id FROM bots) AS spaces
            CROSS JOIN bot_automation_defaults AS defaults
            ON CONFLICT DO NOTHING;

            INSERT INTO tool_grants (space_id, bot_id, tool_id, effect, review_state)
            SELECT bots.space_id, bots.id, tools.id, defaults.effect, 'current'
            FROM bots
            JOIN tools ON tools.space_id = bots.space_id AND tools.source_kind = 'builtin'
            JOIN bot_automation_defaults AS defaults ON defaults.name = tools.name
            WHERE bots.status <> 'removed'
              AND NOT EXISTS (
                  SELECT 1 FROM audit_log AS entry
                  WHERE entry.space_id = bots.space_id AND entry.target_kind = 'bot'
                    AND entry.target_id = bots.id AND entry.action = 'grant.changed'
                    AND entry.data->>'tool' = tools.name)
            ON CONFLICT DO NOTHING;

            ALTER TABLE audit_log FORCE ROW LEVEL SECURITY;
            ALTER TABLE tool_grants FORCE ROW LEVEL SECURITY;
            ALTER TABLE tools FORCE ROW LEVEL SECURITY;
            ALTER TABLE bots FORCE ROW LEVEL SECURITY;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Catalog rows are retained for stored tool calls. The older server still understands memory grants.
        migrationBuilder.Sql(
            """
            ALTER TABLE tools NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE tool_grants NO FORCE ROW LEVEL SECURITY;
            DELETE FROM tool_grants AS grants USING tools
            WHERE grants.space_id = tools.space_id AND grants.tool_id = tools.id AND tools.source_kind = 'builtin'
              AND tools.name IN ('routine.list', 'routine.manage_own', 'routine.manage_descendant', 'rule.list',
                                 'rule.manage_own', 'rule.add_for_descendant', 'rule.change_for_descendant');
            ALTER TABLE tool_grants FORCE ROW LEVEL SECURITY;
            ALTER TABLE tools FORCE ROW LEVEL SECURITY;
            """);

        migrationBuilder.DropForeignKey(
            name: "fk_bot_rules_bots_space_id_managing_bot_id",
            table: "bot_rules");

        migrationBuilder.DropIndex(
            name: "ix_bot_rules_space_id_managing_bot_id",
            table: "bot_rules");

        migrationBuilder.DropColumn(
            name: "managing_bot_id",
            table: "bot_rules");
    }
}
