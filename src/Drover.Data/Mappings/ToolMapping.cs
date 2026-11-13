namespace Drover.Data.Mappings;

public sealed class ToolMapping : IEntityTypeConfiguration<Tool>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<Tool> builder)
    {
        builder.ToTable("tools");
        builder.HasKey(tool => tool.Id);
        builder.Property(tool => tool.Id).ValueGeneratedNever();
        builder.Property(tool => tool.Name).HasMaxLength(200);
        builder.Property(tool => tool.Title).HasMaxLength(200);
        builder.Property(tool => tool.DefinitionHash).HasMaxLength(128);
        builder.Property(tool => tool.InputSchema).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(tool => tool.Annotations).HasColumnType("jsonb");
        builder.Property(tool => tool.ReviewedDefinition).HasColumnType("jsonb");
        builder.HasEnumColumn(tool => tool.SourceKind);
        builder.HasEnumColumn(tool => tool.Sensitivity);
        builder.HasEnumColumn(tool => tool.State);
        builder.WithinSpace<Tool, ToolConnection>(nameof(Tool.ToolConnectionId));

        // The catalog upsert key. A built-in tool has no connection, so nulls count as equal: one row per name.
        builder.HasIndex(tool => new { tool.SpaceId, tool.ToolConnectionId, tool.Name })
            .IsUnique()
            .AreNullsDistinct(false);
    }

    #endregion
}
