namespace Drover.Tests.Common;

/// <summary>
/// One real PostgreSQL for a test assembly. It uses the server named by <c>DROVER_TEST_POSTGRES</c> (a superuser
/// connection string, see scripts/dev-postgres.sh) and starts a container when that is not set. It migrates one
/// template database, and each test class copies that template into a database of its own. Role passwords are
/// derived from the server (see <see cref="TestRolePasswords"/>), so assemblies launched together against one
/// server agree on them and need no lease or queue.
/// </summary>
public sealed class PostgresServer : IAsyncLifetime
{
    #region Private Fields

    // A loaded runner can starve the harness's first connection. Bound it so one that never answers fails with one
    // line, and one that answers late costs only the attempts it needed.
    private static readonly TimeSpan connectionPatience = TimeSpan.FromSeconds(60);

    private string ownerPassword = null!; // set in InitializeAsync, from the server found there

    private string appPassword = null!;

    private readonly string privilegedPassword = NewPassword();

    private readonly string privilegedRole = $"drover_test_superuser_{Guid.NewGuid():N}";

    private readonly string templateName = $"drover_template_{Guid.NewGuid():N}";

    // Roles belong to the server, so two bootstraps at once race on them. Databases are made one at a time.
    private readonly SemaphoreSlim creating = new(1, 1);

    private PostgreSqlContainer? container;

    private NpgsqlConnectionStringBuilder server = null!; // set first thing in InitializeAsync

    #endregion

    #region Properties

    /// <summary>
    /// The owner password this run gave <c>drover_owner</c>, for a test that runs the real one-off initialization
    /// against these roles. Giving it the same value changes nothing.
    /// </summary>
    public string OwnerPassword => ownerPassword;

    #endregion

    #region Interface Implementations

