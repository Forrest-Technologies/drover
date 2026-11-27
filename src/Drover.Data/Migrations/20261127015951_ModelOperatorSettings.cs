using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations;

/// <inheritdoc />
public partial class ModelOperatorSettings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "operator_capabilities",
            table: "model_catalog",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "operator_pricing",
            table: "model_catalog",
            type: "jsonb",
            nullable: true);

        // A model an admin marked as reading pictures keeps the mark: it moves into the operator column, and the
        // old flag leaves the discovered capabilities, which a refresh rewrites anyway. The policies apply to the
        // table's owner too, and a migration runs with no tenant, so it would see no row: the owner reads past them
        // for this one statement, and they are forced again before the migration's transaction commits. Nothing
        // here names a space or reaches a row the statement does not mean to change.
        migrationBuilder.Sql(
            """
            ALTER TABLE model_catalog NO FORCE ROW LEVEL SECURITY;

            UPDATE model_catalog
            SET operator_capabilities = jsonb_build_object('vision', true),
                capabilities = capabilities - 'vision_set_by_operator'
            WHERE capabilities ->> 'vision_set_by_operator' = 'true';

            ALTER TABLE model_catalog FORCE ROW LEVEL SECURITY;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE model_catalog NO FORCE ROW LEVEL SECURITY;

            UPDATE model_catalog
            SET capabilities = capabilities || '{"vision": true, "vision_set_by_operator": true}'::jsonb
            WHERE operator_capabilities ->> 'vision' = 'true';

            ALTER TABLE model_catalog FORCE ROW LEVEL SECURITY;
            """);

        migrationBuilder.DropColumn(
            name: "operator_capabilities",
            table: "model_catalog");

        migrationBuilder.DropColumn(
            name: "operator_pricing",
            table: "model_catalog");
    }
}
