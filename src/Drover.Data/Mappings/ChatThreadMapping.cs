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

        // A thread is private to its owner or shared with the whole space, and nothing between. A child table's
        // foreign key carries the audience, so it can never be wider than this row.
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_threads_audience",
            "audience_user_id IN (sys.everyone(), owner_user_id)"));
        builder.HasAlternateKey(thread => new { thread.SpaceId, thread.Id, thread.AudienceUserId });
        builder.WithinSpace<ChatThread, Bot>(nameof(ChatThread.BotId));
        builder.ReferencesUser(nameof(ChatThread.OwnerUserId));
    }

    #endregion
}
