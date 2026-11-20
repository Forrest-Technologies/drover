namespace Drover.Data.Mappings;

public sealed class PendingFileDeletionMapping : IEntityTypeConfiguration<PendingFileDeletion>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<PendingFileDeletion> builder)
    {
        builder.ToTable("pending_file_deletions");
        builder.HasKey(pending => pending.Id);
        builder.Property(pending => pending.Id).ValueGeneratedNever();
        builder.Property(pending => pending.StorageKey).HasMaxLength(500);
        builder.Property(pending => pending.CreatedAt).HasDefaultValueSql("now()");
        builder.HasIndex(pending => new { pending.SpaceId, pending.ProjectId });
        builder.ToTable(table => table.HasCheckConstraint("ck_pending_file_deletions_size", "size >= 0"));
    }

    #endregion
}
