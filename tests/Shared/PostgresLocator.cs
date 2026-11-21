namespace Drover.Tests.Common;

/// <summary>
/// Finds one PostgreSQL for a test assembly, and says in one sentence what went wrong when it cannot. Environment
/// and the container start are parameters so the two messages and the once-only rule can be tested without Docker.
/// A connection string never appears in either message: only its parsed host, port and user do.
/// </summary>
public sealed class PostgresLocator(
    Func<string?> environment,
    Func<CancellationToken, Task<string>>? startContainer = null)
{
    #region Private Fields

    private readonly Func<CancellationToken, Task<string>> start = startContainer ?? StartContainer;

    private TestEnvironmentException? failure;

    private string? connectionString;

    #endregion

    #region Public Methods

    public async Task<string> Find(CancellationToken cancellationToken)
    {
        if (failure is not null)
        {
            throw failure;
        }

        if (connectionString is not null)
        {
            return connectionString;
        }

        try
        {
            connectionString = await FindNow(cancellationToken);
            return connectionString;
        }
        catch (TestEnvironmentException found)
        {
            failure = found;
            throw;
        }
    }

    #endregion

    #region Private Methods

    private async Task<string> FindNow(CancellationToken cancellationToken)
    {
        var named = environment();

        if (!string.IsNullOrWhiteSpace(named))
        {
            await CheckNamed(named, cancellationToken);
            return named;
        }

        try
        {
            return await start(cancellationToken);
        }
        catch (Exception failed)
        {
            throw new TestEnvironmentException(
                $"""
                No PostgreSQL for the tests. DROVER_TEST_POSTGRES is not set, so a container of postgres:16-alpine was tried through Docker and did not start ({failed.GetType().Name}). Fix one of: start Docker and run again; or run `eval "$(scripts/dev-postgres.sh start)"` and run again.
                """,
                failed);
        }
    }

    private async Task CheckNamed(string named, CancellationToken cancellationToken)
    {
        NpgsqlConnectionStringBuilder parsed;

        try
        {
            parsed = new NpgsqlConnectionStringBuilder(named) { Timeout = 5 };
        }
        catch (Exception failed)
        {
            throw new TestEnvironmentException(
                $"DROVER_TEST_POSTGRES could not be read: {failed.GetType().Name}. Correct the variable.",
                failed);
        }

        try
        {
            await using var connection = new NpgsqlConnection(parsed.ConnectionString);
            await connection.OpenAsync(cancellationToken);
        }
        catch (Exception failed)
        {
            // Only the parsed address and user are printed. The connection string, which may hold a password, never
            // reaches the message or the exception text shown by the test runner.
            throw new TestEnvironmentException(
                $"""
                DROVER_TEST_POSTGRES names {parsed.Host}:{parsed.Port} as {parsed.Username}, and it did not accept a connection: {failed.GetType().Name}. Start it with `eval "$(scripts/dev-postgres.sh start)"` or correct the variable. The value is not shown here because it may hold a password.
                """,
                failed);
        }
    }

    private static async Task<string> StartContainer(CancellationToken cancellationToken)
    {
        var container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await container.StartAsync(cancellationToken);
        return container.GetConnectionString();
    }

    #endregion
}
