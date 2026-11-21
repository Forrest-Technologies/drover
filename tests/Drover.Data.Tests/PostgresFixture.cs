namespace Drover.Data.Tests;

/// <summary>
/// One real PostgreSQL for the whole data test run. It uses the server named by <c>DROVER_TEST_POSTGRES</c> (a
/// superuser connection string, see scripts/dev-postgres.sh) and starts a container when that is not set. It makes
/// a database of its own, bootstraps the roles with passwords generated for this run, applies the migrations as
/// <c>drover_owner</c>, and hands out connections as <c>drover_app</c>. Roles belong to the server, not the
/// database, so their passwords are derived from the server (see <see cref="TestRolePasswords"/>) and assemblies
/// launched together agree on them without a lease.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    #region Private Fields

    private readonly string databaseName = $"drover_test_{Guid.NewGuid():N}";

    private string ownerPassword = null!; // set in InitializeAsync, from the server found there

    private string appPassword = null!;

    private readonly Stopwatch elapsed = new();

    private readonly Lazy<Task<string>> scratchTemplate;

    private PostgreSqlContainer? container;

<<<<<<< HEAD
=======
    private readonly PostgresLocator locator;


    private PostgresRoleLease? roleLease;

>>>>>>> 4c24c01e (fix: give browser and PostgreSQL test lookups one clear failure)
    private string? scratchTemplateName;

    private NpgsqlConnectionStringBuilder serverConnection = null!; // set first thing in InitializeAsync

    #endregion

    #region Constructors

    public PostgresFixture()
    {
        scratchTemplate = new Lazy<Task<string>>(BuildScratchTemplate);
        locator = new(
            () => Environment.GetEnvironmentVariable("DROVER_TEST_POSTGRES"),
        async cancellationToken =>
        {
            container = new PostgreSqlBuilder("postgres:16-alpine").Build();
            await container.StartAsync(cancellationToken);
            return container.GetConnectionString();
        });
    }

    #endregion

    #region Properties

    public string AdminConnectionString => ForDatabase(serverConnection, databaseName).ConnectionString;

    #endregion

    #region Interface Implementations

    public async ValueTask InitializeAsync()
    {
        elapsed.Start();
<<<<<<< HEAD
        serverConnection = [with(await FindServer())];
        ownerPassword = TestRolePasswords.For(serverConnection, "drover_owner");
        appPassword = TestRolePasswords.For(serverConnection, "drover_app");
=======
        serverConnection = [with(await locator.Find(TestContext.Current.CancellationToken))];
        roleLease = await PostgresRoleLease.Acquire(serverConnection.ConnectionString);
>>>>>>> 4c24c01e (fix: give browser and PostgreSQL test lookups one clear failure)

        await CreateDatabase();
        await DatabaseBootstrap.Run(AdminConnectionString, ownerPassword, appPassword);
        await DatabaseMigrator.Run(ConnectionStringFor("drover_owner", ownerPassword).ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(serverConnection.ConnectionString);
        await connection.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)", connection).ExecuteNonQueryAsync();

        if (scratchTemplateName is not null)
        {
            await new NpgsqlCommand($"DROP DATABASE IF EXISTS {scratchTemplateName} WITH (FORCE)", connection).ExecuteNonQueryAsync();
        }

        if (container is not null)
        {
            await container.DisposeAsync();
        }

        ReportDuration();
    }

    #endregion

    #region Public Methods

    /// <summary>A connection string for <c>drover_app</c>. <paramref name="applicationName"/> tells it apart in pg_stat_activity.</summary>
    public string AppConnectionString(int maxPoolSize = 10, bool noResetOnClose = false, string? applicationName = null)
    {
        var builder = ConnectionStringFor("drover_app", appPassword);
        builder.MaxPoolSize = maxPoolSize;
        builder.NoResetOnClose = noResetOnClose;
        builder.Timeout = 10;

        if (applicationName is not null)
        {
            builder.ApplicationName = applicationName;
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// A database of its own with the roles and migrations, for a test that breaks a rule on purpose. Disposing it
    /// drops it, so the main database never sees what the test did.
    /// </summary>
    public async Task<ScratchDatabase> CreateScratchDatabase()
    {
        var templateName = await scratchTemplate.Value;
        var name = $"drover_scratch_{Guid.NewGuid():N}";
        await ExecuteOnServer($"CREATE DATABASE {name} TEMPLATE {templateName}");

        // A copied database keeps its tables and grants but not its database-level privileges, so grant them again.
        await DatabaseBootstrap.Run(ForDatabase(serverConnection, name).ConnectionString, ownerPassword, appPassword);
        var app = ConnectionStringFor("drover_app", appPassword, name);
        app.Timeout = 10;
        return new ScratchDatabase(
            ForDatabase(serverConnection, name).ConnectionString,
            ConnectionStringFor("drover_owner", ownerPassword, name).ConnectionString,
            app.ConnectionString,
            () => ExecuteOnServer($"DROP DATABASE IF EXISTS {name} WITH (FORCE)"));
    }

    /// <summary>The generated checks over the real database, with every seeder this assembly registers.</summary>
    public Task<IsolationHarness> OpenHarness() =>
        IsolationHarness.Create(BuildServices(), AppConnectionString(), OwnershipMap.Current, SeederRegistry.Discover());

    public Task Bootstrap() => DatabaseBootstrap.Run(AdminConnectionString, ownerPassword, appPassword);

    public string RoleConnectionString(string role, string password) =>
        ConnectionStringFor(role, password).ConnectionString;

    public ServiceProvider BuildServices(int maxPoolSize = 10, bool noResetOnClose = false, string? applicationName = null)
    {
        var services = new ServiceCollection();
        services.AddDroverData(AppConnectionString(maxPoolSize, noResetOnClose, applicationName));
        return services.BuildServiceProvider();
    }

    #endregion

    #region Private Methods

    private Task CreateDatabase() => ExecuteOnServer($"CREATE DATABASE {databaseName}");

    /// <summary>
    /// A migrated database nobody connects to, copied for each scratch database so that a test which breaks a rule
    /// on purpose starts in a fraction of a second.
    /// </summary>
    private async Task<string> BuildScratchTemplate()
    {
        var name = $"drover_template_{Guid.NewGuid():N}";
        await ExecuteOnServer($"CREATE DATABASE {name}");
        await DatabaseBootstrap.Run(ForDatabase(serverConnection, name).ConnectionString, ownerPassword, appPassword);
        var owner = ConnectionStringFor("drover_owner", ownerPassword, name).ConnectionString;
        await DatabaseMigrator.Run(owner);
        NpgsqlConnection.ClearAllPools();
        scratchTemplateName = name;
        return name;
    }

    private async Task ExecuteOnServer(string sql)
    {
        await using var connection = new NpgsqlConnection(serverConnection.ConnectionString);
        await connection.OpenAsync();
        await new NpgsqlCommand(sql, connection).ExecuteNonQueryAsync();
    }

    private NpgsqlConnectionStringBuilder ConnectionStringFor(string role, string password, string? database = null)
    {
        var builder = ForDatabase(serverConnection, database ?? databaseName);
        builder.Username = role;
        builder.Password = password;
        return builder;
    }

    private static NpgsqlConnectionStringBuilder ForDatabase(NpgsqlConnectionStringBuilder server, string database) =>
        new(server.ConnectionString) { Database = database };

    /// <summary>
    /// Adds the time the whole data suite took, database setup included, to the job summary on GitHub Actions, so a
    /// slow isolation suite shows on every pull request. It writes nowhere else.
    /// </summary>
    private void ReportDuration()
    {
        var summary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");

        if (!string.IsNullOrEmpty(summary))
        {
            var line = $"Isolation suite (Drover.Data.Tests, with database setup): {elapsed.Elapsed.TotalSeconds:F1} s";
            File.AppendAllText(summary, $"{line}{Environment.NewLine}");
        }
    }

    #endregion
}
