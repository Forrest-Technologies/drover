namespace Drover.Tests.Common;

/// <summary>
/// Finds one PostgreSQL for a test assembly, and says in one sentence what went wrong when it cannot. Environment
/// and the container start are parameters so the two messages can be tested without Docker. A connection string
/// never appears in either message: only its parsed host, port and user do. A <see cref="PostgresAttempt"/> makes
/// every caller that shares it await one attempt; the assembly fixtures share one for the whole process.
/// </summary>
public sealed class PostgresLocator(
    Func<string?> environment,
    Func<CancellationToken, Task<string>> startContainer,
    PostgresAttempt? attempt = null)
{
    #region Private Fields

    private readonly object gate = new();

    private Task<string>? ownAttempt;

    #endregion

    #region Properties

    /// <summary>The one attempt every assembly fixture in this process uses.</summary>
    public static PostgresAttempt SharedAttempt { get; } = new();

    #endregion

    #region Public Methods

    /// <summary>The locator the assembly fixtures use: the same attempt for the whole process.</summary>
    public static PostgresLocator Shared(Func<CancellationToken, Task<string>> start) =>
        new(() => Environment.GetEnvironmentVariable("DROVER_TEST_POSTGRES"), start, SharedAttempt);

    public Task<string> Find(CancellationToken cancellationToken) =>
        attempt is not null
            ? attempt.Get(() => FindNow(cancellationToken))
            : Own(() => FindNow(cancellationToken));

    #endregion

    #region Private Methods

    private Task<string> Own(Func<Task<string>> create)
    {
        lock (gate)
        {
            return ownAttempt ??= create();
        }
    }

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
            return await startContainer(cancellationToken);
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
