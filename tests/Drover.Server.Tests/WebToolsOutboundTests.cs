namespace Drover.Server.Tests;

/// <summary>
/// Verifies the HTTP clients wired by <see cref="WebServiceExtensions.AddDroverWebTools"/> never follow redirects or
/// share cookies across requests, including when the configured search service sits on a private address.
/// </summary>
public sealed class WebToolsOutboundTests
{
    #region Public Methods

    [Fact]
    public async Task Search_does_not_follow_a_redirect_to_another_private_origin_even_in_a_self_hosted_deployment()
    {
        using var targetServer = new TestWebServer(_ => TestWebServer.Ok("""{"results":[]}"""));
        using var searchServer = new TestWebServer(_ =>
            TestWebServer.Redirect($"http://127.0.0.1:{targetServer.Port}/search?q=leak"));
        await using var provider = BuildWebToolsProvider($"http://127.0.0.1:{searchServer.Port}");
        var search = provider.GetRequiredService<IWebSearch>();

        var attempt = () => search.Search(new WebSearchRequest("drover", 5, 16_384), Token);

        var failure = await attempt.Should().ThrowAsync<WebSearchException>();
        failure.Which.Failure.Should().Be(WebSearchFailure.Unavailable);
        failure.Which.Message.Should().Contain("status 302");
        searchServer.Requests.Should().Be(1);
        targetServer.Requests.Should().Be(0, "redirects from the search service must never be followed");
    }

    [Fact]
    public async Task Search_never_sends_a_cookie_set_by_an_earlier_search()
    {
        List<string> requests = [];
        using var searchServer = new TestWebServer(request =>
        {
            lock (requests)
            {
                requests.Add(request);
            }

            var body = """{"results":[]}""";
            return $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nSet-Cookie: searx_session=tenant-a-cookie; Path=/\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}";
        });
        await using var provider = BuildWebToolsProvider($"http://127.0.0.1:{searchServer.Port}");
        var search = provider.GetRequiredService<IWebSearch>();

        await search.Search(new WebSearchRequest("first", 5, 16_384), Token);
        await search.Search(new WebSearchRequest("second", 5, 16_384), Token);

        requests.Should().HaveCount(2);
        requests[1].Should().NotContainEquivalentOf("Cookie:");
    }

    [Fact]
    public void Page_reader_client_built_by_AddDroverWebTools_disables_cookies_and_redirects()
    {
        using var searchServer = new TestWebServer(_ => TestWebServer.Ok("""{"results":[]}"""));
        using var provider = BuildWebToolsProvider($"http://127.0.0.1:{searchServer.Port}");
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        var readerSockets = UnwrapSocketsHandler(factory.CreateHandler(WebPageReader.HttpClientName));
        var searchSockets = UnwrapSocketsHandler(factory.CreateHandler(SearXngSearch.HttpClientName));

        readerSockets.UseCookies.Should().BeFalse();
        readerSockets.AllowAutoRedirect.Should().BeFalse();
        searchSockets.UseCookies.Should().BeFalse();
        searchSockets.AllowAutoRedirect.Should().BeFalse();
    }

    #endregion

    #region Private Methods

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ServiceProvider BuildWebToolsProvider(string searchBaseUrl)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [WebServiceExtensions.SearchAddressSetting] = searchBaseUrl,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDroverPolicy(configuration);
        services.AddDroverWebTools(configuration);
        return services.BuildServiceProvider();
    }

    private static SocketsHttpHandler UnwrapSocketsHandler(HttpMessageHandler handler)
    {
        var current = handler;

        while (current is DelegatingHandler delegating && delegating.InnerHandler is not null)
        {
            current = delegating.InnerHandler;
        }

        var guardedInvokerField = current.GetType().GetField("guardedInvoker", BindingFlags.Instance | BindingFlags.NonPublic);
        guardedInvokerField.Should().NotBeNull("RoutingHandler holds the guarded HttpMessageInvoker");
        var invoker = (HttpMessageInvoker)guardedInvokerField!.GetValue(current)!;
        var handlerField = typeof(HttpMessageInvoker).GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic);
        return (SocketsHttpHandler)handlerField!.GetValue(invoker)!;
    }

    #endregion
}
