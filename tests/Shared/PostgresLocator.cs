namespace Drover.Tests.Common;

/// <summary>
/// Finds one PostgreSQL for a test assembly, and says in one sentence what went wrong when it cannot. Environment
/// and the container start are parameters so the two messages and the once-only rule can be tested without Docker.
/// A connection string never appears in either message: only its parsed host, port and user do. The fixture locator
/// sets <paramref name="shareInProcess"/> so every assembly fixture in one process shares the first result, whether
/// it is a connection or a failure; tests use separate instances with their own state.
/// </summary>
public sealed class PostgresLocator(
    Func<string?> environment,
    Func<CancellationToken, Task<string>> startContainer,
    bool shareInProcess = false)
{
    #region Private Fields

    private static readonly object sharedGate = new();

    private static TestEnvironmentException? sharedFailure;

    private static string? sharedConnection;

    private readonly Func<CancellationToken, Task<string>> start = startContainer;

    private readonly bool share = shareInProcess;

    private TestEnvironmentException? failure;

    private string? connectionString;

    #endregion

    #region Public Methods

    /// <summary>The locator the assembly fixtures use: one result for the whole process.</summary>
    public static PostgresLocator Shared(Func<CancellationToken, Task<string>> start) =>
        new(() => Environment.GetEnvironmentVariable("DROVER_TEST_POSTGRES"), start, shareInProcess: true);

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

        if (share)
        {
            lock (sharedGate)
            {
                if (sharedFailure is not null)
                {
                    failure = sharedFailure;
                    throw sharedFailure;
                }

                if (sharedConnection is not null)
                {
                    connectionString = sharedConnection;
                    return sharedConnection;
                }
            }
        }

        try
        {
            var found = await FindNow(cancellationToken);
            connectionString = found;

            if (share)
            {
                lock (sharedGate)
                {
                    sharedConnection ??= found;
                }
            }

            return found;
        }
        catch (TestEnvironmentException found)
        {
            failure = found;

            if (share)
            {
                lock (sharedGate)
                {
                    sharedFailure ??= found;
                }
            }

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

    #endregion
}
