namespace Drover.Server.Tests;

/// <summary>
/// The run-queue poll reads the database every few seconds. EF Core logs each command at Information, which buries
/// real errors, so by default only warnings from that category are written. Configuration can turn them back on.
/// </summary>
public sealed class DefaultLoggingTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Public Methods

    [Fact]
    public async Task Database_commands_are_not_logged_below_warning_by_default()
    {
        await SetUpFirstAccount();

        Logs.Entries.Should().NotBeEmpty();
        Logs.Entries.Should().NotContain(entry => entry.Category == CommandCategory && entry.Level < LogLevel.Warning);
    }

    #endregion

    #region Constants

    internal const string CommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";

    #endregion
}

/// <summary>The same setup with the category turned back up by configuration, as an operator would with an environment variable.</summary>
public sealed class DatabaseCommandLoggingOverrideTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        [$"Logging:LogLevel:{DefaultLoggingTests.CommandCategory}"] = "Information",
    };

    #endregion

    #region Public Methods

    [Fact]
    public async Task Configuration_can_turn_database_command_logging_back_on()
    {
        await SetUpFirstAccount();

        Logs.Entries.Should().Contain(entry =>
            entry.Category == DefaultLoggingTests.CommandCategory && entry.Level == LogLevel.Information);
    }

    #endregion
}
