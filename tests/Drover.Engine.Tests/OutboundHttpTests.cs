namespace Drover.Engine.Tests;

public sealed class OutboundHttpTests
{
    #region Private Fields

    private static readonly IPAddress publicAddress = IPAddress.Parse("93.184.216.34");

    private static readonly IPAddress loopback = IPAddress.Loopback;

    private static readonly IPAddress otherLoopback = IPAddress.Parse("127.0.0.2");

    private static readonly EffectivePolicy everythingAllowed = new(true, true, false);

    private static readonly EffectivePolicy privateForbidden = new(false, true, false);

    #endregion

    #region Public Methods

    [Fact]
    public async Task Private_addresses_are_reachable_when_the_deployment_and_the_space_allow_them()
    {
        using var server = new TestWebServer(_ => TestWebServer.Ok("hello"));
        using var client = new HttpClient(OutboundHttp.CreateHandler(DeploymentPolicy.SelfHosted));

        var body = await Get(client, $"http://127.0.0.1:{server.Port}/", everythingAllowed);

        body.Should().Be("hello");
    }

    [Fact]
    public async Task A_space_that_narrows_the_rule_is_refused_a_private_address_under_a_permissive_deployment()
    {
        using var server = new TestWebServer(_ => TestWebServer.Ok("hello"));
        using var client = new HttpClient(OutboundHttp.CreateHandler(DeploymentPolicy.SelfHosted));

        var attempt = () => Get(client, $"http://127.0.0.1:{server.Port}/", privateForbidden);

        await attempt.Should().ThrowAsync<OutboundBlockedException>();
        server.Requests.Should().Be(0);
    }

    [Fact]
    public async Task A_request_that_does_not_say_what_its_space_allows_is_treated_as_forbidden()
    {
        using var server = new TestWebServer(_ => TestWebServer.Ok("hello"));
        using var client = new HttpClient(OutboundHttp.CreateHandler(DeploymentPolicy.SelfHosted));

        var attempt = () => client.GetStringAsync($"http://127.0.0.1:{server.Port}/", Token);

        await attempt.Should().ThrowAsync<OutboundBlockedException>();
        server.Requests.Should().Be(0);
    }

    [Fact]
    public async Task A_forbidden_request_cannot_reuse_a_connection_an_allowed_request_opened()
    {
        using var server = new TestWebServer(_ => TestWebServer.Ok("hello").Replace("Connection: close", "Connection: keep-alive", StringComparison.Ordinal));
        using var client = new HttpClient(OutboundHttp.CreateHandler(DeploymentPolicy.SelfHosted));
        var url = $"http://127.0.0.1:{server.Port}/";

        await Get(client, url, everythingAllowed);
        var attempt = () => Get(client, url, privateForbidden);

        await attempt.Should().ThrowAsync<OutboundBlockedException>();
    }

    [Fact]
    public async Task A_private_address_is_refused_with_a_clear_message_and_nothing_is_sent()
    {
        using var server = new TestWebServer(_ => TestWebServer.Ok("hello"));
        using var client = new HttpClient(OutboundHttp.CreateHandler(DeploymentPolicy.Hosted));

        var attempt = () => Get(client, $"http://127.0.0.1:{server.Port}/", everythingAllowed);

        var failure = await attempt.Should().ThrowAsync<OutboundBlockedException>();
        failure.Which.Message.Should().Contain("does not allow requests to private addresses").And.Contain("127.0.0.1");
        server.Requests.Should().Be(0);
    }

    [Fact]
    public async Task A_host_name_is_resolved_and_one_private_address_among_several_refuses_the_request()
    {
        using var server = new TestWebServer(_ => TestWebServer.Ok("hello"));
        using var client = new HttpClient(OutboundHttp.CreateHandler(
            DeploymentPolicy.Hosted,
            (_, _) => Task.FromResult(new[] { publicAddress, loopback }),
            PrivateAddresses.Contains));

        var attempt = () => Get(client, $"http://mixed.test:{server.Port}/", everythingAllowed);

        await attempt.Should().ThrowAsync<OutboundBlockedException>().WithMessage("*mixed.test*");
        server.Requests.Should().Be(0);
    }

