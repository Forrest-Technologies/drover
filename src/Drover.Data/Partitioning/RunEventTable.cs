namespace Drover.Data.Partitioning;

/// <summary>
/// <c>run_events</c> grows fastest, so it is range-partitioned by month on <c>created_at</c> from the first
/// migration: old months can be detached or dropped without a table rewrite. EF Core cannot express partitioning,
/// so the table is written out here and the model still maps it as an ordinary table.
/// </summary>
public static class RunEventTable
{
    #region Private Fields

    private const string TableSql =
        """
        CREATE TABLE run_events (
          space_id uuid NOT NULL,
          root_run_id uuid NOT NULL,
          seq bigint NOT NULL,
          created_at timestamp with time zone NOT NULL DEFAULT now(),
          run_id uuid NOT NULL,
          thread_id uuid NOT NULL,
          audience_user_id uuid NOT NULL,
          type character varying(64) NOT NULL,
          data jsonb NOT NULL DEFAULT '{}'::jsonb,
          CONSTRAINT pk_run_events PRIMARY KEY (space_id, root_run_id, seq, created_at),
          CONSTRAINT ck_run_events_seq CHECK (seq > 0),
          CONSTRAINT fk_run_events_runs_space_id_root_run_id
            FOREIGN KEY (space_id, root_run_id) REFERENCES runs (space_id, id) ON DELETE RESTRICT,
          CONSTRAINT fk_run_events_runs_space_id_run_id
            FOREIGN KEY (space_id, run_id) REFERENCES runs (space_id, id) ON DELETE RESTRICT,
          CONSTRAINT fk_run_events_threads_space_id_thread_id_audience_user_id
            FOREIGN KEY (space_id, thread_id, audience_user_id)
            REFERENCES threads (space_id, id, audience_user_id) ON UPDATE CASCADE ON DELETE RESTRICT
        ) PARTITION BY RANGE (created_at);
        """;

    #endregion

    #region Public Methods

    /// <summary>
    /// Creates the table with one partition for each of <paramref name="months"/> months from <paramref name="first"/>,
    /// and a default partition for anything outside them, so an insert never fails for want of a partition. The
    /// application role gets no privilege on the partitions, only on the table, and each partition has row-level
    /// security enabled and forced with no policy, which denies everything.
    /// </summary>
    public static MigrationBuilder CreateRunEventsTable(this MigrationBuilder migration, DateOnly first, int months)
    {
        migration.Sql(TableSql);

        for (var offset = 0; offset < months; offset++)
        {
            var start = first.AddMonths(offset);
            var end = start.AddMonths(1);
            migration.Sql(
                $"""
                CREATE TABLE run_events_{start:yyyy_MM} PARTITION OF run_events
                  FOR VALUES FROM ('{start:yyyy-MM-dd} 00:00:00+00') TO ('{end:yyyy-MM-dd} 00:00:00+00');
                """);
            migration.EnableForcedSecurity($"run_events_{start:yyyy_MM}");
        }

        migration.Sql("CREATE TABLE run_events_default PARTITION OF run_events DEFAULT;");
        migration.EnableForcedSecurity("run_events_default");
        return migration;
    }

    #endregion
}