    public async ValueTask InitializeAsync()
    {
        server = [with(await FindServer())];
        ownerPassword = TestRolePasswords.For(server, "drover_owner");
        appPassword = TestRolePasswords.For(server, "drover_app");

        await CreateTemplateDatabase();
        await CreatePrivilegedRole();
        await DatabaseBootstrap.Run(ForDatabase(templateName).ConnectionString, ownerPassword, appPassword);
        await DatabaseMigrator.Run(ForRole("drover_owner", ownerPassword, templateName).ConnectionString);
        NpgsqlConnection.ClearAllPools();
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await ExecuteOnServer($"DROP DATABASE IF EXISTS {templateName} WITH (FORCE)");
        await DropPrivilegedRole();

        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    #endregion

    #region Public Methods

    /// <summary>A migrated database of its own, with the roles granted. Disposing it drops it.</summary>
    public async Task<TestDatabase> CreateDatabase()
    {
        var name = $"drover_test_{Guid.NewGuid():N}";
        await creating.WaitAsync();

        try
        {
            await ExecuteOnServer($"CREATE DATABASE {name} TEMPLATE {templateName}");

            // A copied database keeps its tables and grants but not its database-level privileges, so grant them again.
            await DatabaseBootstrap.Run(ForDatabase(name).ConnectionString, ownerPassword, appPassword);
        }
        finally
        {
            creating.Release();
        }

        var app = ForRole("drover_app", appPassword, name);
        app.Timeout = 10;
        var privileged = ForRole(privilegedRole, privilegedPassword, name);
        privileged.Timeout = 10;
        return new TestDatabase(
            ForDatabase(name).ConnectionString,
            app.ConnectionString,
            privileged.ConnectionString,
            () => Drop(name));
    }

    #endregion

    #region Private Methods

    private async Task<string> FindServer()
    {
        var named = Environment.GetEnvironmentVariable("DROVER_TEST_POSTGRES");

        if (!string.IsNullOrWhiteSpace(named))
        {
            return named;
        }

        container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await container.StartAsync();
        return container.GetConnectionString();
    }

    private async Task CreateTemplateDatabase()
    {
        await using var connection = new NpgsqlConnection(server.ConnectionString);
        await OpenWithPatience(connection);
        await new NpgsqlCommand($"CREATE DATABASE {templateName}", connection).ExecuteNonQueryAsync();
    }

    // Not ClearAllPools: other tests in this assembly are using their own pools at the same time.
    private Task Drop(string name) => ExecuteOnServer($"DROP DATABASE IF EXISTS {name} WITH (FORCE)");

    private async Task CreatePrivilegedRole()
    {
        await using var connection = new NpgsqlConnection(server.ConnectionString);
        await connection.OpenAsync();
        // PostgreSQL does not accept a bind parameter in CREATE ROLE. The password is hex generated above, so it
        // cannot hold a quote; there is nothing to inject in it.
        var role = $"\"{privilegedRole}\"";
        await using var command = new NpgsqlCommand($"CREATE ROLE {role} LOGIN SUPERUSER PASSWORD '{privilegedPassword}'", connection);
        _ = await command.ExecuteNonQueryAsync();
    }

    private Task DropPrivilegedRole()
    {
        var role = $"\"{privilegedRole}\"";
        return ExecuteOnServer($"DROP ROLE IF EXISTS {role}");
    }

    private async Task ExecuteOnServer(string sql)
    {
        await using var connection = new NpgsqlConnection(server.ConnectionString);
        await connection.OpenAsync();
        await new NpgsqlCommand(sql, connection).ExecuteNonQueryAsync();
    }

    private static async Task OpenWithPatience(NpgsqlConnection connection)
    {
        using var budget = new CancellationTokenSource(connectionPatience);
        var delay = TimeSpan.FromMilliseconds(500);

        while (true)
        {
            var failure = await TryOpen(connection, budget.Token);

            if (failure is null)
            {
                return;
            }

            try
            {
                await Task.Delay(delay, budget.Token);
            }
            catch (OperationCanceledException)
            {
                throw new PostgresUnavailableException(connectionPatience, failure);
            }

            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 2000));
        }
    }

    private static async Task<Exception?> TryOpen(NpgsqlConnection connection, CancellationToken token)
    {
        try
        {
            await connection.OpenAsync(token);
            return null;
        }
        catch (Exception failure) when (IsConnectFailure(failure))
        {
            return failure;
        }
    }

    private static bool IsConnectFailure(Exception failure) =>
        failure is NpgsqlException { IsTransient: true }
            or NpgsqlException { InnerException: OperationCanceledException }
            or TimeoutException
            or OperationCanceledException;

    private NpgsqlConnectionStringBuilder ForDatabase(string database) =>
        new(server.ConnectionString) { Database = database };

    private NpgsqlConnectionStringBuilder ForRole(string role, string password, string database)
    {
        var builder = ForDatabase(database);
        builder.Username = role;
        builder.Password = password;
        return builder;
    }

    private static string NewPassword() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    #endregion
}

/// <summary>A database made for one test class. Disposing it drops it.</summary>
public sealed class TestDatabase(
    string adminConnectionString,
    string appConnectionString,
    string privilegedConnectionString,
    Func<Task> drop)
    : IAsyncDisposable
{
    #region Properties

    public string AdminConnectionString { get; } = adminConnectionString;

    /// <summary>The connection string of <c>drover_app</c>, the role every request and job uses.</summary>
    public string AppConnectionString { get; } = appConnectionString;

    /// <summary>A generated, password-authenticated superuser used only by role-policy tests.</summary>
    public string PrivilegedConnectionString { get; } = privilegedConnectionString;

    #endregion

    #region Interface Implementations

    public async ValueTask DisposeAsync() => await drop();

    #endregion
}

/// <summary>The first connection to the test PostgreSQL did not succeed before the bound ran out.</summary>
public sealed class PostgresUnavailableException(TimeSpan limit, Exception cause)
    : Exception($"PostgreSQL did not accept a connection within {limit}.", cause)
{
    /// <summary>The total time the first connection was allowed.</summary>
    public TimeSpan Limit { get; } = limit;
}
