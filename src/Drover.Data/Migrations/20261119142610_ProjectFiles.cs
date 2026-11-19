namespace Drover.Data.Migrations;

public partial class ProjectFiles : Migration
{
    #region Overrides

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>("project_id", "files", "uuid", nullable: true);
        migrationBuilder.CreateIndex("ix_files_space_id_project_id", "files", ["space_id", "project_id"]);
        migrationBuilder.AddForeignKey(
            "fk_files_projects_space_id_project_id",
            "files",
            ["space_id", "project_id"],
            "projects",
            principalColumns: ["space_id", "id"],
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddCheckConstraint(
            "ck_files_project_scope",
            "files",
            """
            project_id IS NULL OR (thread_id IS NULL AND message_id IS NULL AND created_by_bot_id IS NULL
                AND audience_user_id = '00000000-0000-0000-0000-000000000000')
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("ck_files_project_scope", "files");
        migrationBuilder.DropForeignKey("fk_files_projects_space_id_project_id", "files");
        migrationBuilder.DropIndex("ix_files_space_id_project_id", "files");
        migrationBuilder.DropColumn("project_id", "files");
    }

    #endregion
}
