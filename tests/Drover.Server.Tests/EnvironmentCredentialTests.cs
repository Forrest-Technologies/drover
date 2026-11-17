namespace Drover.Server.Tests;

/// <summary>
/// A platform that builds the image passes the application password as an environment variable rather than as a
/// file. The value must reach the connection and nothing else: no log line, no status row, no health answer. The
/// variables belong to the whole process, so these tests run alone.
/// </summary>
[Collection(Name)]
public sealed class EnvironmentCredentialTests(PostgresServer postgres)
{
    #region Private Fields

    private static readonly string[] initializeArguments = ["--initialize-database"];

    #endregion

    #region Properties

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    #endregion

    #region Public Methods

    [Fact]
    public async Task The_server_runs_on_an_app_password_from_the_environment_and_no_log_status_or_health_answer_carries_it()
    {
        await using var database = await postgres.CreateDatabase();
        var password = new NpgsqlConnectionStringBuilder(database.AppConnectionString).Password!;
        var logs = new LogCapture();

        using (Environment(database, password))
        {
            await using var factory = Factory(database, logs);
            var client = Client(factory, "127.0.0.1");

            var ready = await client.Get("/health/ready");
            var status = await Status(factory, client);
            var failedSignIn = await Client(factory, "192.0.2.10").SignIn("nobody@example.test", "not-the-password-1");

            ready.StatusCode.Should().Be(HttpStatusCode.OK);
            status.Should().Contain("drover_app").And.NotContain(password);
            failedSignIn.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await ready.Content.ReadAsStringAsync(Token)).Should().NotContain(password);
        }

