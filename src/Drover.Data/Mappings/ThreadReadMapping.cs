namespace Drover.Data.Mappings;

public sealed class ThreadReadMapping : IEntityTypeConfiguration<ThreadRead>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<ThreadRead> builder)
    {
        builder.ToTable("thread_reads");
        builder.HasKey(read => new { read.SpaceId, read.ThreadId, read.AudienceUserId });
        builder.ToTable(table => table.HasCheckConstraint("ck_thread_reads_position", "seen_through_position >= 0"));
    }

    #endregion
}
