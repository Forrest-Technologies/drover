namespace Drover.Data.Repositories;

/// <summary>The run queue of the whole server, as counts. No row of any space is returned.</summary>
public sealed record RunWorkload(long QueuedRuns, long RunningRuns, long WaitingRuns, long ExpiredLeases, DateTimeOffset? OldestLeaseExpiresAt);

/// <summary>
/// What the status page reads about the server itself. Runs inside a tenant scope the service has opened; both reads
/// go through functions in schema <c>sys</c> that return counts or one migration id.
/// </summary>
public sealed class ServerHealthRepository(DroverDbContext db)
{
    #region Properties

    /// <summary>The newest migration this build carries.</summary>
    public string? NewestKnownMigration => db.Database.GetMigrations().LastOrDefault();

    #endregion

    #region Public Methods

    public Task<RunWorkload> ReadWorkload(CancellationToken cancellationToken) =>
        db.Database
            .SqlQuery<RunWorkload>($"SELECT queued_runs, running_runs, waiting_runs, expired_leases, oldest_lease_expires_at FROM sys.health()")
            .SingleAsync(cancellationToken);

    /// <summary>The newest migration applied to the database, or null when none is.</summary>
    public Task<string?> ReadAppliedMigration(CancellationToken cancellationToken) =>
        db.Database
            .SqlQuery<string?>($"SELECT sys.applied_migration() AS \"Value\"")
            .SingleAsync(cancellationToken);

    #endregion
}
