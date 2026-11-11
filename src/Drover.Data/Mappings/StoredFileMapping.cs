namespace Drover.Data.Mappings;

public sealed class StoredFileMapping : IEntityTypeConfiguration<StoredFile>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        builder.ToTable("files");
        builder.HasKey(file => file.Id);
        builder.Property(file => file.Id).ValueGeneratedNever();
        builder.Property(file => file.StorageKey).HasMaxLength(500);
        builder.Property(file => file.Name).HasMaxLength(500);
        builder.Property(file => file.ContentType).HasMaxLength(200);
        builder.Property(file => file.Sha256).HasMaxLength(64);
        builder.WithinSpace<StoredFile, Bot>(nameof(StoredFile.CreatedByBotId));
        builder.ReferencesUser(nameof(StoredFile.CreatedByUserId));
    }

    #endregion
}
