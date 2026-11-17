namespace Drover.Server.Health;

/// <summary>
/// Logs once at start, as an error, when the database's migrations do not match this build, so the real cause is
/// the first thing in the log rather than a 500 on whichever request touches new schema. It does not stop the
/// server: a platform would restart it in a loop. Readiness keeps traffic off it instead.
/// </summary>
public sealed class MigrationStartupCheck(IServiceScopeFactory scopes, ILogger<MigrationStartupCheck> logger) : IHostedService
{
    #region Interface Implementations

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var state = await scope.ServiceProvider.GetRequiredService<StatusService>().ReadMigrations(cancellationToken);
            if (MigrationAdvice.Readiness(state) is { } advice)
            {
                logger.LogError(
                    "{Advice} The database is at {Applied}; this build carries {Newest}",
                    advice,
                    state.Applied ?? "no migration",
                    state.Newest);
            }
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or IOException)
        {
            // Not fatal here: readiness reports the outage.
            logger.LogWarning("The database's migrations could not be checked at start because PostgreSQL did not answer");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    #endregion
}
