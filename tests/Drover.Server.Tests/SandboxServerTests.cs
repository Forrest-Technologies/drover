namespace Drover.Server.Tests;

/// <summary>
/// How the operator's sandbox provider key is read: from a setting or a file, held in memory only, never in an answer
/// or the log. A key that is plainly wrong stops the server and names the setting; no key means no sandboxes.
/// Every key here is random text made for the test.
/// </summary>
public sealed class SandboxKeySettingTests
{
    #region Public Methods

    [Fact]
    public void No_key_registers_no_provider_and_no_sandbox_tool_and_is_not_an_error()
    {
        using var services = Build([]);

        services.GetServices<ISandboxProvider>().Should().BeEmpty();
        services.GetServices<BuiltinTool>().Should().NotContain(tool => SandboxTools.IsSandboxTool(tool.Name));
        services.GetRequiredService<SandboxAvailability>().IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void A_key_in_the_setting_registers_the_provider_and_the_four_tools_with_the_key_held_in_memory()
    {
        var key = NewKey();

        using var services = Build(new() { [SandboxServiceExtensions.KeySetting] = key, ["Drover:Sandbox:E2B:Domain"] = "e2b.example.test" });

        services.GetRequiredService<ISandboxProvider>().Kind.Should().Be("e2b");
        services.GetServices<BuiltinTool>().Select(tool => tool.Name).Should().Contain(SandboxTools.Names);
        services.GetRequiredService<SandboxAvailability>().IsConfigured.Should().BeTrue();
        var options = services.GetRequiredService<IOptions<E2BOptions>>().Value;
        options.ApiKey.Should().Be(key);
        options.Domain.Should().Be("e2b.example.test");
    }

    [Fact]
    public void A_key_in_a_file_is_read_from_the_file()
    {
        var key = NewKey();
        var path = Path.Combine(Path.GetTempPath(), $"drover-e2b-{Guid.NewGuid():N}");
        File.WriteAllText(path, $"{key}\n");

        try
        {
            using var services = Build(new() { [SandboxServiceExtensions.KeyFileSetting] = path });

            services.GetRequiredService<IOptions<E2BOptions>>().Value.ApiKey.Should().Be(key);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(SandboxServiceExtensions.KeySetting)]
    [InlineData(SandboxServiceExtensions.KeyFileSetting)]
    public void A_key_that_is_not_an_e2b_key_stops_the_server_naming_the_setting_and_never_the_value(string setting)
    {
        var wrong = $"sk_{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}";
        var path = Path.Combine(Path.GetTempPath(), $"drover-e2b-{Guid.NewGuid():N}");
        File.WriteAllText(path, wrong);

        try
        {
            var build = () => Build(new() { [setting] = setting == SandboxServiceExtensions.KeySetting ? wrong : path });

            build.Should().ThrowExactly<InvalidOperationException>()
                .Which.Message.Should().Contain(setting).And.Contain("e2b_").And.NotContain(wrong);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_key_given_both_ways_stops_the_server()
    {
        var build = () => Build(new() { [SandboxServiceExtensions.KeySetting] = NewKey(), [SandboxServiceExtensions.KeyFileSetting] = "/run/secrets/e2b" });

        build.Should().ThrowExactly<InvalidOperationException>().Which.Message.Should().Contain("one way only");
    }

    [Fact]
    public void A_key_file_that_is_not_there_stops_the_server()
    {
        var build = () => Build(new() { [SandboxServiceExtensions.KeyFileSetting] = $"/nonexistent/{Guid.NewGuid():N}" });

        build.Should().ThrowExactly<InvalidOperationException>().Which.Message.Should().Contain(SandboxServiceExtensions.KeyFileSetting);
    }

    [Theory]
    [InlineData(DeploymentPolicy.SelfHostedName)]
    [InlineData(DeploymentPolicy.HostedName)]
    public void A_provider_with_a_hard_isolation_boundary_is_registered_under_either_preset(string preset)
    {
        DeploymentPolicy.TryGetPreset(preset, out var policy);

        using var services = Build(new() { [SandboxServiceExtensions.KeySetting] = NewKey() }, policy);

        services.GetServices<ISandboxProvider>().Should().ContainSingle();
    }

    public static string NewKey() => $"e2b_{Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant()}";

    #endregion

    #region Private Methods

    private static ServiceProvider Build(Dictionary<string, string?> settings, DeploymentPolicy? policy = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<SandboxAvailability>();
        services.AddDroverSandboxes(configuration, policy ?? DeploymentPolicy.SelfHosted);
        return services.BuildServiceProvider();
    }

    #endregion
}


/// <summary>
/// The server started with a sandbox provider key, under each preset: the provider is there, the status page says
/// only "configured", and the key appears in no answer and no log line.
/// </summary>
public abstract class SandboxServerTests(PostgresServer postgres, string preset) : ServerTestBase(postgres)
{
    #region Private Fields

    private readonly string key = SandboxKeySettingTests.NewKey();

    #endregion

    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings
    {
        get
        {
            var settings = new Dictionary<string, string?>(base.Settings)
            {
                ["DROVER_POLICY"] = preset,
                [SandboxServiceExtensions.KeySetting] = key,
            };

            foreach (var (name, value) in preset == DeploymentPolicy.HostedName ? KeyRingCertificate.Settings : [])
            {
                settings[name] = value;
            }

            return settings;
        }
    }

    #endregion

    #region Public Methods

    [Fact]
    public async Task The_server_has_the_provider_and_says_only_that_one_is_configured_and_the_key_is_in_no_answer_or_log()
    {
        var (email, password, _, spaceId) = await SeedPerson("sandboxowner");
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();

        var bodies = new List<string>();

        foreach (var path in new[] { "/api/status", $"/api/spaces/{spaceId}/status", $"/api/spaces/{spaceId}/bots/{Guid.CreateVersion7()}/sandbox/options" })
        {
            var response = await client.Get(path);
            response.StatusCode.Should().Be(HttpStatusCode.OK, path);
            bodies.Add(await response.Content.ReadAsStringAsync(Token));
        }

        Services.GetServices<ISandboxProvider>().Should().ContainSingle();
        bodies.Should().OnlyContain(body => !body.Contains(key, StringComparison.Ordinal));
        bodies.Should().OnlyContain(body => !body.Contains("e2b", StringComparison.OrdinalIgnoreCase), "no answer names the provider");
        Logs.AllText().Should().NotContain(key);

        var options = JsonSerializer.Deserialize<BotSandboxOptions>(bodies[2], JsonSerializerOptions.Web)!;
        options.Available.Should().BeTrue();

        if (preset == DeploymentPolicy.SelfHostedName)
        {
            var status = JsonSerializer.Deserialize<StatusReport>(bodies[1], JsonSerializerOptions.Web)!;
            status.Rows.Should().ContainSingle(row => row.Name == "Sandboxes").Which.Should().Match<StatusRow>(row =>
                row.Area == "server" && row.State == "ok" && row.Summary.StartsWith("Configured.", StringComparison.Ordinal));
        }
    }

    #endregion
}

public sealed class SelfHostedSandboxServerTests(PostgresServer postgres) : SandboxServerTests(postgres, DeploymentPolicy.SelfHostedName);

public sealed class HostedSandboxServerTests(PostgresServer postgres) : SandboxServerTests(postgres, DeploymentPolicy.HostedName);


/// <summary>A server without a sandbox provider key: no sandboxes, and the status page and editor say so.</summary>
public sealed class NoSandboxServerTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Public Methods

    [Fact]
    public async Task The_status_page_says_sandboxes_are_not_configured_and_the_editor_is_told_none_is_available()
    {
        var (email, password, _, spaceId) = await SeedPerson("nosandbox");
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();

        var status = (await (await client.Get($"/api/spaces/{spaceId}/status")).Content.ReadFromJsonAsync<StatusReport>(Token))!;
        var options = (await (await client.Get($"/api/spaces/{spaceId}/bots/{Guid.CreateVersion7()}/sandbox/options")).Content.ReadFromJsonAsync<BotSandboxOptions>(Token))!;

        Services.GetServices<ISandboxProvider>().Should().BeEmpty();
        status.Rows.Should().ContainSingle(row => row.Name == "Sandboxes").Which.State.Should().Be("off");
        options.Available.Should().BeFalse();
    }

    #endregion
}
