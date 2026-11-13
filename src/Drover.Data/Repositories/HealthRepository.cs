namespace Drover.Data.Repositories;

/// <summary>
/// What the status page shows about the server as a whole. Both reads go through functions in schema <c>sys</c>,
/// which return counts and a version name and no tenant data.
/// </summary>
public sealed class HealthRepository(DroverDbContext db)
{
    #region Public Methods

    /// <summary>The runs waiting for or held by a worker, across all spaces, as counts.</summary>
    public Task<BackgroundWork> ReadWork(CancellationToken cancellationToken) =>
        db.Database
            .SqlQuery<BackgroundWork>(
                $"""
                SELECT queued_runs AS queued, running_runs AS running, waiting_runs AS waiting, expired_leases, oldest_lease_expires_at
                FROM sys.health()
                """)
            .SingleAsync(cancellationToken);

    /// <summary>
    /// The newest migration the database has, through <c>sys.applied_migration</c>, beside the newest this build
    /// holds. The application role cannot read the migration history itself.
    /// </summary>
    public async Task<SchemaVersion> ReadSchemaVersion(CancellationToken cancellationToken)
    {
        var applied = await db.Database
            .SqlQuery<string?>($"SELECT sys.applied_migration() AS \"Value\"")
            .SingleAsync(cancellationToken);
        return new SchemaVersion(applied, db.Database.GetMigrations().LastOrDefault());
    }

    #endregion
}

/// <summary>Counts of runs by state across all spaces. Nothing here names a space, a person, or a run.</summary>
public sealed record BackgroundWork(long Queued, long Running, long Waiting, long ExpiredLeases, DateTimeOffset? OldestLeaseExpiresAt);

/// <summary>The newest migration applied to the database, and the newest this build would apply.</summary>
public sealed record SchemaVersion(string? Applied, string? Expected)
{
    public bool IsCurrent => Applied is not null && Applied == Expected;
}
