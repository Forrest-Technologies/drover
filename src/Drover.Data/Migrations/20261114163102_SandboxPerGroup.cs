using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <summary>
    /// One live sandbox per owner bot, the sharing group's id: a partial unique index, so two first uses at once
    /// create one sandbox and the second reads the first's row. Written as SQL beside the foreign key's own index,
    /// because a model index on the same columns would replace that one, and a partial index cannot serve the key.
    /// </summary>
    public partial class SandboxPerGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"CREATE UNIQUE INDEX {SandboxRepository.OneLivePerOwner} ON sandboxes (space_id, owner_bot_id) WHERE state IN ('creating', 'running', 'paused');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DROP INDEX {SandboxRepository.OneLivePerOwner};");
        }
    }
}
