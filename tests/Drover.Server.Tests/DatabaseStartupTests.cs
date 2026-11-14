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

    [Fact]
    public void An_app_password_given_as_a_value_is_used_like_the_file_form()
    {
        var password = Guid.NewGuid().ToString("N");
        var configuration = Configuration(new()
        {
            ["Database:Host"] = "db",
            ["Database:AppPassword"] = $"{password}\n",
            ["Database:Username"] = "postgres",
        });

        var connection = new NpgsqlConnectionStringBuilder(DatabaseStartup.ApplicationConnection(configuration));

        connection.Username.Should().Be("drover_app");
        connection.Password.Should().Be(password);
        connection.Host.Should().Be("db");
    }

    [Fact]
    public void A_password_given_both_as_a_value_and_as_a_file_is_refused_without_echoing_it()
    {
        var path = Path.GetTempFileName();
        try
        {
            var password = Guid.NewGuid().ToString("N");
            File.WriteAllText(path, password);
            var configuration = Configuration(new()
            {
                ["Database:Host"] = "db",
                ["Database:AppPassword"] = password,
                ["Database:AppPasswordFile"] = path,
            });

            var resolve = () => DatabaseStartup.ApplicationConnection(configuration);

            resolve.Should().Throw<InvalidOperationException>()
                .WithMessage("*Database:AppPassword*Database:AppPasswordFile*")
                .Which.ToString().Should().NotContain(password);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(" \n")]
    public void A_blank_password_value_fails_closed(string value)
    {
        var configuration = Configuration(new()
        {
            ["Database:Host"] = "db",
            ["Database:AppPassword"] = value,
        });

        var resolve = () => DatabaseStartup.ApplicationConnection(configuration);

        resolve.Should().Throw<InvalidOperationException>().WithMessage("*Database:AppPassword*");
    }

    [Fact]
    public void Initialization_takes_every_password_as_a_value()
    {
        var (admin, owner, app) = (NewSecret(), NewSecret(), NewSecret());
        var configuration = Configuration(new()
        {
            ["Database:Host"] = "db",
            ["Database:Password"] = admin,
            ["Database:OwnerPassword"] = owner,
            ["Database:AppPassword"] = app,
        });

        var connections = DatabaseStartup.InitializationConnections(configuration);

        (connections.Admin.Username, connections.Admin.Password).Should().Be(("postgres", admin));
        (connections.Owner.Username, connections.Owner.Password).Should().Be(("drover_owner", owner));
        (connections.App.Username, connections.App.Password).Should().Be(("drover_app", app));
    }

    [Fact]
    public void Connections_use_tls_when_the_server_offers_it_unless_told_otherwise()
    {
        var configuration = Configuration(new()
        {
            ["Database:Host"] = "db",
            ["Database:Password"] = NewSecret(),
            ["Database:OwnerPassword"] = NewSecret(),
            ["Database:AppPassword"] = NewSecret(),
        });

        var app = new NpgsqlConnectionStringBuilder(DatabaseStartup.ApplicationConnection(configuration));
        var initialization = DatabaseStartup.InitializationConnections(configuration);

        app.SslMode.Should().Be(SslMode.Prefer);
        initialization.Admin.SslMode.Should().Be(SslMode.Prefer);
    }

    [Theory]
    [InlineData("Require", SslMode.Require)]
    [InlineData("VerifyFull", SslMode.VerifyFull)]
    public void Tls_can_be_required_for_every_connection(string setting, SslMode expected)
    {
        var configuration = Configuration(new()
        {
            ["Database:Host"] = "db",
            ["Database:SslMode"] = setting,
            ["Database:Password"] = NewSecret(),
            ["Database:OwnerPassword"] = NewSecret(),
            ["Database:AppPassword"] = NewSecret(),
        });

        var app = new NpgsqlConnectionStringBuilder(DatabaseStartup.ApplicationConnection(configuration));
        var initialization = DatabaseStartup.InitializationConnections(configuration);

        NpgsqlConnectionStringBuilder[] all = [app, initialization.Admin, initialization.Owner, initialization.App];
        all.Should().OnlyContain(connection => connection.SslMode == expected);
    }

    [Fact]
    public void Initialization_without_the_owner_password_names_both_ways_to_give_it()
    {
        var configuration = Configuration(new()
        {
            ["Database:Host"] = "db",
            ["Database:Password"] = NewSecret(),
            ["Database:AppPassword"] = NewSecret(),
        });

        var resolve = () => DatabaseStartup.InitializationConnections(configuration);

        resolve.Should().Throw<InvalidOperationException>().WithMessage("*Database:OwnerPassword*Database:OwnerPasswordFile*");
    }

    #endregion

    #region Private Methods

    private static string NewSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    #endregion
}
