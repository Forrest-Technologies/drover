namespace Drover.Data.Mappings;

public sealed class PendingUploadDeletionMapping : IEntityTypeConfiguration<PendingUploadDeletion>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<PendingUploadDeletion> builder)
    {
        builder.ToTable("pending_upload_deletions");
        builder.HasKey(pending => pending.Id);
        builder.Property(pending => pending.Id).ValueGeneratedNever();
        builder.Property(pending => pending.CreatedAt).HasDefaultValueSql("now()");
        builder.HasIndex(pending => new { pending.CleanupClaimUntil, pending.Id });
    }

    #endregion
}
