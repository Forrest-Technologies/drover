namespace Drover.Adapters.Tests.Mcp;

/// <summary>The tool source conformance suite, run against the adapter for remote tool servers.</summary>
public sealed class McpToolSourceConformanceTests : ToolSourceConformance
{
    #region Overrides

    protected override ToolSourceUnderTest CreateSource()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddMcpToolSource();
        var provider = services.BuildServiceProvider();
        return new ToolSourceUnderTest(provider.GetRequiredService<IToolSource>(), provider);
    }

    protected override async Task<IToolServer> StartServer(ToolServerSpec spec) => await SdkToolServer.Start(spec);

    #endregion

    #region Tests

    [Fact]
    public async Task A_request_on_a_connection_the_server_closed_is_sent_again_once()
    {
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.SessionBased));
        await using var provider = BuildWithClosedConnection();
        var source = provider.GetRequiredService<IToolSource>();
        var endpoint = new ToolEndpoint(Guid.CreateVersion7(), server.Url);

        var opened = await source.ListTools(endpoint, Token);
        opened.Tools.Should().NotBeEmpty();

        var closed = provider.GetRequiredService<ClosedConnection>();
        closed.Arm();

        var again = await source.ListTools(endpoint, Token);

        again.Tools.Should().NotBeEmpty("the read is sent again on a fresh connection");
        closed.Failures.Should().Be(1, "only the request on the closed connection failed");
        server.Handshakes.Should().Be(2, "one new session served the second listing");
    }

    #endregion

    #region Private Methods

    private static ServiceProvider BuildWithClosedConnection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddMcpToolSource();
        services.AddSingleton<ClosedConnection>();
        services.AddHttpClient(McpToolSource.HttpClientName)
            .AddHttpMessageHandler(provider => provider.GetRequiredService<ClosedConnection>());
        return services.BuildServiceProvider();
    }

    #endregion

    #region Nested Types

    /// <summary>Fails one request at the socket, as a server that closed a pooled keep-alive connection does.</summary>
    private sealed class ClosedConnection : DelegatingHandler
    {
        #region Private Fields

        private int armed;

        private int failures;

        #endregion

        #region Properties

        public int Failures => Volatile.Read(ref failures);

        #endregion

        #region Public Methods

        public void Arm() => Interlocked.Exchange(ref armed, 1);

        #endregion

        #region Protected Methods

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref armed, 0) is 1)
            {
                Interlocked.Increment(ref failures);
                throw new HttpRequestException(
                    "An error occurred while sending the request.",
                    new SocketException((int)SocketError.ConnectionReset));
            }

            return base.SendAsync(request, cancellationToken);
        }

        #endregion
    }

    #endregion
}
