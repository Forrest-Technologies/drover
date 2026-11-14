namespace Drover.Server.Tests;

/// <summary>
/// What the server does at start when the policy it is given cannot run: it does not start, and the message
/// names the rule. And what it does when the policy only tightens a preset.
/// </summary>
public sealed class PolicyStartupTests(PostgresServer postgres)
{
    #region Public Methods

    [Fact]
    public async Task Hosted_without_a_key_ring_certificate_does_not_start_and_names_the_rule()
    {
        var message = await StartupFailure(Hosted(withCertificate: false));

        message.Should().Contain(nameof(DeploymentPolicy.KeyRingProtection)).And.Contain("DataProtection:CertificatePath");
    }

    [Fact]
    public async Task Hosted_with_a_key_ring_certificate_starts()
    {
        await using var server = await Boot(Hosted(withCertificate: true));

        server.Services.GetRequiredService<IOptions<DeploymentPolicyOptions>>().Value.Rules
            .Should().Be(DeploymentPolicy.Hosted);
    }

    [Theory]
    [InlineData("AllowPrivateAddresses", "true")]
    [InlineData("RequireSetupCode", "false")]
    [InlineData("KeyRingProtection", "FilePermissions")]
    [InlineData("RequireEmailVerification", "false")]
    [InlineData("SpaceQuotas", "Unlimited")]
    public async Task Hosted_with_a_rule_set_looser_than_the_preset_does_not_start_and_names_the_rule(string rule, string setting)
    {
        var settings = Hosted(withCertificate: true);
        settings[$"Drover:Policy:Rules:{rule}"] = setting;

        var message = await StartupFailure(settings);

        message.Should().Contain($"{rule} is set looser than the 'hosted' preset allows");
    }

    [Theory]
    [InlineData("KeyRingProtection", "-1")]
    [InlineData("KeyRingProtection", "7")]
    [InlineData("SandboxAdapters", "-1")]
    [InlineData("AuditRetention", "99")]
    public async Task Hosted_with_an_enum_rule_that_is_not_one_of_its_values_does_not_start(string rule, string number)
    {
        var settings = Hosted(withCertificate: rule != "KeyRingProtection");
        settings[$"Drover:Policy:Rules:{rule}"] = number;

        var message = await StartupFailure(settings);

        message.Should().Contain($"{rule} is not one of the values this rule has");
    }

    [Fact]
    public async Task Hosted_with_key_ring_protection_set_to_minus_one_and_no_certificate_does_not_start()
    {
        var settings = Hosted(withCertificate: false);
        settings["Drover:Policy:Rules:KeyRingProtection"] = "-1";

        var message = await StartupFailure(settings);

        message.Should().Contain("DataProtection:CertificatePath");
    }

    [Fact]
    public async Task Self_hosted_with_an_enum_rule_that_is_not_one_of_its_values_does_not_start()
    {
        var settings = SelfHosted();
        settings["Drover:Policy:Rules:ModelProviders"] = "-1";

        var message = await StartupFailure(settings);

        message.Should().Contain("ModelProviders is not one of the values this rule has");
    }

    [Fact]
    public async Task Hosted_connected_as_a_role_that_is_not_the_application_role_does_not_start()
    {
        var settings = Hosted(withCertificate: true);

        await using var database = await postgres.CreateDatabase();
        settings["ConnectionStrings:Drover"] = database.AdminConnectionString;

        var message = await StartupFailure<InvalidOperationException>(settings);

        message.Should().Contain(nameof(DeploymentPolicy.AllowMigrationCredentialInServer)).And.Contain("drover_app");
    }

    [Fact]
    public async Task Real_program_validates_policy_before_resolving_an_allowed_custom_role()
    {
        var settings = SelfHosted();
        settings["Drover:Policy:Rules:ModelProviders"] = "-1";
        var boot = () => Boot(settings, useAdminConnection: true);

        await boot.Should().ThrowAsync<OptionsValidationException>()
            .WithMessage("*ModelProviders is not one of the values this rule has*");
    }

