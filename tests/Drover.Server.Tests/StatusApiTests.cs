namespace Drover.Server.Tests;

/// <summary>The status report: what a space's admins see of the server, and that it is the truth about the database role.</summary>
public sealed class StatusApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Tests

    [Fact]
    public async Task An_admin_sees_each_thing_that_was_checked_and_a_member_is_refused()
    {
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var admin = await SignIn(alice.Email, alice.Password);
        var asMember = await SignIn(member.Email, member.Password);

        var response = await admin.Get($"/api/spaces/{alice.SpaceId}/status");
        var status = (await response.Content.ReadFromJsonAsync<SpaceStatus>(Token))!;
        var refused = await asMember.Get($"/api/spaces/{alice.SpaceId}/status");

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        status.Server!.Select(row => row.Name).Should().Equal("database", "migrations", "background_work", "key_ring", "public_address");
        Row(status, "database").Should().Match<StatusRow>(row => row.State == "ok" && row.Summary.Contains("drover_app"));
        Row(status, "migrations").State.Should().Be("ok", Row(status, "migrations").Summary);
        Row(status, "background_work").Should().Match<StatusRow>(row => row.State == "ok" && row.Summary.StartsWith("0 queued, 0 running"));
        Row(status, "key_ring").State.Should().NotBe("failing", Row(status, "key_ring").Summary);
        Row(status, "public_address").Should().Match<StatusRow>(row => row.State == "off" && row.Summary.Contains("DROVER_PUBLIC_URL"));
    }

    [Fact]
    public async Task Ready_asks_only_whether_the_database_can_be_reached()
    {
        var response = await NewClient().Get("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a public address that is not set, or a key ring with no key yet, does not make the server unready");
    }

    #endregion

    #region Helpers

    private async Task<ApiClient> SignIn(string email, string password)
    {
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    private static StatusRow Row(SpaceStatus status, string name) => status.Server!.Single(row => row.Name == name);

    #endregion
}

/// <summary>A deployment that keeps the server's own state to its operator.</summary>
public sealed class StatusWithheldTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Overrides

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        ["Drover:Policy:Rules:AllowServerStatusInSpaces"] = "false",
    };

    #endregion

    #region Tests

    [Fact]
    public async Task With_the_rule_off_a_spaces_admin_is_told_nothing_about_the_server()
    {
        var alice = await SeedPerson("alice");
        var admin = NewClient();
        (await admin.SignIn(alice.Email, alice.Password)).EnsureSuccessStatusCode();

        var status = (await (await admin.Get($"/api/spaces/{alice.SpaceId}/status")).Content.ReadFromJsonAsync<SpaceStatus>(Token))!;

        status.Server.Should().BeNull();
    }

    #endregion
}

/// <summary>
/// A server pointed at a connection that bypasses row-level security works, and protects nothing. It must say so:
/// red on the status page, and an error in the log at start.
/// </summary>
public sealed class DatabaseRoleStatusTests(PostgresServer postgres)
{
    #region Properties

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    #endregion

    #region Tests

    [Fact]
    public async Task A_superuser_connection_turns_the_database_row_red_and_is_logged_as_an_error_at_start()
    {
        await using var database = await postgres.CreateDatabase();
        var options = Options.Create(new DatabaseOptions { ConnectionString = database.AdminConnectionString });
        using var logs = new LogCapture();
        using var loggers = LoggerFactory.Create(logging => logging.AddProvider(logs));
        var guard = new DatabaseRoleGuard(
            new ConfigurationBuilder().Build(),
            Options.Create(new DeploymentPolicyOptions { Rules = DeploymentPolicy.SelfHosted }),
            options,
            loggers.CreateLogger<DatabaseRoleGuard>());

        var result = await new DatabaseRoleHealthCheck(options).CheckHealthAsync(new HealthCheckContext(), Token);
        await guard.StartAsync(Token);

        StatusChecks.ToRow("database", new HealthReportEntry(result.Status, result.Description, TimeSpan.Zero, null, result.Data))
            .Should().Match<StatusRow>(row => row.State == "failing" && row.Summary.Contains("bypasses row-level security"));
        logs.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error)
            .Which.Text.Should().Contain("bypasses row-level security");
    }

    [Fact]
    public async Task The_application_role_is_green_and_logs_nothing()
    {
        await using var database = await postgres.CreateDatabase();
        var options = Options.Create(new DatabaseOptions { ConnectionString = database.AppConnectionString });
        using var logs = new LogCapture();
        using var loggers = LoggerFactory.Create(logging => logging.AddProvider(logs));
        var guard = new DatabaseRoleGuard(
            new ConfigurationBuilder().Build(),
            Options.Create(new DeploymentPolicyOptions { Rules = DeploymentPolicy.SelfHosted }),
            options,
            loggers.CreateLogger<DatabaseRoleGuard>());

        var result = await new DatabaseRoleHealthCheck(options).CheckHealthAsync(new HealthCheckContext(), Token);
        await guard.StartAsync(Token);

        result.Status.Should().Be(HealthStatus.Healthy);
        logs.Entries.Should().BeEmpty();
    }

    [Fact]
    public void A_role_that_is_neither_the_application_role_nor_able_to_bypass_asks_for_attention()
    {
        var result = DatabaseRoleHealthCheck.Judge(new DatabaseRole("drover_owner", IsSuperuser: false, BypassesRowLevelSecurity: false));

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    #endregion
}
