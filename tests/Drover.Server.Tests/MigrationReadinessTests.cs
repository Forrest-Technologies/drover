namespace Drover.Server.Tests;

/// <summary>
/// A deploy that skips the one-off <c>--initialize-database</c> step starts a build whose schema the database does
/// not have. Readiness keeps traffic off it, and the log says what to run. The database is made one migration behind
/// (or ahead) by changing EF Core's history, as a skipped or rolled-back deploy would leave it.
/// </summary>
public sealed class MigrationReadinessTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Public Methods

    [Fact]
    public async Task Ready_and_healthy_when_the_database_has_every_migration_of_the_build()
    {
        var result = await Check();
        var ready = await NewClient().Get("/health/ready");

        result.Status.Should().Be(HealthStatus.Healthy);
        ready.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Not_ready_when_the_database_is_one_migration_behind_and_the_description_names_the_step_to_run()
    {
        await RemoveNewestMigration();

        var result = await Check();
        var ready = await NewClient().Get("/health/ready");

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be(
            "The database's migrations are behind this build. Run the --initialize-database step (a one-off run of this image with that command); this server is ready once it has finished.");
        ready.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task The_description_carries_no_connection_detail_secret_or_migration_name()
    {
        await RemoveNewestMigration();
        var password = new NpgsqlConnectionStringBuilder(AppConnectionString).Password!;

        var result = await Check();

        result.Description.Should().NotContain(password).And.NotContain("drover_app").And.NotMatchRegex("[0-9]{14}_");
    }

    [Fact]
    public async Task Not_ready_when_the_database_is_ahead_of_the_build_and_the_description_says_to_deploy_the_newer_build()
    {
        await Execute("""INSERT INTO "__EFMigrationsHistory" (migration_id, product_version) VALUES ('99991231235959_FromANewerBuild', '0')""");

        var result = await Check();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("newer than this build").And.Contain("Deploy the build that carries them");
    }

    [Fact]
    public async Task Liveness_stays_healthy_while_the_database_is_behind()
    {
        await RemoveNewestMigration();

        var live = await NewClient().Get("/health/live");

        live.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Start_logs_one_error_naming_what_to_run_when_the_database_is_behind_and_the_server_still_starts()
    {
        await RemoveNewestMigration();

        var live = await NewClient().Get("/health/live");

        live.StatusCode.Should().Be(HttpStatusCode.OK);
        var errors = Logs.Entries.Where(entry => entry.Category == typeof(MigrationStartupCheck).FullName).ToList();
        errors.Should().ContainSingle().Which.Should().Match<LogEntry>(entry =>
            entry.Level == LogLevel.Error && entry.Text.Contains("--initialize-database", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Start_logs_nothing_about_migrations_when_the_database_is_up_to_date()
    {
        await NewClient().Get("/health/live");

        Logs.Entries.Should().NotContain(entry => entry.Category == typeof(MigrationStartupCheck).FullName);
    }

    #endregion

    #region Private Methods

    private async Task<HealthReport> Report()
    {
        _ = NewClient();
        return await Services.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Name == "migrations", Token);
    }

    private async Task<HealthReportEntry> Check() => (await Report()).Entries["migrations"];

    private Task RemoveNewestMigration() =>
        Execute("""DELETE FROM "__EFMigrationsHistory" WHERE migration_id = (SELECT max(migration_id) FROM "__EFMigrationsHistory")""");

    private async Task Execute(string sql)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync(Token);
        await new NpgsqlCommand(sql, connection).ExecuteNonQueryAsync(Token);
    }

    #endregion
}