    [Fact]
    public async Task A_host_name_that_resolves_to_a_private_address_is_refused()
    {
        using var server = new TestWebServer(_ => TestWebServer.Ok("hello"));
        using var client = new HttpClient(OutboundHttp.CreateHandler(
            DeploymentPolicy.Hosted,
            (_, _) => Task.FromResult(new[] { loopback }),
            PrivateAddresses.Contains));

        var attempt = () => Get(client, $"http://internal.test:{server.Port}/", everythingAllowed);

        await attempt.Should().ThrowAsync<OutboundBlockedException>();
    }

    [Fact]
    public async Task A_redirect_to_a_refused_address_is_refused_even_though_the_first_address_was_fine()
    {
        using var server = new TestWebServer(request => request.Contains("first.test", StringComparison.Ordinal)
            ? TestWebServer.Redirect($"http://second.test:{Port(request)}/")
            : TestWebServer.Ok("reached the second host"));
        var addresses = new Dictionary<string, IPAddress> { ["first.test"] = loopback, ["second.test"] = otherLoopback };
        using var client = new HttpClient(OutboundHttp.CreateHandler(
            DeploymentPolicy.Hosted,
            (host, _) => Task.FromResult(new[] { addresses[host] }),
            address => address.Equals(otherLoopback)));

        var attempt = () => Get(client, $"http://first.test:{server.Port}/", everythingAllowed);

        await attempt.Should().ThrowAsync<OutboundBlockedException>().WithMessage("*second.test*");
        server.Requests.Should().Be(1);
    }

    [Fact]
    public async Task A_redirect_to_an_allowed_address_is_followed()
    {
        using var server = new TestWebServer(request => request.Contains("first.test", StringComparison.Ordinal)
            ? TestWebServer.Redirect($"http://second.test:{Port(request)}/")
            : TestWebServer.Ok("reached the second host"));
        List<string> resolvedHosts = [];
        using var client = new HttpClient(OutboundHttp.CreateHandler(
            DeploymentPolicy.Hosted,
            (host, _) =>
            {
                resolvedHosts.Add(host);
                return Task.FromResult<IPAddress[]>([loopback]);
            },
            _ => false));

        var body = await Get(client, $"http://first.test:{server.Port}/", everythingAllowed);

        body.Should().Be("reached the second host");
        server.Requests.Should().Be(2);
        resolvedHosts.Should().Equal("first.test", "second.test");
    }

    [Theory]
    [InlineData(3, 0)]
    [InlineData(4, 0)]
    public async Task HTTP_3_and_above_is_refused_because_it_would_skip_the_connection_check(int major, int minor)
    {
        using var server = new TestWebServer(_ => TestWebServer.Ok("hello"));
        using var client = new HttpClient(OutboundHttp.CreateHandler(DeploymentPolicy.SelfHosted));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{server.Port}/")
        {
            Version = new Version(major, minor),
        }.ForSpace(everythingAllowed);

        var attempt = () => client.SendAsync(request, Token);

        await attempt.Should().ThrowAsync<OutboundBlockedException>().WithMessage("*HTTP/3*");
        server.Requests.Should().Be(0);
    }

    [Fact]
    public async Task A_request_is_not_left_free_to_upgrade_to_HTTP_3()
    {
        using var server = new TestWebServer(_ => TestWebServer.Ok("hello"));
        using var client = new HttpClient(OutboundHttp.CreateHandler(DeploymentPolicy.SelfHosted));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{server.Port}/")
        {
            VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
        }.ForSpace(everythingAllowed);

        await client.SendAsync(request, Token);

        request.VersionPolicy.Should().Be(HttpVersionPolicy.RequestVersionOrLower);
    }

    #endregion

    #region Private Methods

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<string> Get(HttpClient client, string url, EffectivePolicy space)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url).ForSpace(space);
        using var response = await client.SendAsync(request, Token);
        return await response.Content.ReadAsStringAsync(Token);
    }

    // The test server hands the request text to its responder; the port is in the Host header.
    private static string Port(string request) =>
        request.Split("Host: ")[1].Split("\r\n")[0].Split(':')[1];

    #endregion
}
