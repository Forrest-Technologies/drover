namespace Drover.Server.Tests;

/// <summary>
/// The compose files a self-hoster runs, read as data. Each rule here is one a careless edit could break without any
/// container noticing: a privilege, a port open to the network, an image that is not pinned, a secret the serving
/// process should never see.
/// </summary>
public sealed class ComposeFileTests
{
    #region Private Fields

    private static readonly string[] allowedCapabilities = ["CHOWN", "DAC_OVERRIDE", "DAC_READ_SEARCH", "FOWNER", "SETGID", "SETUID", "NET_BIND_SERVICE"];

    private static readonly ComposeFile main = ComposeFile.Read("compose.yaml");

    private static readonly ComposeFile proxy = ComposeFile.Read("compose.proxy.yaml");

    #endregion

    #region Public Methods

    public static TheoryData<string, string> Services() =>
    [
        .. main.ServiceNames.Select(name => ("compose.yaml", name)),
        .. proxy.ServiceNames.Where(name => proxy.Service(name).Children.ContainsKey("image")).Select(name => ("compose.proxy.yaml", name)),
    ];

    [Theory]
    [MemberData(nameof(Services))]
    public void Every_service_drops_every_capability_and_cannot_gain_privileges(string file, string name)
    {
        var service = Read(file).Service(name);

        ComposeFile.Text(service, "privileged").Should().NotBe("true");
        ComposeFile.List(service, "cap_drop").Should().Contain("ALL");
        ComposeFile.List(service, "cap_add").Should().OnlyContain(capability => allowedCapabilities.Contains(capability));
        ComposeFile.List(service, "security_opt").Should().Contain("no-new-privileges:true");
        ComposeFile.Text(service, "read_only").Should().Be("true");
        ComposeFile.Text(service, "network_mode").Should().BeNull();
        ComposeFile.Text(service, "pid").Should().BeNull();
        ComposeFile.Text(service, "ipc").Should().BeNull();
        ComposeFile.List(service, "devices").Should().BeEmpty();
        ComposeFile.List(service, "volumes").Should().NotContain(volume => volume.Contains("docker.sock", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void Every_image_from_elsewhere_is_pinned_by_digest(string file, string name)
    {
        var service = Read(file).Service(name);

        if (!service.Children.ContainsKey("build"))
        {
            ComposeFile.Text(service, "image").Should().MatchRegex("@sha256:[0-9a-f]{64}$");
        }
    }

    [Fact]
    public void The_server_runs_as_its_own_non_root_user()
    {
        var user = ComposeFile.Text(main.Service("drover"), "user");

        user.Should().Be("1654:1654");
    }

    [Fact]
    public void The_serving_process_mounts_only_its_own_secret()
    {
        var volumes = ComposeFile.List(main.Service("drover"), "volumes");

        volumes.Should().Contain("drover-app-secrets:/run/secrets:ro");
        volumes.Should().NotContain(volume => volume.StartsWith("drover-secrets:", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_port_of_the_main_file_is_bound_to_this_machine_only()
    {
        var ports = main.ServiceNames.SelectMany(name => ComposeFile.List(main.Service(name), "ports"));

        ports.Should().NotBeEmpty().And.OnlyContain(port => port.StartsWith("127.0.0.1:", StringComparison.Ordinal));
    }

    [Fact]
    public void Data_secrets_keys_and_files_each_live_on_a_named_volume()
    {
        main.VolumeNames.Should().Contain(["drover-db", "drover-secrets", "drover-app-secrets", "drover-keys", "drover-files"]);
    }

    [Fact]
    public void Optional_services_stay_off_unless_their_profile_is_asked_for()
    {
        var required = main.ServiceNames.Where(name => ComposeFile.List(main.Service(name), "profiles").Count == 0);

        required.Should().BeEquivalentTo(["init", "db", "database-init", "drover"]);
    }

    [Fact]
    public void The_proxy_is_the_one_address_the_server_trusts_and_that_address_is_in_its_network()
    {
        var trusted = ComposeFile.Text(ComposeFile.Map(proxy.Service("drover"), "environment"), "Drover__Proxy__TrustedAddresses__0");
        var address = ComposeFile.Text(ComposeFile.Map(ComposeFile.Map(proxy.Service("proxy"), "networks"), "default"), "ipv4_address");
        var subnet = ComposeFile.Text(
            (YamlMappingNode)((YamlSequenceNode)ComposeFile.Map(ComposeFile.Map(proxy.Networks, "default"), "ipam").Children["config"]).Children[0],
            "subnet");

        trusted.Should().Be(address);
        var defaultAddress = IPAddress.Parse(ComposeFile.Fallback(address!));
        System.Net.IPNetwork.Parse(ComposeFile.Fallback(subnet!)).Contains(defaultAddress).Should().BeTrue();
        ComposeFile.Text(ComposeFile.Map(proxy.Service("drover"), "environment"), "DROVER_PUBLIC_URL").Should().StartWith("https://");
    }

    #endregion

    #region Private Methods

    private static ComposeFile Read(string file) => file == "compose.yaml" ? main : proxy;

    #endregion
}

/// <summary>A compose file parsed into its YAML nodes, with the few lookups the tests need.</summary>
public sealed class ComposeFile(YamlMappingNode root)
{
    #region Properties

    public IReadOnlyList<string> ServiceNames => [.. Map(root, "services").Children.Keys.Select(key => ((YamlScalarNode)key).Value!)];

    public IReadOnlyList<string> VolumeNames => [.. Map(root, "volumes").Children.Keys.Select(key => ((YamlScalarNode)key).Value!)];

    public YamlMappingNode Networks => Map(root, "networks");

    #endregion

    #region Public Methods

    public static ComposeFile Read(string name)
    {
        var stream = new YamlStream();
        using var reader = new StreamReader(Path.Combine(FindRoot(), name));
        stream.Load(reader);
        return new((YamlMappingNode)stream.Documents[0].RootNode);
    }

    public YamlMappingNode Service(string name) => Map(Map(root, "services"), name);

    public static YamlMappingNode Map(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(key, out var child) ? (YamlMappingNode)child : [];

    public static string? Text(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(key, out var child) ? ((YamlScalarNode)child).Value : null;

    public static IReadOnlyList<string> List(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(key, out var child)
            ? [.. ((YamlSequenceNode)child).Children.Select(item => ((YamlScalarNode)item).Value!)]
            : [];

    /// <summary>The value compose uses when the variable is not set: <c>${NAME:-value}</c> gives <c>value</c>.</summary>
    public static string Fallback(string text)
    {
        var marker = text.IndexOf(":-", StringComparison.Ordinal);
        return marker < 0 ? text : text[(marker + 2)..text.LastIndexOf('}')];
    }

    #endregion

    #region Private Methods

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Drover.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Drover.slnx was not found above the test output.");
    }

    #endregion
}
