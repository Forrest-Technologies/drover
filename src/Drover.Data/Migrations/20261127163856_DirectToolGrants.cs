using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations;

/// <inheritdoc />
public partial class _20261127163856_DirectToolGrants : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "is_direct",
            table: "tool_grants",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.Sql(
            """
            CREATE OR REPLACE FUNCTION effective_grants(target_bot_id uuid)
            RETURNS TABLE (tool_id uuid, effect text)
            LANGUAGE sql STABLE
            AS $function$
              WITH path AS (
                SELECT space_id, ancestor_bot_id, distance
                FROM bot_ancestors
                WHERE descendant_bot_id = target_bot_id
              ), grants AS (
                SELECT g.tool_id, a.distance, g.review_state,
                       CASE g.effect WHEN 'deny' THEN 0 WHEN 'ask' THEN 1 ELSE 2 END AS rank,
                       min(a.distance) FILTER (WHERE g.is_direct) OVER (PARTITION BY g.tool_id) AS direct_distance
                FROM path a
                JOIN tool_grants g ON g.space_id = a.space_id AND g.bot_id = a.ancestor_bot_id
              )
              SELECT g.tool_id,
                     (ARRAY['deny', 'ask', 'allow'])[
                       min(CASE WHEN g.review_state = 'needs_review' THEN LEAST(g.rank, 1) ELSE g.rank END) + 1]
              FROM grants g
              WHERE (g.direct_distance IS NULL OR g.distance <= g.direct_distance)
                AND EXISTS (SELECT 1 FROM path WHERE ancestor_bot_id = target_bot_id AND distance = 0)
              GROUP BY g.tool_id, g.direct_distance
              HAVING count(*) = coalesce(g.direct_distance + 1, (SELECT count(*) FROM path))
            $function$;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE OR REPLACE FUNCTION effective_grants(target_bot_id uuid)
            RETURNS TABLE (tool_id uuid, effect text)
            LANGUAGE sql STABLE
            AS $function$
              SELECT g.tool_id,
                     (ARRAY['deny', 'ask', 'allow'])[
                       min(CASE WHEN g.review_state = 'needs_review' THEN LEAST(ranked.rank, 1) ELSE ranked.rank END) + 1]
              FROM bot_ancestors a
              JOIN tool_grants g ON g.space_id = a.space_id AND g.bot_id = a.ancestor_bot_id
              CROSS JOIN LATERAL (SELECT CASE g.effect WHEN 'deny' THEN 0 WHEN 'ask' THEN 1 ELSE 2 END AS rank) ranked
              WHERE a.descendant_bot_id = target_bot_id
                AND EXISTS (
                  SELECT 1 FROM bot_ancestors own
                  WHERE own.descendant_bot_id = target_bot_id AND own.ancestor_bot_id = target_bot_id AND own.distance = 0)
              GROUP BY g.tool_id
              HAVING count(*) = (SELECT count(*) FROM bot_ancestors WHERE descendant_bot_id = target_bot_id)
            $function$;
            """);

        migrationBuilder.DropColumn(
            name: "is_direct",
            table: "tool_grants");
    }
}
