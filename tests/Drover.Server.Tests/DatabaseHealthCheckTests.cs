namespace Drover.Server.Tests;

/// <summary>
/// The paths that need no database. The healthy path and the turn to unhealthy when the database stops
/// are proven by the compose job in CI.
/// </summary>
public sealed class DatabaseHealthCheckTests
{
    #region Public Methods

    [Fact]
    public async Task Unhealthy_when_no_host_is_set()
    {
        var result = await Check(new());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task Unhealthy_when_the_password_file_is_missing()
    {
        var result = await Check(new() { Host = "localhost", PasswordFile = MissingFile() });

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task Unhealthy_when_nothing_listens_on_the_port()
    {
        var passwordFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(passwordFile, "not-a-real-password\n", TestContext.Current.CancellationToken);

            var result = await Check(new()
            {
                Host = IPAddress.Loopback.ToString(),
                Port = FindUnusedPort(),
                PasswordFile = passwordFile,
            });

            result.Status.Should().Be(HealthStatus.Unhealthy);
        }
        finally
        {
            File.Delete(passwordFile);
        }
    }

    [Fact]
    public async Task The_result_never_carries_the_password()
    {
        var passwordFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(passwordFile, "distinctive-test-password", TestContext.Current.CancellationToken);

            var result = await Check(new()
            {
                Host = IPAddress.Loopback.ToString(),
                Port = FindUnusedPort(),
                PasswordFile = passwordFile,
            });

            $"{result.Description} {result.Exception}".Should().NotContain("distinctive-test-password");
        }
        finally
        {
            File.Delete(passwordFile);
        }
    }

    #endregion

    #region Private Methods

    private static Task<HealthCheckResult> Check(DatabaseOptions options) =>
        new DatabaseHealthCheck(Options.Create(options)).CheckHealthAsync(new());

    private static string MissingFile() =>
        Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-missing");

    private static int FindUnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    #endregion
}
