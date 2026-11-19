namespace Drover.Data.Mappings;

public sealed class ChatThreadMapping : IEntityTypeConfiguration<ChatThread>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<ChatThread> builder)
    {
        builder.ToTable("threads");
        builder.HasKey(thread => thread.Id);
        builder.Property(thread => thread.Id).ValueGeneratedNever();
        builder.Property(thread => thread.Title).HasMaxLength(500);
        builder.Property(thread => thread.LastActivityAt).HasDefaultValueSql("now()");
        builder.HasEnumColumn(thread => thread.Origin);

        // Listing a bot's threads reads this. Declared, because the filtered index below would otherwise stand in for it.
        builder.HasIndex(thread => new { thread.SpaceId, thread.BotId });

        // One main thread per person per bot. Two requests that race to open it are decided here: the loser re-reads.
        builder.HasIndex(thread => new { thread.SpaceId, thread.BotId, thread.OwnerUserId })
            .IsUnique()
            .HasFilter("is_main");

        // A thread is private to its owner or shared with the whole space, and nothing between. A child table's
        // foreign key carries the audience, so it can never be wider than this row.
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_threads_audience",
            "audience_user_id IN (sys.everyone(), owner_user_id)"));

        // A summary always says how far it reaches, and only a main thread has one.
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_threads_summary",
            "(summary IS NULL) = (summary_through_position IS NULL) AND (summary IS NULL OR is_main)"));
        builder.HasAlternateKey(thread => new { thread.SpaceId, thread.Id, thread.AudienceUserId });
        builder.WithinSpace<ChatThread, Bot>(nameof(ChatThread.BotId));
        builder.WithinSpace<ChatThread, Project>(nameof(ChatThread.ProjectId));
        builder.ReferencesUser(nameof(ChatThread.OwnerUserId));
    }

    #endregion
}
