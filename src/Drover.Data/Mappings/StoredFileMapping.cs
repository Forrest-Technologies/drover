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
        builder.Property(file => file.Preview).HasMaxLength(1000);
        builder.Property(file => file.ReadingNote).HasMaxLength(300);
        builder.Property(file => file.CreatedAt).HasDefaultValueSql("now()");
        builder.ToTable(table => table.HasCheckConstraint("ck_files_size", "size >= 0"));
        builder.ToTable(table => table.HasCheckConstraint("ck_files_picture_size", "(width IS NULL) = (height IS NULL) AND (width IS NULL OR (width > 0 AND height > 0))"));
        builder.ToTable(table => table.HasCheckConstraint("ck_files_owner", "thread_id IS NULL OR project_id IS NULL"));
        builder.ToTable(table => table.HasCheckConstraint("ck_files_project_audience", "project_id IS NULL OR (message_id IS NULL AND audience_user_id = sys.everyone())"));

        // The thread's key carries the audience: a file is seen by exactly the people who see its conversation.
        builder.WithinSpace<StoredFile, ChatThread>(nameof(StoredFile.ThreadId), audience: true);
        builder.WithinSpace<StoredFile, Project>(nameof(StoredFile.ProjectId));
        builder.HasIndex(file => new { file.SpaceId, file.MessageId });
        builder.HasIndex(file => new { file.SpaceId, file.ProjectId, file.CreatedAt, file.Id });
        builder.WithinSpace<StoredFile, Bot>(nameof(StoredFile.CreatedByBotId));
        builder.ReferencesUser(nameof(StoredFile.CreatedByUserId));
    }

    #endregion
}
