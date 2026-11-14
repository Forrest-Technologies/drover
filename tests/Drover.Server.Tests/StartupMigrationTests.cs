namespace Drover.Server.Tests;

/// <summary>
/// An upgrade works without a separate step where the deployment allows the migration credential in the serving
/// process and the process was given it: the pending migrations are applied before the first request, and the
/// server then serves as <c>drover_app</c>. Each database is really behind: the newest migrations are rolled back by
/// their own <c>Down</c> methods, as a database that never saw this build would be.
/// </summary>
public sealed class StartupMigrationTests(PostgresServer postgres)
{
    #region Public Methods

    [Fact]
    public async Task With_the_owner_and_superuser_passwords_a_database_two_migrations_behind_is_migrated_before_serving()
    {
        await using var database = await BehindBy(2);
        await using var server = Start(database, Credentials.Both);

        await server.ShouldBeReadyOnTheNewestMigration(database);
    }

    [Fact]
    public async Task With_only_the_owner_password_a_database_one_migration_behind_is_migrated_and_the_superuser_is_never_needed()
    {
        await using var database = await BehindBy(1);
        await using var server = Start(database, Credentials.OwnerOnly);

        await server.ShouldBeReadyOnTheNewestMigration(database);
    }

    [Fact]
    public async Task The_server_still_serves_as_the_application_role_after_migrating()
    {
        await using var database = await BehindBy(1);
        await using var server = Start(database, Credentials.Both);

        var status = await server.SetUpAndReadStatus();

        status.Should().Contain("drover_app").And.NotContain(postgres.OwnerPassword);
    }

    [Fact]
    public async Task A_database_that_is_up_to_date_starts_the_same_way()
    {
        await using var database = await postgres.CreateDatabase();
        await using var server = Start(database, Credentials.OwnerOnly);

        await server.ShouldBeReadyOnTheNewestMigration(database);
    }

