namespace Drover.Engine.Health;

/// <summary>
/// Reads what the status page shows about the server as a whole. Each read is a short transaction with no space
/// and no user, so row-level security shows it no tenant rows; the numbers come from functions in schema sys.
/// </summary>
public sealed class ServerHealthService(HealthRepository health, TenantScope scope)
{
    #region Public Methods

    public async Task<BackgroundWork> ReadWork(CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        var found = await health.ReadWork(cancellationToken);
        await work.Commit(cancellationToken);
        return found;
    }

    public async Task<SchemaVersion> ReadSchemaVersion(CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        var found = await health.ReadSchemaVersion(cancellationToken);
        await work.Commit(cancellationToken);
        return found;
    }

    #endregion
}
