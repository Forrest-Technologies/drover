namespace Drover.Data.Mappings;

public sealed class ProjectMapping : IEntityTypeConfiguration<Project>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.HasKey(project => project.Id);
        builder.Property(project => project.Id).ValueGeneratedNever();
        builder.Property(project => project.Name).HasMaxLength(Project.MaxNameLength);
        builder.Property(project => project.Instructions).HasMaxLength(Project.MaxInstructionsLength);
        builder.Property(project => project.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(project => project.UpdatedAt).HasDefaultValueSql("now()");
        builder.HasIndex(project => new { project.SpaceId, project.UpdatedAt, project.Id });
        builder.ToTable(table => table.HasCheckConstraint("ck_projects_name", "char_length(btrim(name)) > 0"));
    }

    #endregion
}
