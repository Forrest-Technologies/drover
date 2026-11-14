namespace Drover.Server.Tests;

/// <summary>The matrix itself: it names every rule once, and states what each preset sets.</summary>
public sealed class PolicyMatrixCoverageTests
{
    #region Public Methods

    [Fact]
    public void Every_rule_of_the_policy_has_a_row_in_the_matrix()
    {
        var rules = typeof(DeploymentPolicy)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name);

        PolicyMatrix.Rows.Select(row => row.Rule).Should().BeEquivalentTo(rules);
    }

    [Fact]
    public void The_matrix_states_what_the_two_presets_really_set()
    {
        foreach (var rule in DeploymentPolicy.Rules)
        {
            var row = PolicyMatrix.Rows.Single(candidate => candidate.Rule == rule.Name);

            rule.ReadText(DeploymentPolicy.SelfHosted).Should().Be(row.SelfHosted, $"{rule.Name} under self_hosted");
            rule.ReadText(DeploymentPolicy.Hosted).Should().Be(row.Hosted, $"{rule.Name} under hosted");
        }
    }

    #endregion
}

/// <summary>
/// The server started under one preset, asked each question the preset answers. The two subclasses run every test
/// once per preset, and each test reads what to expect from <see cref="PolicyMatrix"/>.
/// </summary>
public abstract class PolicyMatrixTests(PostgresServer postgres, string preset) : ServerTestBase(postgres)
{
    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings
    {
        get
        {
            var settings = new Dictionary<string, string?>(base.Settings) { ["DROVER_POLICY"] = preset };

            foreach (var (key, value) in preset == DeploymentPolicy.HostedName ? KeyRingCertificate.Settings : [])
            {
                settings[key] = value;
            }

            return settings;
        }
    }

    #endregion

    #region Public Methods

    [Fact]
    public async Task The_status_endpoint_lists_the_preset_and_every_rule_with_its_setting()
    {
        var client = await SignedInClient();

        var status = await (await client.Get("/api/status")).Content.ReadFromJsonAsync<PolicyStatus>(Token);

        status!.Preset.Should().Be(preset);
        status.Rules.Select(rule => (rule.Rule, rule.Setting))
            .Should().BeEquivalentTo(PolicyMatrix.Rows.Select(row => (row.Rule, row.For(preset))));
        status.Rules.Should().OnlyContain(rule => rule.EnforcedBy != string.Empty);
    }

    [Fact]
    public async Task The_status_endpoint_needs_a_signed_in_person()
    {
        var client = NewClient();

        var response = await client.Get("/api/status");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AllowPrivateAddresses_decides_whether_an_outbound_client_may_reach_a_private_address()
    {
        using var target = new TestWebServer(_ => TestWebServer.Ok("reached"));
        var http = Services.GetRequiredService<IHttpClientFactory>().CreateClient();
        var attempt = async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{target.Port}/")
                .ForSpace(new EffectivePolicy(AllowPrivateAddresses: true, TaintedRunEscalation: true, AutoLoadRemoteImages: false));
            using var response = await http.SendAsync(request, Token);
            return await response.Content.ReadAsStringAsync(Token);
        };

        if (Setting(nameof(DeploymentPolicy.AllowPrivateAddresses)) == "true")
        {
            (await attempt()).Should().Be("reached");
        }
        else
        {
            await attempt.Should().ThrowAsync<OutboundBlockedException>().WithMessage("*does not allow requests to private addresses*");
            target.Requests.Should().Be(0);
        }
    }