    [Fact]
    public async Task Hosted_real_program_rejects_a_privileged_role_before_building_the_host()
    {
        var settings = Hosted(withCertificate: true);
        var boot = () => Boot(settings, useAdminConnection: true);

        await boot.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*entry point exited without ever building an IHost*");
    }

    [Fact]
    public async Task Tightened_self_hosted_real_program_uses_the_secret_file_connection_for_role_validation()
    {
        var settings = SelfHosted();
        settings["Drover:Policy:Rules:AllowMigrationCredentialInServer"] = "false";
        await using var server = await Boot(settings, useSecretFile: true);

        var connection = server.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString;
        var role = await DatabaseRole.Inspect(connection, TestContext.Current.CancellationToken);
        role.IsApplicationRole.Should().BeTrue();
        server.Services.GetRequiredService<IOptions<DeploymentPolicyOptions>>().Value.Rules
            .AllowMigrationCredentialInServer.Should().BeFalse();
    }

    [Fact]
    public async Task Self_hosted_may_connect_as_any_role()
    {
        var settings = SelfHosted();

        await using var server = await Boot(settings, useAdminConnection: true);

        server.Services.GetRequiredService<IOptions<DeploymentPolicyOptions>>().Value.Rules
            .AllowMigrationCredentialInServer.Should().BeTrue();
    }

