namespace Drover.Adapters.Tests.Mcp;

/// <summary>
/// A kept session is closed as the connection it belongs to, whoever's use happens to be running when it is closed:
/// its last request goes out with its own credential, to its own server, and is counted on its own circuit.
/// </summary>
public sealed class McpSessionClosingTests
{
    #region Properties

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    #endregion

    #region Tests

    [Fact]
    public async Task Letting_a_connection_go_tells_its_server_the_session_is_over_also_when_the_connection_carries_a_token()
    {
        await using var server = await SdkToolServer.Start(WithToken(out var headers));
        await using var provider = Build(TimeProvider.System);
        var source = provider.GetRequiredService<IToolSource>();
        var endpoint = new ToolEndpoint(Guid.CreateVersion7(), server.Url) { Headers = headers };

        await source.ListTools(endpoint, Token);
        await source.Forget(endpoint.ConnectionId, Token);

        server.SessionsClosed.Should().Be(1, "the closing request is the connection's own, so its token may go with it");
    }

    [Fact]
    public async Task A_session_left_idle_is_closed_as_its_own_connection_while_another_connection_is_in_use()
    {
        var clock = new SkippingClock();
        await using var idle = await SdkToolServer.Start(WithToken(out var headers));
        await using var busy = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.SessionBased));
        await using var provider = Build(clock);
        var source = provider.GetRequiredService<IToolSource>();

        await source.ListTools(new ToolEndpoint(Guid.CreateVersion7(), idle.Url) { Headers = headers }, Token);
        clock.Skip(TimeSpan.FromMinutes(11));
        await source.ListTools(new ToolEndpoint(Guid.CreateVersion7(), busy.Url), Token);

        idle.SessionsClosed.Should().Be(1, "closing it is not a request of the connection that happened to be in use, whose address its token must not go to");
        busy.SessionsClosed.Should().Be(0);
    }

    #endregion

    #region Private Methods

    private static ToolServerSpec WithToken(out Dictionary<string, string> headers)
    {
        var value = $"Bearer {Guid.CreateVersion7():N}";
        headers = new Dictionary<string, string> { ["Authorization"] = value };
        return new ToolServerSpec(ServerGeneration.SessionBased, "Authorization", value);
    }

    private static ServiceProvider Build(TimeProvider time)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(time);
        services.AddMcpToolSource();
        return services.BuildServiceProvider();
    }

    #endregion

    #region Nested Types

    /// <summary>The real clock, which a test can move ahead, so ten idle minutes take no time.</summary>
    private sealed class SkippingClock : TimeProvider
    {
        #region Private Fields

        private long ahead;

        #endregion

        #region Public Methods

        public override long GetTimestamp() => base.GetTimestamp() + Interlocked.Read(ref ahead);

        public void Skip(TimeSpan by) => Interlocked.Add(ref ahead, (long)(by.TotalSeconds * TimestampFrequency));

        #endregion
    }

    #endregion
}