    [Fact]
    public async Task RequireSetupCode_decides_whether_first_run_setup_needs_the_code()
    {
        var required = Setting(nameof(DeploymentPolicy.RequireSetupCode)) == "true";
        var local = NewClient("127.0.0.1");

        var status = await (await local.Get("/api/setup")).Content.ReadFromJsonAsync<SetupStatus>(Token);
        var withoutCode = await local.Post("/api/setup", new SetupRequest("o@example.test", "Owner", NewPassword(), null));
        var withCode = await local.Post(
            "/api/setup",
            new SetupRequest("o@example.test", "Owner", NewPassword(), Services.GetRequiredService<SetupCode>().Display));

        status!.RequiresCode.Should().Be(required);
        withoutCode.StatusCode.Should().Be(required ? HttpStatusCode.Forbidden : HttpStatusCode.Created);
        withCode.StatusCode.Should().Be(required ? HttpStatusCode.Created : HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OpenRegistration_is_off_in_both_presets_until_the_operator_turns_it_on()
    {
        var beforeSetup = NewClient("127.0.0.1");
        var statusBefore = await beforeSetup.Get("/api/register");
        var client = await SignedInClient();

        var status = await client.Get("/api/register");
        var attempt = await client.Post("/api/register", new RegisterRequest("new@example.test", "New", NewPassword(), "any"));

        Setting(nameof(DeploymentPolicy.OpenRegistration)).Should().Be("false");
        statusBefore.StatusCode.Should().Be(HttpStatusCode.NotFound);
        status.StatusCode.Should().Be(HttpStatusCode.NotFound);
        attempt.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task KeyRingProtection_decides_whether_the_stored_keys_are_encrypted_with_a_certificate()
    {
        var (_, _, userId, spaceId) = await SeedPerson("keyed");
        await SeedSecret(spaceId, userId, "a-secret-for-the-key-ring-test");
        var keys = Directory.GetFiles(KeyRingPath, "key-*.xml").Select(File.ReadAllText).ToList();

        keys.Should().NotBeEmpty();

        if (Setting(nameof(DeploymentPolicy.KeyRingProtection)) == nameof(KeyRingProtection.CertificateOrKeyService))
        {
            keys.Should().OnlyContain(key => key.Contains("<encryptedSecret", StringComparison.Ordinal)
                && !key.Contains("<masterKey", StringComparison.Ordinal));
        }
        else
        {
            keys.Should().OnlyContain(key => key.Contains("<masterKey", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task SpaceAdminsSeeServerStatus_decides_whether_a_space_owner_sees_the_server_rows()
    {
        var (email, password, _, spaceId) = await SeedPerson("statusowner");
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();

        var report = (await (await client.Get($"/api/spaces/{spaceId}/status")).Content.ReadFromJsonAsync<StatusReport>(Token))!;

        var shown = Setting(nameof(DeploymentPolicy.SpaceAdminsSeeServerStatus)) == "true";
        report.ServerRowsShown.Should().Be(shown);
        report.Rows.Any(row => row.Area == "server").Should().Be(shown);
    }

    [Fact]
    public async Task AllowWrittenRulePatterns_decides_whether_a_rule_may_look_for_text_with_a_written_pattern()
    {
        var (email, password, _, spaceId) = await SeedPerson("ruleowner");
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();

        // The answer is the server's, the same for every bot, so no bot has to exist to ask.
        var options = (await (await client.Get($"/api/spaces/{spaceId}/bots/{Guid.CreateVersion7()}/rules/options")).Content.ReadFromJsonAsync<BotRuleOptions>(Token))!;

        options.WrittenPatterns.Should().Be(Setting(nameof(DeploymentPolicy.AllowWrittenRulePatterns)) == "true");
    }

    [Fact]
    public async Task The_sandbox_rules_decide_what_the_bot_editor_offers_and_what_a_bot_may_save()
    {
        var (email, password, _, spaceId) = await SeedPerson("sandboxrules");
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();

        var options = (await (await client.Get($"/api/spaces/{spaceId}/bots/{Guid.CreateVersion7()}/sandbox/options")).Content.ReadFromJsonAsync<BotSandboxOptions>(Token))!;

        options.OpenNetwork.Should().Be(Setting(nameof(DeploymentPolicy.AllowOpenSandboxNetwork)) == "true");
        options.Desktop.Should().Be(Setting(nameof(DeploymentPolicy.AllowDesktopSandboxes)) == "true");
        options.MaxIdleMinutes.Should().Be(int.Parse(Setting(nameof(DeploymentPolicy.MaxSandboxIdleMinutes)), CultureInfo.InvariantCulture));
        var rules = Services.GetRequiredService<IOptions<DeploymentPolicyOptions>>().Value.Rules;
        rules.MaxSandboxCommandSeconds.ToString(CultureInfo.InvariantCulture).Should().Be(Setting(nameof(DeploymentPolicy.MaxSandboxCommandSeconds)));
        rules.MaxSandboxFileBytes.ToString(CultureInfo.InvariantCulture).Should().Be(Setting(nameof(DeploymentPolicy.MaxSandboxFileBytes)));
    }

    #endregion

    #region Private Methods

    private string Setting(string rule) => PolicyMatrix.Setting(rule, preset);

    private async Task<ApiClient> SignedInClient()
    {
        var (email, password, _, _) = await SeedPerson("signedin");
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    #endregion
}

public sealed class SelfHostedPolicyMatrixTests(PostgresServer postgres)
    : PolicyMatrixTests(postgres, DeploymentPolicy.SelfHostedName);

public sealed class HostedPolicyMatrixTests(PostgresServer postgres)
    : PolicyMatrixTests(postgres, DeploymentPolicy.HostedName);
