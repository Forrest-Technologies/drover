namespace Drover.Data.Mappings;

public sealed class SpaceMapping : IEntityTypeConfiguration<Space>
{
    #region Private Fields

    private static readonly JsonSerializerOptions settingsJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    #endregion

    #region Public Methods

    public void Configure(EntityTypeBuilder<Space> builder)
    {
        builder.ToTable("spaces");
        builder.HasKey(space => space.Id);
        builder.Property(space => space.Id).ValueGeneratedNever();
        builder.Property(space => space.Kind).HasConversion<LowercaseEnumConverter<SpaceKind>>().HasMaxLength(16);
        builder.Property(space => space.Name).HasMaxLength(200);
        builder.Property(space => space.Slug).HasMaxLength(100);
        builder.HasIndex(space => space.Slug).IsUnique();
        builder.Property(space => space.Settings)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .HasConversion(
                settings => JsonSerializer.Serialize(settings, settingsJson),
                json => JsonSerializer.Deserialize<SpaceSettings>(json, settingsJson) ?? new SpaceSettings());
        builder.ToTable(table => table.HasCheckConstraint("ck_spaces_kind", "kind IN ('personal', 'shared')"));
    }

    #endregion
}
