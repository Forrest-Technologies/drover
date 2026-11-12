namespace Drover.Server.Tests;

public sealed class DatabaseStartupTests
{
    #region Public Methods

    [Fact]
    public void Application_requires_configuration()
    {
        var resolve = () => DatabaseStartup.ApplicationConnection(Configuration([]));

        resolve.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("drover_owner")]
    [InlineData("")]
    public void Application_rejects_privileged_or_missing_role(string role)
    {
        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = "localhost",
            Database = "postgres",
            Username = role,
            Password = Guid.NewGuid().ToString("N"),
        };
        var configuration = Configuration(new() { ["ConnectionStrings:Drover"] = connection.ConnectionString });

        var resolve = () => DatabaseStartup.ApplicationConnection(configuration);

        resolve.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Explicit_app_connection_takes_precedence_without_reading_secret_files()
    {
        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = "localhost",
            Database = "postgres",
            Username = "drover_app",
            Password = Guid.NewGuid().ToString("N"),
        };
        var configuration = Configuration(new()
        {
            ["ConnectionStrings:Drover"] = connection.ConnectionString,
            ["Database:AppPasswordFile"] = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
        });

        DatabaseStartup.ApplicationConnection(configuration).Should().Be(connection.ConnectionString);
    }

    [Fact]
    public void Secret_file_configuration_uses_only_app_role()
    {
        var path = Path.GetTempFileName();
        try
        {
            var password = Guid.NewGuid().ToString("N");
            File.WriteAllText(path, $"{password}\n");
            var configuration = Configuration(new()
            {
                ["Database:Host"] = "db",
                ["Database:AppPasswordFile"] = path,
                ["Database:Username"] = "postgres",
            });

            var connection = new NpgsqlConnectionStringBuilder(DatabaseStartup.ApplicationConnection(configuration));

            connection.Username.Should().Be("drover_app");
            connection.Password.Should().Be(password);
            connection.Host.Should().Be("db");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n")]
    public void Empty_password_files_fail_closed(string content)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, content);
            var configuration = Configuration(new()
            {
                ["Database:Host"] = "db",
                ["Database:AppPasswordFile"] = path,
            });

            var resolve = () => DatabaseStartup.ApplicationConnection(configuration);

            resolve.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    #endregion

    #region Private Methods

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    #endregion
}
