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

        builder.Property(note => note.Tainted).HasDefaultValue(false);

        // source_thread_id and source_message_id are plain columns on purpose, as tool_calls.result_file_id is. A key to
        // messages would have to carry the audience, and an audience key moves with its parent: sharing a conversation
        // would then share every note taken from it, and deleting the conversation would be blocked by its notes. A
        // note's audience is its person's alone and never moves. MemoryService names only the reply that made the call,
        // in a conversation private to that person.
        builder.ReferencesUser(nameof(MemoryNote.WrittenByUserId));
    }

    #endregion
}
