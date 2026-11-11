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
        var start = () => Boot(Hosted(withCertificate: true));

        await start.Should().NotThrowAsync();
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

    [Fact]
    public async Task Hosted_with_the_migration_credential_in_the_server_does_not_start_and_names_the_rule()
    {
        var settings = Hosted(withCertificate: true);
        settings["ConnectionStrings:DroverOwner"] = "Host=localhost;Username=drover_owner";

        var message = await StartupFailure(settings);

        message.Should().Contain(nameof(DeploymentPolicy.AllowMigrationCredentialInServer));
    }

    [Fact]
    public async Task Self_hosted_may_hold_the_migration_credential_in_the_server()
    {
        var settings = SelfHosted();
        settings["ConnectionStrings:DroverOwner"] = "Host=localhost;Username=drover_owner";

        var start = () => Boot(settings);

        await start.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Hosted_with_open_registration_and_no_terms_version_does_not_start_and_names_the_rule()
    {
        var settings = Hosted(withCertificate: true);
        settings["Drover:Policy:Rules:OpenRegistration"] = "true";

        var message = await StartupFailure(settings);

        message.Should().Contain(nameof(DeploymentPolicy.RequireTermsAcceptance)).And.Contain("TermsVersion");
    }

    [Fact]
    public async Task A_key_that_names_no_rule_does_not_start()
    {
        var settings = SelfHosted();
        settings["Drover:Policy:Rules:AllowEverything"] = "true";

        var message = await StartupFailure(settings);

        message.Should().Contain("AllowEverything");
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

        var attempt = () => http.GetStringAsync($"http://127.0.0.1:{target.Port}/", TestContext.Current.CancellationToken);

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

    private async Task<string> StartupFailure(Dictionary<string, string?> settings)
    {
        try
        {
            await using var server = await Boot(settings);
        }
        catch (Exception failure)
        {
            return failure.ToString();
        }

        throw new InvalidOperationException("The server started, and it should not have.");
    }

    private async Task<RunningServer> Boot(Dictionary<string, string?> settings)
    {
        var database = await postgres.CreateDatabase();
        var keyRing = Path.Combine(Path.GetTempPath(), $"drover-keys-{Guid.NewGuid():N}");
        var factory = new DroverFactory(database, keyRing, new LogCapture(), new AdjustableTimeProvider(), new HashCounter(), settings);
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
        public IServiceProvider Services => factory.Services;

        public async ValueTask DisposeAsync()
        {
            await factory.DisposeAsync();
            await database.DisposeAsync();

            if (Directory.Exists(keyRing))
            {
                Directory.Delete(keyRing, recursive: true);
            }
        }
    }

    #endregion
}
