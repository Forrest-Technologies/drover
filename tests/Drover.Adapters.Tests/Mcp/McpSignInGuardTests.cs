namespace Drover.Adapters.Tests.Mcp;

/// <summary>
/// Where the adapter lets credentials go during and after a sign-in. A tool server's documents name the addresses
/// the protocol library talks to; these tests hold that the library's requests all pass the checked client, and
/// that the guard on it refuses the ones a hostile document could cause.
/// </summary>
public sealed class McpSignInGuardTests
{
    #region Properties

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    #endregion

    #region Tests

    [Fact]
    public async Task Every_request_of_a_sign_in_goes_through_the_checked_client()
    {
        var seen = new SeenRequests();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddMcpToolSource();
        services.AddHttpClient(McpToolSource.HttpClientName).AddHttpMessageHandler(() => new RecordingHandler(seen));
        await using var provider = services.BuildServiceProvider();
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless) { SignIn = new ToolServerSignIn() });
        var tokens = new KeptTokens();
        var endpoint = new ToolEndpoint(Guid.CreateVersion7(), server.Url)
        {
            SignIn = tokens.SignIn() with { AskPerson = (prompt, _) => server.Approve(prompt.AuthorizationUrl) },
        };

        await provider.GetRequiredService<IToolSource>().ListTools(endpoint, Token);

        seen.Paths.Should().Contain(path => path.StartsWith("/.well-known/oauth-protected-resource", StringComparison.Ordinal), "discovery of the resource");
        seen.Paths.Should().Contain(path => path.StartsWith("/.well-known/oauth-authorization-server", StringComparison.Ordinal), "discovery of the sign-in server");
        seen.Paths.Should().Contain("/desk/register").And.Contain("/desk/token");
        seen.Paths.Count.Should().Be(server.Requests, "the tool server saw no request that the checked client did not make");
    }

    [Fact]
    public async Task An_access_token_is_sent_to_the_tool_servers_own_origin_and_nowhere_else()
    {
        SignInAttempt.Begin(new ToolEndpoint(Guid.CreateVersion7(), new Uri("https://tools.example.test/mcp")));

        var home = await Send("https://tools.example.test/other/path", withToken: true);
        var otherHost = () => Send("https://elsewhere.example.test/mcp", withToken: true);
        var otherPort = () => Send("https://tools.example.test:8443/mcp", withToken: true);
        var withoutToken = await Send("https://sign-in.example.test/token", withToken: false);

        home.Should().Be(HttpStatusCode.OK);
        (await otherHost.Should().ThrowAsync<ToolSourceException>()).Which.Failure.Should().Be(ToolSourceFailure.Blocked);
        (await otherPort.Should().ThrowAsync<ToolSourceException>()).Which.Failure.Should().Be(ToolSourceFailure.Blocked);
        withoutToken.Should().Be(HttpStatusCode.OK, "the sign-in server may be another origin; it is never sent an access token");
    }

    [Fact]
    public async Task A_connection_saved_as_https_makes_no_request_in_the_clear()
    {
        SignInAttempt.Begin(new ToolEndpoint(Guid.CreateVersion7(), new Uri("https://tools.example.test/mcp")));

        var tokenEndpointInTheClear = () => Send("http://sign-in.example.test/token", withToken: false);
        var sameHostInTheClear = () => Send("http://tools.example.test/mcp", withToken: true);

        (await tokenEndpointInTheClear.Should().ThrowAsync<ToolSourceException>("a document cannot move the code or a refresh token onto http"))
            .Which.Failure.Should().Be(ToolSourceFailure.Blocked);
        (await sameHostInTheClear.Should().ThrowAsync<ToolSourceException>()).Which.Failure.Should().Be(ToolSourceFailure.Blocked);
    }

    [Fact]
    public async Task Outside_any_use_of_a_connection_no_access_token_is_sent()
    {
        var call = () => Send("https://tools.example.test/mcp", withToken: true);

        SignInAttempt.Current.Should().BeNull("this test began no use");
        (await call.Should().ThrowAsync<ToolSourceException>()).Which.Failure.Should().Be(ToolSourceFailure.Blocked);
    }

    #endregion

    #region Private Methods

    private static async Task<HttpStatusCode> Send(string address, bool withToken)
    {
        using var guard = new CredentialGuardHandler { InnerHandler = new AnswersOk() };
        using var invoker = new HttpMessageInvoker(guard);
        using var request = new HttpRequestMessage(HttpMethod.Get, address);

        if (withToken)
        {
            // Not a credential: the guard looks at the scheme only.
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {Guid.CreateVersion7():N}");
        }

        using var response = await invoker.SendAsync(request, Token);
        return response.StatusCode;
    }

    #endregion

    #region Nested Types

    private sealed class SeenRequests
    {
        public ConcurrentQueue<string> Paths { get; } = new();
    }

    private sealed class RecordingHandler(SeenRequests seen) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            seen.Paths.Enqueue(request.RequestUri!.AbsolutePath);
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class AnswersOk : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    #endregion
}
