namespace Drover.Data.Mappings;

public sealed class MessageMapping : IEntityTypeConfiguration<Message>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.Parts).HasJsonRecord("'[]'::jsonb");
        builder.Property(message => message.Withheld).HasDefaultValue(false);
        builder.Property(message => message.CreatedAt).HasDefaultValueSql("now()");
        builder.HasEnumColumn(message => message.Role);
        builder.HasIndex(message => new { message.SpaceId, message.ThreadId, message.Position }).IsUnique();
        builder.WithinSpace<Message, ChatThread>(nameof(Message.ThreadId), audience: true);
        builder.WithinSpace<Message, Run>(nameof(Message.RunId));
        builder.WithinSpace<Message, Bot>(nameof(Message.AuthorBotId));
        builder.ReferencesUser(nameof(Message.AuthorUserId));
    }

    #endregion
}
