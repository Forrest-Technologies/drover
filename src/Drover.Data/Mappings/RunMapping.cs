namespace Drover.Data.Mappings;

public sealed class RunMapping : IEntityTypeConfiguration<Run>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<Run> builder)
    {
        builder.ToTable("runs");
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).ValueGeneratedNever();
        builder.Property(run => run.StatusReason).HasMaxLength(64);
        builder.Property(run => run.LeaseOwner).HasMaxLength(200);
        builder.Property(run => run.Error).HasJsonRecord(defaultSql: null);
        builder.Property(run => run.Usage).HasJsonRecord();
        builder.Property(run => run.SubtreeUsage).HasJsonRecord();
        builder.HasEnumColumn(run => run.Trigger);
        builder.HasEnumColumn(run => run.Status);

        // Job claiming and lease recovery look only at runs that are not finished.
        builder.HasIndex(run => new { run.Status, run.LeaseExpiresAt })
            .HasFilter("status IN ('queued', 'running', 'waiting')");

        // Subtree cancel, cost roll-up, and the activity tree.
        builder.HasIndex(run => new { run.SpaceId, run.RootRunId });

        // thread_id, trigger_message_id and parent_tool_call_id are plain columns on purpose: a run has no audience,
        // and a foreign key to a row that has one must carry it (see the audience rule in docs/architecture/02).
        builder.WithinSpace<Run, Bot>(nameof(Run.BotId));
        builder.WithinSpace<Run, Run>(nameof(Run.ParentRunId));
        builder.WithinSpace<Run, Run>(nameof(Run.RootRunId));
        builder.ReferencesUser(nameof(Run.PrincipalUserId));
    }

    #endregion
}
