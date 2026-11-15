namespace Drover.Data.Mappings;

public sealed class MemoryNoteMapping : IEntityTypeConfiguration<MemoryNote>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<MemoryNote> builder)
    {
        builder.ToTable("memories");
        builder.HasKey(note => note.Id);
        builder.Property(note => note.Id).ValueGeneratedNever();
        builder.Property(note => note.Text).HasMaxLength(DeploymentPolicy.SelfHostedMaxMemoryNoteCharacters);
        builder.Property(note => note.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(note => note.UpdatedAt).HasDefaultValueSql("now()");

        // A run reads every note its bot keeps about its person, and a save counts them first.
        builder.HasIndex(note => new { note.SpaceId, note.BotId, note.AudienceUserId });

        // A note is written by the bot or by a person, never both and never by nobody. Only a note a bot wrote comes
        // from a conversation, and then it names both the conversation and the message.
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_memories_writer",
            "(written_by_bot_id IS NULL) <> (written_by_user_id IS NULL)"));
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_memories_source",
            "(source_thread_id IS NULL) = (source_message_id IS NULL) AND (written_by_bot_id IS NOT NULL OR source_message_id IS NULL)"));
        builder.ToTable(table => table.HasCheckConstraint("ck_memories_text", "char_length(btrim(text)) > 0"));

        builder.WithinSpace<MemoryNote, Bot>(nameof(MemoryNote.BotId));
        builder.WithinSpace<MemoryNote, Bot>(nameof(MemoryNote.WrittenByBotId));

        // The message a note came from, in the conversation it names, with the note's own audience: one key, so the
        // message must be in that conversation and a note can never be read by more people, or other people, than the
        // conversation it was taken from. The migration adds ON UPDATE CASCADE, so sharing the conversation shares it.
        builder.HasOne<Message>()
            .WithMany()
            .HasForeignKey(
                nameof(ISpaceOwned.SpaceId),
                nameof(MemoryNote.SourceThreadId),
                nameof(MemoryNote.SourceMessageId),
                nameof(IAudienceScoped.AudienceUserId))
            .HasPrincipalKey(
                nameof(ISpaceOwned.SpaceId),
                nameof(Message.ThreadId),
                nameof(Message.Id),
                nameof(IAudienceScoped.AudienceUserId))
            .OnDelete(DeleteBehavior.Restrict);
        builder.ReferencesUser(nameof(MemoryNote.WrittenByUserId));
    }

    #endregion
}
