namespace Drover.Adapters.Tests.Mcp;

/// <summary>
/// One tool server that stops answering must not stop the others, nor another connection to the same server: a
/// public tool server is reached by many spaces, and a circuit that counted them together would let one space's
/// trouble pause everybody's calls.
/// </summary>
public sealed class McpCircuitTests
{
    #region Private Fields

    private static readonly Uri server = new("https://tools.example.test/mcp");

    #endregion

    #region Properties

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    #endregion

    #region Tests

    [Fact]
    public async Task A_connection_whose_server_stopped_answering_pauses_its_own_calls_and_nobody_elses()
    {
        var reached = new RequestCount();
        await using var provider = Build(reached);
        var source = provider.GetRequiredService<IToolSource>();
        var dead = new ToolEndpoint(Guid.CreateVersion7(), server);
        var alongside = new ToolEndpoint(Guid.CreateVersion7(), server);
        var elsewhereOnTheSameServer = new ToolEndpoint(Guid.CreateVersion7(), new Uri("https://tools.example.test/other/mcp"));

        await Fails(source, dead);
        await Fails(source, dead);
        var afterItsCircuitOpened = reached.Value;
        await Fails(source, dead);
        var whileOpen = reached.Value;
        await Fails(source, alongside);
        var afterTheOtherConnection = reached.Value;
        await Fails(source, elsewhereOnTheSameServer);

        whileOpen.Should().Be(afterItsCircuitOpened, "the connection's own calls are paused: nothing is sent while its circuit is open");
        afterTheOtherConnection.Should().BeGreaterThan(whileOpen, "another connection to the same server is not paused by it");
        reached.Value.Should().BeGreaterThan(afterTheOtherConnection, "nor is another connection to another path of it");
    }

    #endregion

    #region Private Methods

    private static ServiceProvider Build(RequestCount reached)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddMcpToolSource();
        services.AddHttpClient(McpToolSource.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new AlwaysDown(reached));
        services.Configure<HttpStandardResilienceOptions>(
            $"{McpToolSource.HttpClientName}-standard",
            options =>
            {
                // Two failures are enough to open a circuit here, so the test does not have to send many.
                options.CircuitBreaker.MinimumThroughput = 2;
                options.CircuitBreaker.FailureRatio = 0.5;
                options.CircuitBreaker.BreakDuration = TimeSpan.FromMinutes(5);
            });
        return services.BuildServiceProvider();
    }

    private static async Task Fails(IToolSource source, ToolEndpoint endpoint)
    {
        var listing = () => source.ListTools(endpoint, Token);
        await listing.Should().ThrowAsync<ToolSourceException>();
    }

    #endregion

    #region Nested Types

    private sealed class RequestCount
    {
        #region Private Fields

        private int value;

        #endregion

        #region Properties

        public int Value => Volatile.Read(ref value);

        #endregion

        #region Public Methods

        public void Count() => Interlocked.Increment(ref value);

        #endregion
    }

    /// <summary>A server that is there and answers every request with an error, as one that has fallen over does.</summary>
    private sealed class AlwaysDown(RequestCount reached) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            reached.Count();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }

    #endregion
}
