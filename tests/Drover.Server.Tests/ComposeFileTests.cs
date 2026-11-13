namespace Drover.Server.Tests;

/// <summary>
/// The hardening rules of the deployment a self-hoster runs, read from the compose files themselves: a change that
/// loosens one fails here, with no container scanner in the build.
/// </summary>
public sealed class ComposeFileTests
{
    #region Private Fields

    private const string ProxyAddress = "172.31.250.2";

    private static readonly string[] allowedCapabilities = ["CHOWN", "FOWNER", "DAC_OVERRIDE", "DAC_READ_SEARCH", "SETGID", "SETUID", "NET_BIND_SERVICE"];

    #endregion

    #region Tests

    [Theory]
    [InlineData("compose.yaml")]
    [InlineData("compose.proxy.yaml")]
    public void No_service_is_privileged_shares_the_host_or_mounts_anything_of_the_host(string file)
    {
        var compose = Read(file);

        foreach (var (name, service) in Services(compose))
        {
            Text(service, "privileged").Should().NotBe("true", $"{name} must not be privileged");
            Text(service, "network_mode").Should().NotBe("host", $"{name} must not share the host's network");
            Text(service, "pid").Should().BeNull($"{name} must not share a process namespace");
            Text(service, "ipc").Should().BeNull($"{name} must not share memory with the host");
            service.Children.Keys.Select(key => ((YamlScalarNode)key).Value).Should().NotContain(["devices", "userns_mode"], name);

            foreach (var source in List(service, "volumes").Select(volume => volume.Split(':')[0]))
            {
                Volumes(compose).Should().Contain(source, $"{name} mounts '{source}', and only a named volume may be mounted: no path of the host, and never the Docker socket");
            }
        }
    }

    [Theory]
    [InlineData("compose.yaml")]
    [InlineData("compose.proxy.yaml")]
    public void Every_service_has_a_read_only_root_no_capability_it_does_not_list_and_no_way_to_gain_one(string file)
    {
        var defined = Services(Read(file)).Where(found => found.Service.Children.ContainsKey(new YamlScalarNode("image")));

        foreach (var (name, service) in defined)
        {
            Text(service, "read_only").Should().Be("true", $"{name} has a read-only root filesystem");
            List(service, "cap_drop").Should().Equal(["ALL"], $"{name} drops every capability");
            List(service, "cap_add").Should().BeSubsetOf(allowedCapabilities, $"{name} adds back only what it needs, and nothing that reaches the host");
            List(service, "security_opt").Should().Contain("no-new-privileges:true", name);
        }
    }

    [Fact]
    public void The_server_runs_as_no_root_adds_no_capability_and_sees_only_its_own_password()
    {
        var drover = Services(Read("compose.yaml")).Single(found => found.Name == "drover").Service;

        Text(drover, "user").Should().BeNull("the image's own user is not root, and nothing here changes it");
        List(drover, "cap_add").Should().BeEmpty();
        List(drover, "volumes").Should().Contain("drover-app-secrets:/run/secrets:ro")
            .And.NotContain(volume => volume.StartsWith("drover-secrets:", StringComparison.Ordinal), "the bootstrap and owner passwords never reach the serving process");
    }

    [Fact]
    public void Every_port_of_the_default_deployment_is_bound_to_this_machine_only()
    {
        var ports = Services(Read("compose.yaml")).SelectMany(found => List(found.Service, "ports")).ToList();

        ports.Should().Equal(["127.0.0.1:8080:8080"], "nothing listens to the network until a person puts a proxy in front");
    }

    [Theory]
    [InlineData("compose.yaml")]
    [InlineData("compose.proxy.yaml")]
    public void Every_image_that_is_pulled_is_pinned_by_digest(string file)
    {
        var compose = Read(file);

        foreach (var (name, service) in Services(compose).Where(found => !found.Service.Children.ContainsKey(new YamlScalarNode("build"))))
        {
            if (Text(service, "image") is { } image)
            {
                image.Should().MatchRegex("@sha256:[0-9a-f]{64}$", $"{name} pulls an image, and a tag alone can be moved");
            }
        }
    }

    [Fact]
    public void The_proxy_example_gives_the_proxy_one_address_and_drover_believes_that_address_alone()
    {
        var compose = Read("compose.proxy.yaml");
        var services = Services(compose).ToDictionary(found => found.Name, found => found.Service);
        var proxyNetworks = (YamlMappingNode)services["proxy"].Children[new YamlScalarNode("networks")];
        var edge = (YamlMappingNode)proxyNetworks.Children[new YamlScalarNode("edge")];
        var droverEnvironment = (YamlMappingNode)services["drover"].Children[new YamlScalarNode("environment")];

        Text(edge, "ipv4_address").Should().Be(ProxyAddress);
        Text(droverEnvironment, "Drover__Proxy__TrustedAddresses__0").Should().Be(ProxyAddress, "forwarded headers are believed from the proxy and from nowhere else");
        List(services["proxy"], "command").Should().ContainInOrder("--to", "drover:8080");
        List(services["drover"], "networks").Should().BeEquivalentTo(["default", "edge"], "the server still reaches its database");
        List(services["proxy"], "ports").Should().BeEquivalentTo(["80:80", "443:443"]);
    }

    #endregion

    #region Private Methods

    private static YamlMappingNode Read(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Drover.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests run from inside the repository");
        using var reader = new StreamReader(Path.Combine(directory!.FullName, file));
        var stream = new YamlStream();

        // The files share settings through merge keys, which the plain parser leaves as they are written.
        stream.Load(new MergingParser(new Parser(reader)));
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static IEnumerable<(string Name, YamlMappingNode Service)> Services(YamlMappingNode compose) =>
        ((YamlMappingNode)compose.Children[new YamlScalarNode("services")]).Children
            .Select(entry => (((YamlScalarNode)entry.Key).Value!, (YamlMappingNode)entry.Value));

    private static HashSet<string> Volumes(YamlMappingNode compose) =>
        compose.Children.TryGetValue(new YamlScalarNode("volumes"), out var volumes)
            ? [.. ((YamlMappingNode)volumes).Children.Keys.Select(key => ((YamlScalarNode)key).Value!)]
            : [];

    private static string? Text(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? ((YamlScalarNode)value).Value : null;

    private static List<string> List(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value)
            ? [.. ((YamlSequenceNode)value).Children.Select(item => ((YamlScalarNode)item).Value!)]
            : [];

    #endregion
}
