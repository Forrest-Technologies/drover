namespace Drover.Server.Tests;

/// <summary>
/// A platform that builds the image passes the application password as an environment variable rather than as a
/// file. The value must reach the connection and nothing else: no log line, no status row, no health answer. The
/// variables belong to the whole process, so these tests run alone.
/// </summary>
[Collection(Name)]
public sealed class EnvironmentCredentialTests(PostgresServer postgres)
{
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
    /// Against a server that checks passwords (the container CI starts), the server refuses to start: work at start
    /// cannot reach the database. A local server set to trust connections lets the wrong password in. Either way,
    /// neither the failure, the log nor the health answer carries the password.
    /// </summary>
    [Fact]
    public async Task A_wrong_app_password_from_the_environment_is_never_echoed()
    {
        await using var database = await postgres.CreateDatabase();
        var wrong = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var logs = new LogCapture();
        string answer;

        using (Environment(database, wrong))
        {
            await using var factory = Factory(database, logs);
            answer = await ReadinessOrStartFailure(factory);
        }

        answer.Should().NotBeEmpty().And.NotContain(wrong);
        logs.Entries.Should().NotContain(entry => entry.Text.Contains(wrong, StringComparison.Ordinal));
    }

    #endregion

    #region Private Methods

    private static async Task<string> ReadinessOrStartFailure(DroverFactory factory)
    {
        try
        {
            var ready = await Client(factory, "127.0.0.1").Get("/health/ready");
            return $"{ready.StatusCode} {await ready.Content.ReadAsStringAsync(Token)}";
        }
        catch (Exception failure) when (failure is NpgsqlException or InvalidOperationException)
        {
            return failure.ToString();
        }
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

    private static ProcessEnvironment Environment(TestDatabase database, string password)
    {
        var connection = new NpgsqlConnectionStringBuilder(database.AppConnectionString);
        return new(new()
        {
            ["Database__Host"] = connection.Host,
            ["Database__Port"] = connection.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Database__Database"] = connection.Database,
            ["Database__AppPassword"] = password,
        });
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
