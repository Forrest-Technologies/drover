namespace Drover.Data.Mappings;

public sealed class AgentSessionMapping : IEntityTypeConfiguration<AgentSession>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<AgentSession> builder)
    {
        builder.ToTable("agent_sessions");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).ValueGeneratedNever();
        // json, not jsonb: the framework's polymorphic JSON needs its "$type" property kept first, and jsonb reorders
        // object keys. The blob is opaque, so nothing is lost by not indexing it.
        builder.Property(session => session.State).HasColumnType("json").HasDefaultValueSql("'{}'::json");
        builder.Property(session => session.FrameworkVersion).HasMaxLength(64);
        builder.Property(session => session.UpdatedAt).HasDefaultValueSql("now()");
        builder.WithinSpace<AgentSession, ChatThread>(nameof(AgentSession.ThreadId), audience: true);
        builder.WithinSpace<AgentSession, Bot>(nameof(AgentSession.BotId));
    }

    #endregion
}
