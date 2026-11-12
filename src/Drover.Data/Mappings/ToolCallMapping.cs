namespace Drover.Data.Mappings;

public sealed class ToolCallMapping : IEntityTypeConfiguration<ToolCall>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<ToolCall> builder)
    {
        builder.ToTable("tool_calls");
        builder.HasKey(call => call.Id);
        builder.Property(call => call.Id).ValueGeneratedNever();
        builder.Property(call => call.ToolName).HasMaxLength(200);
        builder.Property(call => call.Arguments).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(call => call.Result).HasColumnType("jsonb");
        builder.HasEnumColumn(call => call.Status);

        // The approval inbox.
        builder.HasIndex(call => new { call.SpaceId, call.Status })
            .HasFilter("status = 'pending_approval'");

        // result_file_id is a plain column on purpose: it would need a second audience in the key.
        builder.WithinSpace<ToolCall, Run>(nameof(ToolCall.RunId));
        builder.WithinSpace<ToolCall, ChatThread>(nameof(ToolCall.ThreadId), audience: true);
        builder.WithinSpace<ToolCall, Tool>(nameof(ToolCall.ToolId));
        builder.WithinSpace<ToolCall, Run>(nameof(ToolCall.ChildRunId));
        builder.WithinSpace<ToolCall, Sandbox>(nameof(ToolCall.SandboxId));
        builder.ReferencesUser(nameof(ToolCall.DecidedByUserId));
    }

    #endregion
}
