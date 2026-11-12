namespace Drover.Data.Mappings;

/// <summary>
/// The migration creates this table range-partitioned by month on <c>created_at</c>, which EF Core cannot express.
/// PostgreSQL requires the partition column in the primary key.
/// </summary>
public sealed class RunEventMapping : IEntityTypeConfiguration<RunEvent>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<RunEvent> builder)
    {
        builder.ToTable("run_events");
        builder.HasKey(runEvent => new { runEvent.SpaceId, runEvent.RootRunId, runEvent.Seq, runEvent.CreatedAt });
        builder.Property(runEvent => runEvent.Seq).ValueGeneratedNever();
        builder.Property(runEvent => runEvent.CreatedAt).ValueGeneratedNever();
        builder.Property(runEvent => runEvent.Type).HasMaxLength(64);
        builder.Property(runEvent => runEvent.Data).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.ToTable(table => table.HasCheckConstraint("ck_run_events_seq", "seq > 0"));
        builder.WithinSpace<RunEvent, Run>(nameof(RunEvent.RootRunId));
        builder.WithinSpace<RunEvent, Run>(nameof(RunEvent.RunId));
        builder.WithinSpace<RunEvent, ChatThread>(nameof(RunEvent.ThreadId), audience: true);
    }

    #endregion
}