    [Fact]
    public async Task Self_hosted_on_a_superuser_connection_starts_but_logs_an_error_and_the_status_page_turns_red()
    {
        var logs = new LogCapture();
        await using var server = await Boot(SelfHosted(), useAdminConnection: true, logs: logs);
        var connection = server.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString;

        var row = await StatusEndpoints.CheckDatabase(connection, NullLogger.Instance, TestContext.Current.CancellationToken);

        logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Error
            && entry.Category == typeof(DatabaseRoleGuard).FullName
            && entry.Text.Contains("row-level security", StringComparison.Ordinal));
        row.State.Should().Be("problem");
        row.Summary.Should().Contain("drover_app");
    }

    [Fact]
    public async Task Self_hosted_on_the_application_role_logs_no_error_about_the_role()
    {
        var logs = new LogCapture();
        await using var server = await Boot(SelfHosted(), logs: logs);

        logs.Entries.Should().NotContain(entry => entry.Category == typeof(DatabaseRoleGuard).FullName && entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Hosted_with_the_migration_credential_in_the_server_does_not_start_and_names_the_rule()
    {
        var settings = Hosted(withCertificate: true);
        settings["ConnectionStrings:DroverOwner"] = "Host=localhost;Username=drover_owner";

        var message = await StartupFailure(settings);

        message.Should().Contain(nameof(DeploymentPolicy.AllowMigrationCredentialInServer));
    }

    [Theory]
    [InlineData("Database:Password")]
    [InlineData("Database:PasswordFile")]
    [InlineData("Database:OwnerPassword")]
    [InlineData("Database:OwnerPasswordFile")]
    public async Task Hosted_given_an_initialization_password_in_the_server_does_not_start_and_names_the_setting(string setting)
    {
        var settings = Hosted(withCertificate: true);
        settings[setting] = "given-to-the-wrong-process";

        var message = await StartupFailure(settings);

        message.Should().Contain(nameof(DeploymentPolicy.AllowMigrationCredentialInServer)).And.Contain(setting)
            .And.NotContain("given-to-the-wrong-process");
    }

    [Theory]
    [InlineData("Database:Password")]
    [InlineData("Database:OwnerPasswordFile")]
    public async Task Self_hosted_given_an_initialization_password_starts_but_logs_an_error_naming_the_setting(string setting)
    {
        var settings = SelfHosted();
        settings[setting] = "given-to-the-wrong-process";
        var logs = new LogCapture();

        await using var server = await Boot(settings, logs: logs);

        var errors = logs.Entries.Where(entry => entry.Level == LogLevel.Error).ToList();
        errors.Should().Contain(entry => entry.Text.Contains(setting, StringComparison.Ordinal)
            && entry.Text.Contains(nameof(DeploymentPolicy.AllowMigrationCredentialInServer), StringComparison.Ordinal));
        logs.Entries.Should().NotContain(entry => entry.Text.Contains("given-to-the-wrong-process", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Self_hosted_without_an_initialization_password_logs_no_error_about_one()
    {
        var logs = new LogCapture();

        await using var server = await Boot(SelfHosted(), logs: logs);

        logs.Entries.Should().NotContain(entry => entry.Level >= LogLevel.Error
            && entry.Text.Contains(nameof(DeploymentPolicy.AllowMigrationCredentialInServer), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Self_hosted_may_hold_the_migration_credential_in_the_server()
    {
        var settings = SelfHosted();
        settings["ConnectionStrings:DroverOwner"] = "Host=localhost;Username=drover_owner";

        await using var server = await Boot(settings);

        server.Services.GetRequiredService<IOptions<DeploymentPolicyOptions>>().Value.Rules
            .AllowMigrationCredentialInServer.Should().BeTrue();
    }

    [Fact]
    public async Task Hosted_with_open_registration_and_no_terms_version_does_not_start_and_names_the_rule()
    {
        var settings = Hosted(withCertificate: true);
        settings["Drover:Policy:Rules:OpenRegistration"] = "true";

        var message = await StartupFailure(settings);

        message.Should().Contain(nameof(DeploymentPolicy.RequireTermsAcceptance)).And.Contain("TermsVersion");
    }

    public static TheoryData<int> RefusedCombinations => [.. Enumerable.Range(0, PolicyMatrix.Refusals.Count)];

    [Theory]
    [MemberData(nameof(RefusedCombinations))]
    public async Task A_refused_combination_of_settings_does_not_start_and_names_what_to_change(int row)
    {
        var refusal = PolicyMatrix.Refusals[row];
        var settings = new Dictionary<string, string?> { ["DROVER_POLICY"] = refusal.Preset };

        foreach (var (rule, setting) in refusal.Rules)
        {
            settings[$"Drover:Policy:Rules:{rule}"] = setting;
        }

        var message = await StartupFailure(settings);

        foreach (var name in refusal.Names)
        {
            message.Should().Contain(name);
        }
    }

    [Fact]
    public async Task Self_hosted_with_open_registration_starts_once_private_addresses_are_forbidden()
    {
        var settings = SelfHosted();
        settings["Drover:Policy:Rules:OpenRegistration"] = "true";
        settings["Drover:Policy:Rules:AllowPrivateAddresses"] = "false";

        await using var server = await Boot(settings);

        var rules = server.Services.GetRequiredService<IOptions<DeploymentPolicyOptions>>().Value.Rules;
        rules.OpenRegistration.Should().BeTrue();
        rules.AllowPrivateAddresses.Should().BeFalse();
    }

    [Fact]
    public async Task A_key_that_names_no_rule_does_not_start()
    {
        var settings = SelfHosted();
        settings["Drover:Policy:Rules:AllowEverything"] = "true";

        var message = await StartupFailure<InvalidOperationException>(settings);

        message.Should().Contain("AllowEverything");
    }

    [Theory]
    [InlineData("AllowEverything", "true")]
    [InlineData("AllowPrivateAddresses", "not-a-boolean")]
    [InlineData("KeyRingProtection", "not-a-protection")]
    public void Invalid_rule_configuration_is_rejected_when_options_are_resolved(string rule, string setting)
    {
        var settings = SelfHosted();
        settings[$"Drover:Policy:Rules:{rule}"] = setting;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddDroverPolicy(configuration);
        using var provider = services.BuildServiceProvider();
        var resolve = () => provider.GetRequiredService<IOptions<DeploymentPolicyOptions>>().Value;

        resolve.Should().ThrowExactly<InvalidOperationException>().Which.Message.Should().Contain(rule);
    }

    [Fact]
    public async Task A_preset_that_does_not_exist_does_not_start()
    {
        var settings = SelfHosted();
        settings["DROVER_POLICY"] = "Hosted";

        var message = await StartupFailure(settings);

        message.Should().Contain("DROVER_POLICY").And.Contain("'self_hosted' or 'hosted'");
    }

    [Fact]
    public async Task No_preset_means_self_hosted()
    {
        var settings = new Dictionary<string, string?>();

        await using var server = await Boot(settings);
        var policy = server.Services.GetRequiredService<IOptions<DeploymentPolicyOptions>>().Value;

        policy.Preset.Should().Be(DeploymentPolicy.SelfHostedName);
        policy.Rules.Should().Be(DeploymentPolicy.SelfHosted);
    }

    [Fact]
    public async Task Self_hosted_can_tighten_a_rule_and_the_server_enforces_the_tighter_value()
    {
        var settings = SelfHosted();
        settings["Drover:Policy:Rules:AllowPrivateAddresses"] = "false";
        await using var server = await Boot(settings);
        using var target = new TestWebServer(_ => TestWebServer.Ok("reached"));
        var http = server.Services.GetRequiredService<IHttpClientFactory>().CreateClient();

        var attempt = async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{target.Port}/")
                .ForSpace(new EffectivePolicy(AllowPrivateAddresses: true, TaintedRunEscalation: true, AutoLoadRemoteImages: false));
            return await http.SendAsync(request, TestContext.Current.CancellationToken);
        };

        await attempt.Should().ThrowAsync<OutboundBlockedException>();
        target.Requests.Should().Be(0);
    }

    #endregion

    #region Private Methods

    private static Dictionary<string, string?> SelfHosted() => new() { ["DROVER_POLICY"] = DeploymentPolicy.SelfHostedName };

    private static Dictionary<string, string?> Hosted(bool withCertificate)
    {
        var settings = new Dictionary<string, string?> { ["DROVER_POLICY"] = DeploymentPolicy.HostedName };

        foreach (var (key, value) in withCertificate ? KeyRingCertificate.Settings : [])
        {
            settings[key] = value;
        }

        return settings;
    }

    private static Task<string> StartupFailure(Dictionary<string, string?> settings) =>
        StartupFailure<OptionsValidationException>(settings);

    private static async Task<string> StartupFailure<TException>(Dictionary<string, string?> settings)
        where TException : Exception
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddDroverPolicy(builder.Configuration);
        using var host = builder.Build();
        var start = () => host.StartAsync(TestContext.Current.CancellationToken);

        var failure = await start.Should().ThrowExactlyAsync<TException>();

        return failure.Which.Message;
    }

    private async Task<RunningServer> Boot(
        Dictionary<string, string?> settings,
        bool useAdminConnection = false,
        bool useSecretFile = false,
        LogCapture? logs = null)
    {
        var database = await postgres.CreateDatabase();

        if (useAdminConnection)
        {
            settings["ConnectionStrings:Drover"] = database.AdminConnectionString;
        }

        var keyRing = Path.Combine(Path.GetTempPath(), $"drover-keys-{Guid.NewGuid():N}");
        if (useSecretFile)
        {
            Directory.CreateDirectory(keyRing);
            var connection = new NpgsqlConnectionStringBuilder(database.AppConnectionString);
            var passwordFile = Path.Combine(keyRing, "database-password");
            await File.WriteAllTextAsync(passwordFile, connection.Password, TestContext.Current.CancellationToken);
            settings["Database:Host"] = connection.Host;
            settings["Database:Port"] = connection.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            settings["Database:Database"] = connection.Database;
            settings["Database:AppPasswordFile"] = passwordFile;
        }

        var factory = new DroverFactory(database, keyRing, logs ?? new LogCapture(), new AdjustableTimeProvider(), new HashCounter(), settings, useAppConnection: !useSecretFile);
        var server = new RunningServer(factory, database, keyRing);

        try
        {
            _ = factory.Services;
            return server;
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }
    }

    #endregion

    #region Nested Types

    private sealed class RunningServer(DroverFactory factory, TestDatabase database, string keyRing) : IAsyncDisposable
    {
        #region Properties

        public IServiceProvider Services => factory.Services;

        #endregion

        #region Interface Implementations

        public async ValueTask DisposeAsync()
        {
            await factory.DisposeAsync();
            await database.DisposeAsync();

            if (Directory.Exists(keyRing))
            {
                Directory.Delete(keyRing, recursive: true);
            }
        }

        #endregion
    }

    #endregion
}