    [Fact]
    public async Task Where_the_rule_is_off_and_no_credential_is_given_nothing_is_applied_and_readiness_says_what_to_run()
    {
        await using var database = await BehindBy(1);
        await using var server = Start(database, Credentials.None, allowMigrationCredential: false);

        var ready = await server.Client.Get("/health/ready");

        ready.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await AppliedMigration(database)).Should().NotBe(Newest());
        var report = await server.Services.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Name == "migrations", Token);
        report.Entries["migrations"].Description.Should().Contain("--initialize-database");
        server.Logs.Entries.Should().ContainSingle(entry => entry.Category == typeof(MigrationStartupCheck).FullName
            && entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Where_the_rule_is_on_but_no_credential_is_given_nothing_is_applied_and_readiness_says_what_to_run()
    {
        await using var database = await BehindBy(1);
        await using var server = Start(database, Credentials.None);

        var ready = await server.Client.Get("/health/ready");

        ready.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await AppliedMigration(database)).Should().NotBe(Newest());
    }

    [Fact]
    public async Task Where_the_rule_is_off_a_server_given_the_owner_password_does_not_start()
    {
        await using var database = await BehindBy(1);
        await using var server = Start(database, Credentials.OwnerOnly, allowMigrationCredential: false);

        var start = () => server.Services;

        start.Should().Throw<Exception>();
        (await AppliedMigration(database)).Should().NotBe(Newest());
    }

    #endregion

    #region Private Methods

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Newest()
    {
        using var context = Context("Host=unused");
        return context.Database.GetMigrations().Last();
    }

    private async Task<TestDatabase> BehindBy(int migrations)
    {
        var database = await postgres.CreateDatabase();
        var owner = new NpgsqlConnectionStringBuilder(database.AdminConnectionString)
        {
            Username = "drover_owner",
            Password = postgres.OwnerPassword,
        };
        await using var context = Context(owner.ConnectionString);
        var target = context.Database.GetMigrations().SkipLast(migrations).Last();
        await context.GetService<IMigrator>().MigrateAsync(target, Token);
        (await AppliedMigration(database)).Should().Be(target);
        return database;
    }

    private static DroverDbContext Context(string connectionString)
    {
        var options = new DbContextOptionsBuilder<DroverDbContext>();
        options.UseDroverNpgsql(connectionString);
        return new DroverDbContext(options.Options);
    }

    private static async Task<string?> AppliedMigration(TestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.AppConnectionString);
        await connection.OpenAsync(Token);
        return (string?)await new NpgsqlCommand("SELECT sys.applied_migration()", connection).ExecuteScalarAsync(Token);
    }

    private RunningServer Start(TestDatabase database, Credentials credentials, bool allowMigrationCredential = true)
    {
        var connection = new NpgsqlConnectionStringBuilder(database.AppConnectionString);
        var settings = new Dictionary<string, string?>
        {
            ["Database:Host"] = connection.Host,
            ["Database:Port"] = connection.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Database:Database"] = connection.Database,
            ["Database:AppPassword"] = connection.Password,
        };

        if (!allowMigrationCredential)
        {
            settings["Drover:Policy:Rules:AllowMigrationCredentialInServer"] = "false";
        }

        if (credentials is not Credentials.None)
        {
            settings["Database:OwnerPassword"] = postgres.OwnerPassword;
        }

        if (credentials is Credentials.Both)
        {
            // A server that trusts the superuser takes any value; one that checks needs the real one.
            var admin = new NpgsqlConnectionStringBuilder(database.AdminConnectionString).Password;
            settings["Database:Password"] = string.IsNullOrEmpty(admin) ? Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) : admin;
        }

        var logs = new LogCapture();
        var keyRing = Path.Combine(Path.GetTempPath(), $"drover-keys-{Guid.NewGuid():N}");
        var factory = new DroverFactory(database, keyRing, logs, new AdjustableTimeProvider(), new HashCounter(), settings, useAppConnection: false);
        return new RunningServer(factory, logs, keyRing);
    }

    #endregion

    #region Nested Types

    private enum Credentials
    {
        None,
        OwnerOnly,
        Both,
    }

    private sealed class RunningServer(DroverFactory factory, LogCapture logs, string keyRing) : IAsyncDisposable
    {
        #region Properties

        public LogCapture Logs { get; } = logs;

        public IServiceProvider Services => factory.Services;

        public ApiClient Client => new(factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false,
        }))
        { RemoteIp = "127.0.0.1" };

        #endregion

        #region Public Methods

        public async Task ShouldBeReadyOnTheNewestMigration(TestDatabase database)
        {
            var ready = await Client.Get("/health/ready");

            ready.StatusCode.Should().Be(HttpStatusCode.OK);
            (await AppliedMigration(database)).Should().Be(Newest());
            (await SetUpAndReadStatus()).Should().Contain("Up to date");
        }

        /// <summary>Creates the owner account through the API, which signs in, and reads the status page with it.</summary>
        public async Task<string> SetUpAndReadStatus()
        {
            var client = Client;
            var code = Services.GetRequiredService<SetupCode>().Display;
            var setup = await client.Post("/api/setup", new SetupRequest("owner@example.test", "Owner", $"pw-{Guid.NewGuid():N}", code));
            setup.StatusCode.Should().Be(HttpStatusCode.Created);
            var session = (await setup.Content.ReadFromJsonAsync<SessionResponse>(Token))!;
            var status = await client.Get($"/api/spaces/{session.Spaces[0].Id}/status");
            status.StatusCode.Should().Be(HttpStatusCode.OK);
            return await status.Content.ReadAsStringAsync(Token);
        }

        public async ValueTask DisposeAsync()
        {
            await factory.DisposeAsync();

            if (Directory.Exists(keyRing))
            {
                Directory.Delete(keyRing, recursive: true);
            }
        }

        #endregion
    }

    #endregion
}
