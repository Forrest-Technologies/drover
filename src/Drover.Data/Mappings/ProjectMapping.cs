namespace Drover.Data.Mappings;

public sealed class ProjectMapping : IEntityTypeConfiguration<Project>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects", table => table.HasCheckConstraint("ck_projects_name", "char_length(btrim(name)) > 0"));
        builder.HasKey(project => project.Id);
        builder.Property(project => project.Id).ValueGeneratedNever();
        builder.Property(project => project.Name).HasMaxLength(Project.MaxNameLength);
        builder.Property(project => project.Instructions).HasMaxLength(Project.MaxInstructionsLength);
        builder.HasAlternateKey(project => new { project.SpaceId, project.Id });
    }

    #endregion
}
