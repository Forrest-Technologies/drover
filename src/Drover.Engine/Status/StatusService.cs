namespace Drover.Engine.Status;

/// <summary>What the server knows about itself: the run queue as counts, and which migration the database has.</summary>
public sealed record ServerReadings(RunWorkload Workload, MigrationState Migrations);

/// <summary>The space's own connections, as last checked. Nothing here calls a provider or a tool server.</summary>
public sealed record SpaceReadings(IReadOnlyList<ModelConnection> Models, IReadOnlyList<ToolConnection> Tools);

/// <summary>
/// The readings behind the status page. Each read opens one short tenant scope and waits on nothing but the
/// database. Whether a person may see the server's readings is the caller's question; the policy answers it.
/// </summary>
public sealed class StatusService(
    TenantScope scope,
    ServerHealthRepository health,
    ModelConnectionRepository models,
    ToolConnectionRepository tools,
    PolicyService policy)
{
    #region Properties

    public bool SpaceAdminsSeeServerStatus => policy.Deployment.SpaceAdminsSeeServerStatus;

    #endregion

    #region Public Methods

    public async Task<ServerReadings> ReadServer(CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        var workload = await health.ReadWorkload(cancellationToken);
        var applied = await health.ReadAppliedMigration(cancellationToken);
        await work.Commit(cancellationToken);
        return new ServerReadings(workload, new MigrationState(applied, health.NewestKnownMigration));
    }

    /// <summary>
    /// Which migration the database has against the build. It needs no tenant and returns no tenant data, so
    /// readiness and the start-up check use it as well as the status page.
    /// </summary>
    public async Task<MigrationState> ReadMigrations(CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        var applied = await health.ReadAppliedMigration(cancellationToken);
        await work.Commit(cancellationToken);
        return new MigrationState(applied, health.NewestKnownMigration);
    }

    public async Task<SpaceReadings> ReadSpace(CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        var modelConnections = await models.ListConnections(cancellationToken);
        var toolConnections = policy.Deployment.AllowToolConnections ? await tools.ListConnections(cancellationToken) : [];
        await work.Commit(cancellationToken);
        return new SpaceReadings(modelConnections, toolConnections);
    }

    #endregion
}