        logs.Entries.Should().NotBeEmpty();
        logs.Entries.Should().NotContain(entry => entry.Text.Contains(password, StringComparison.Ordinal));
    }

    /// <summary>
    /// Work at start reaches the database, so a wrong password stops the start with PostgreSQL's refusal. The
    /// refusal names the role, never the password. The test first makes sure the database really checks passwords for
    /// the role: one set to trust every connection cannot show this (scripts/dev-postgres.sh sets it up to check).
    /// </summary>
    [Fact]
    public async Task A_wrong_app_password_from_the_environment_stops_the_start_without_being_echoed()
    {
        await using var database = await postgres.CreateDatabase();
        var wrong = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var logs = new LogCapture();
        await RequirePasswordChecks(database, wrong);
        PostgresException refusal;

        using (Environment(database, wrong))
        {
            await using var factory = Factory(database, logs);
            var start = () => Client(factory, "127.0.0.1");
            refusal = start.Should().Throw<PostgresException>().Which;
        }

        refusal.SqlState.Should().Be(PostgresErrorCodes.InvalidPassword);
        refusal.ToString().Should().Contain("drover_app").And.NotContain(wrong);
        logs.Entries.Should().NotContain(entry => entry.Text.Contains(wrong, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_one_off_initialization_takes_every_password_from_the_environment_and_prints_none()
    {
        await using var database = await postgres.CreateDatabase();
        var passwords = Passwords(database);

        var (failure, output) = RunInitialization(Environment(database, passwords));

        failure.Should().BeNull();
        await using var app = new NpgsqlConnection(database.AppConnectionString);
        await app.OpenAsync(Token);
        output.Should().NotContainAny(passwords.Admin, passwords.Owner, passwords.App);
    }

    [Fact]
    public async Task A_one_off_initialization_that_cannot_reach_the_database_fails_without_printing_a_password()
    {
        await using var database = await postgres.CreateDatabase();
        var passwords = Passwords(database);
        var variables = Environment(database, passwords);
        variables["Database__Port"] = UnusedPort().ToString(System.Globalization.CultureInfo.InvariantCulture);

        var (failure, output) = RunInitialization(variables);

        failure.Should().NotBeNull();
        $"{failure} {output}".Should().NotContainAny(passwords.Admin, passwords.Owner, passwords.App);
    }

    /// <summary>
    /// The serving app that is allowed to migrate and cannot (here, a wrong owner password) says so in one critical
    /// line without a secret, sets a non-zero exit code and does not start, so the platform keeps the old build serving.
    /// </summary>
    [Fact]
    public async Task A_server_that_cannot_apply_its_migrations_exits_non_zero_without_printing_a_password()
    {
        await using var database = await postgres.CreateDatabase();
        var passwords = Passwords(database);
        var wrong = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        await RequirePasswordChecks(database, wrong);
        var variables = Environment(database, passwords);
        variables.Remove("Database__Password");
        variables["Database__OwnerPassword"] = wrong;

        var (failure, output) = RunInitialization(variables, []);
        var exitCode = System.Environment.ExitCode;
        System.Environment.ExitCode = 0;

        failure.Should().BeNull();
        exitCode.Should().Be(1);
        output.Should().Contain("Applying the database migrations at start failed").And.Contain("28P01");
        output.Should().NotContainAny(wrong, passwords.Admin, passwords.Owner, passwords.App);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Runs the real program with <c>--initialize-database</c> in this process, as the one-off run does, with
    /// <paramref name="variables"/> set, and returns what it threw and everything it printed.
    /// </summary>
    // The one-off run has no logger of its own: what it prints goes to the console, so the test reads the console.
#pragma warning disable RS0030
    private static (Exception? Failure, string Output) RunInitialization(Dictionary<string, string?> variables, string[]? arguments = null)
    {
        var output = new StringWriter();
        var (console, errors) = (Console.Out, Console.Error);
        Console.SetOut(output);
        Console.SetError(output);

        try
        {
            using (new ProcessEnvironment(variables))
            {
                typeof(Program).Assembly.EntryPoint!.Invoke(null, [arguments ?? initializeArguments]);
            }

            return (null, output.ToString());
        }
        catch (TargetInvocationException failure)
        {
            return (failure.InnerException ?? failure, output.ToString());
        }
        finally
        {
            Console.SetOut(console);
            Console.SetError(errors);
        }
    }
#pragma warning restore RS0030

    private (string Admin, string Owner, string App) Passwords(TestDatabase database)
    {
        // A local server that trusts the superuser takes any value; a server that checks needs the real one.
        var admin = new NpgsqlConnectionStringBuilder(database.AdminConnectionString).Password;
        return (
            string.IsNullOrEmpty(admin) ? Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) : admin,
            postgres.OwnerPassword,
            new NpgsqlConnectionStringBuilder(database.AppConnectionString).Password!);
    }

    private static async Task RequirePasswordChecks(TestDatabase database, string wrong)
    {
        var connection = new NpgsqlConnectionStringBuilder(database.AppConnectionString) { Password = wrong, Pooling = false };
        await using var attempt = new NpgsqlConnection(connection.ConnectionString);
        var open = () => attempt.OpenAsync(Token);

        await open.Should().ThrowAsync<PostgresException>(
            "this PostgreSQL must check drover_app's password; scripts/dev-postgres.sh start sets that up");
    }

    private static int UnusedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static DroverFactory Factory(TestDatabase database, LogCapture logs) =>
        new(database, Path.Combine(Path.GetTempPath(), $"drover-keys-{Guid.NewGuid():N}"), logs, new AdjustableTimeProvider(),
            new HashCounter(), new Dictionary<string, string?>(), useAppConnection: false);

    private static ApiClient Client(DroverFactory factory, string remoteIp) =>
        new(factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false,
        }))
        { RemoteIp = remoteIp };

    private static async Task<string> Status(DroverFactory factory, ApiClient client)
    {
        var code = factory.Services.GetRequiredService<SetupCode>().Display;
        var setup = await client.Post("/api/setup",
            new SetupRequest("owner@example.test", "Owner", $"pw-{Guid.NewGuid():N}", code));
        setup.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = (await setup.Content.ReadFromJsonAsync<SessionResponse>(Token))!;

        var status = await client.Get($"/api/spaces/{session.Spaces[0].Id}/status");
        status.StatusCode.Should().Be(HttpStatusCode.OK);
        return await status.Content.ReadAsStringAsync(Token);
    }

    private static ProcessEnvironment Environment(TestDatabase database, string appPassword) =>
        new(Location(database, new() { ["Database__AppPassword"] = appPassword }));

    private static Dictionary<string, string?> Environment(TestDatabase database, (string Admin, string Owner, string App) passwords) =>
        Location(database, new()
        {
            ["Database__Password"] = passwords.Admin,
            ["Database__OwnerPassword"] = passwords.Owner,
            ["Database__AppPassword"] = passwords.App,
        });

    private static Dictionary<string, string?> Location(TestDatabase database, Dictionary<string, string?> passwords)
    {
        var connection = new NpgsqlConnectionStringBuilder(database.AppConnectionString);
        passwords["Database__Host"] = connection.Host;
        passwords["Database__Port"] = connection.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        passwords["Database__Database"] = connection.Database;
        return passwords;
    }

    #endregion

    #region Nested Types

    /// <summary>Sets process environment variables and puts back what was there when disposed.</summary>
    private sealed class ProcessEnvironment : IDisposable
    {
        #region Private Fields

        private readonly Dictionary<string, string?> previous = [];

        #endregion

        #region Constructors

        public ProcessEnvironment(Dictionary<string, string?> variables)
        {
            foreach (var (name, value) in variables)
            {
                previous[name] = System.Environment.GetEnvironmentVariable(name);
                System.Environment.SetEnvironmentVariable(name, value);
            }
        }

        #endregion

        #region Interface Implementations

        public void Dispose()
        {
            foreach (var (name, value) in previous)
            {
                System.Environment.SetEnvironmentVariable(name, value);
            }
        }

        #endregion
    }

    #endregion

    #region Constants

    public const string Name = "Process environment";

    #endregion
}

/// <summary>Tests that change the process environment run on their own, after every other test.</summary>
[CollectionDefinition(EnvironmentCredentialTests.Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentGroup;
