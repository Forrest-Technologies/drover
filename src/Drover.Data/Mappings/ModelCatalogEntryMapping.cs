namespace Drover.Data.Mappings;

public sealed class ModelCatalogEntryMapping : IEntityTypeConfiguration<ModelCatalogEntry>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<ModelCatalogEntry> builder)
    {
        builder.ToTable("model_catalog");
        builder.HasKey(entry => new { entry.SpaceId, entry.ModelConnectionId, entry.Model });
        builder.Property(entry => entry.Model).HasMaxLength(200);
        builder.Property(entry => entry.DisplayName).HasMaxLength(200);
        builder.Property(entry => entry.PrivacyMode).HasMaxLength(64);
        builder.Property(entry => entry.Traits).HasDefaultValueSql("'{}'::text[]");
        builder.Property(entry => entry.Capabilities).HasJsonRecord();
        builder.Property(entry => entry.Pricing).HasJsonRecord();
        builder.HasEnumColumn(entry => entry.State);
        builder.WithinSpace<ModelCatalogEntry, ModelConnection>(nameof(ModelCatalogEntry.ModelConnectionId));
    }

    #endregion
}
