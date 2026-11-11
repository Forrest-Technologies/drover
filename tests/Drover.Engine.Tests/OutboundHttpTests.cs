namespace Drover.Engine.Tests;

public sealed class OutboundHttpTests
{
    #region Private Fields

    private static readonly IPAddress publicAddress = IPAddress.Parse("93.184.216.34");

    private static readonly IPAddress loopback = IPAddress.Loopback;

    private static readonly IPAddress otherLoopback = IPAddress.Parse("127.0.0.2");

    #endregion

    #region Public Methods

    [Fact]
    public async Task Private_addresses_are_reachable_when_the_deployment_allows_them()
    {
        using var server = new TestWebServer(_ => TestWebServer.Ok("hello"));
        using var client = new HttpClient(OutboundHttp.CreateHandler(DeploymentPolicy.SelfHosted));

        var body = await client.GetStringAsync($"http://127.0.0.1:{server.Port}/", Token);

        body.Should().Be("hello");
    }

    [Fact]
    public async Task A_private_address_is_refused_with_a_clear_message_and_nothing_is_sent()
    {
        using var server = new TestWebServer(_ => TestWebServer.Ok("hello"));
        using var client = new HttpClient(OutboundHttp.CreateHandler(DeploymentPolicy.Hosted));

        var attempt = () => client.GetStringAsync($"http://127.0.0.1:{server.Port}/", Token);

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

        var attempt = () => client.GetStringAsync($"http://mixed.test:{server.Port}/", Token);

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

        var attempt = () => client.GetStringAsync($"http://internal.test:{server.Port}/", Token);

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

        var attempt = () => client.GetStringAsync($"http://first.test:{server.Port}/", Token);

        await attempt.Should().ThrowAsync<OutboundBlockedException>().WithMessage("*second.test*");
        server.Requests.Should().Be(1);
    }

    [Fact]
    public async Task A_redirect_to_an_allowed_address_is_followed()
    {
        using var server = new TestWebServer(request => request.Contains("first.test", StringComparison.Ordinal)
            ? TestWebServer.Redirect($"http://second.test:{Port(request)}/")
            : TestWebServer.Ok("reached the second host"));
        var addresses = new Dictionary<string, IPAddress> { ["first.test"] = loopback, ["second.test"] = otherLoopback };
        using var client = new HttpClient(OutboundHttp.CreateHandler(
            DeploymentPolicy.Hosted,
            (host, _) => Task.FromResult(new[] { addresses[host] }),
            _ => false));

        var body = await client.GetStringAsync($"http://first.test:{server.Port}/", Token);

        body.Should().Be("reached the second host");
        server.Requests.Should().Be(2);
    }

    #endregion

    #region Private Methods

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // The test server hands the request text to its responder; the port is in the Host header.
    private static string Port(string request) =>
        request.Split("Host: ")[1].Split("\r\n")[0].Split(':')[1];

    #endregion
}
