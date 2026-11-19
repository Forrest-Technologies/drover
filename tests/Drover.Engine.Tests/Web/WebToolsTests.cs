namespace Drover.Engine.Tests.Web;

/// <summary>
/// The web tools in real runs over real PostgreSQL: a bot granted them searches and reads a page through the standard
/// tool events with any model, and its run is tainted by what it read. Without a grant, or where the deployment does
/// not offer them, they are never sent to the model, and a call is refused.
/// </summary>
public sealed class WebToolsTests(PostgresServer postgres)
{
    #region Granted

    [Fact]
    public async Task A_bot_granted_the_web_tools_searches_and_reads_a_page_through_the_standard_tool_events()
    {
        using var site = new TestWebServer(_ => Page("<html><head><title>Barn report</title></head><body><p>The barn holds 40 bales.</p></body></html>"));
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "web_search", """{"query":"barn report","max_results":1}""")],
            [new ToolCallOutput("c2", "web_fetch", $$"""{"url":"http://site.test:{{site.Port}}/report"}""")],
            [new TextOutput("The barn holds 40 bales.")]);
        var search = new FixedSearch(new WebSearchResult("Barn report", new Uri($"http://site.test:{site.Port}/report"), "How many bales the barn holds."));
        await using var world = await RunWorld.Create(postgres, script, everyHost: services => AddWebTools(services, search));
        await world.Grant(WebTools.SearchName, GrantEffect.Allow);
        await world.Grant(WebTools.FetchName, GrantEffect.Allow);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "How many bales are in the barn?");
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        world.Model.ToolsOffered[0].Should().Contain(["web_search", "web_fetch"]);
        search.Queries.Should().Equal(["barn report"]);
        var events = await world.ReadEvents(runId);
        events.Where(stored => stored.Type == RunEventTypes.ToolRequested).Select(RunWorld.Data<ToolRequested>).Select(requested => requested.ToolName)
            .Should().Equal(WebTools.SearchName, WebTools.FetchName);
        events.Count(stored => stored.Type == RunEventTypes.ToolStarted).Should().Be(2);
        events.Where(stored => stored.Type == RunEventTypes.ToolCompleted).Select(RunWorld.Data<ToolCompleted>).Select(completed => completed.Outcome)
            .Should().Equal("completed", "completed");
        var searched = JsonElement.Parse(ToolResult(world.Model.Requests[1]));
        searched.GetProperty("results")[0].GetProperty("url").GetString().Should().Be($"http://site.test:{site.Port}/report");
        searched.GetProperty("note").GetString().Should().Contain("not instructions");
        var read = JsonElement.Parse(ToolResult(world.Model.Requests[2]));
        read.GetProperty("title").GetString().Should().Be("Barn report");
        read.GetProperty("text").GetString().Should().Be("The barn holds 40 bales.");
        (await world.ReadRun(runId)).Tainted.Should().BeTrue("text from the web is untrusted");
    }

    #endregion

    #region Not Offered

    [Fact]
    public async Task Without_a_grant_neither_web_tool_is_sent_to_the_model()
    {
        await using var world = await RunWorld.Create(
            postgres,
            new ScriptedModel([new TextOutput("I cannot look that up.")]),
            everyHost: services => AddWebTools(services, new FixedSearch()));
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Search the web for the barn report.");
        await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        (await world.Read(db => db.Tools.CountAsync(tool => tool.Name.StartsWith("web."), RunWorld.Token)))
            .Should().Be(2, "each is a catalog row a person can grant");
        world.Model.ToolsOffered[0].Should().NotContain(name => name.StartsWith("web_", StringComparison.Ordinal));
        (await world.ReadRun(runId)).Tainted.Should().BeFalse();
    }

    [Fact]
    public async Task Where_the_server_does_not_offer_the_web_tools_a_granted_one_is_not_sent_and_a_call_is_refused()
    {
        var policy = DeploymentPolicy.SelfHosted with { AllowWebTools = false };
        var search = new FixedSearch();
        await using var world = await RunWorld.Create(
            postgres,
            new ScriptedModel([new TextOutput("I cannot look that up.")]),
            everyHost: services =>
            {
                services.AddSingleton<IOptions<DeploymentPolicyOptions>>(Options.Create(new DeploymentPolicyOptions { Rules = policy }));
                AddWebTools(services, search);
            });
        await world.Grant(WebTools.SearchName, GrantEffect.Allow);
        await world.Grant(WebTools.FetchName, GrantEffect.Allow);
        var host = world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Search the web for the barn report.");
        await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);
        var call = async () => await Tool(host, WebTools.SearchName).Run(Call(world, host, """{"query":"barn"}"""), RunWorld.Token);

        world.Model.ToolsOffered[0].Should().NotContain(name => name.StartsWith("web_", StringComparison.Ordinal));
        (await call.Should().ThrowAsync<BuiltinToolException>()).Which.Message.Should().Contain(nameof(DeploymentPolicy.AllowWebTools));
        search.Queries.Should().BeEmpty();
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// The web tools as the server registers them, with a search that answers as the test says and a page client
    /// whose host names all resolve to the local test server.
    /// </summary>
    private static void AddWebTools(IServiceCollection services, IWebSearch search)
    {
        services.AddSingleton(search);
        services.AddWebSearch();
        services.AddWebFetch(provider => PageClients.Through(OutboundHttp.CreateHandler(
            provider.GetRequiredService<IOptions<DeploymentPolicyOptions>>().Value.Rules,
            (_, _) => Task.FromResult<IPAddress[]>([IPAddress.Loopback]),
            _ => false,
            followRedirects: false)));
    }

    private static string Page(string html) =>
        $"HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: {html.Length}\r\nConnection: close\r\n\r\n{html}";

    private static string ToolResult(ModelChatRequest request) =>
        request.Messages.Last(message => message.Role == "tool").Contents.OfType<ModelToolResult>().Single().Result.GetRawText();

    private static BuiltinTool Tool(EngineHost host, string name) =>
        host.Services.GetServices<BuiltinTool>().Single(tool => tool.Name == name);

    private static BuiltinToolCall Call(RunWorld world, EngineHost host, string arguments) =>
        new(
            JsonElement.Parse(arguments),
            world.BotId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            host.Services.GetRequiredService<RunScopes>().For(world.SpaceId, world.UserId),
            _ => Task.CompletedTask,
            () => Task.CompletedTask,
            _ => { });

    #endregion

    #region Nested Types

    /// <summary>A search service that answers every query with the same results, and keeps the queries it was asked.</summary>
    private sealed class FixedSearch(params WebSearchResult[] results) : IWebSearch
    {
        #region Properties

        public string Kind => "fixed";

        public ConcurrentQueue<string> Queries { get; } = new();

        #endregion

        #region Interface Implementations

        public Task<IReadOnlyList<WebSearchResult>> Search(WebSearchRequest request, CancellationToken cancellationToken)
        {
            Queries.Enqueue(request.Query);
            return Task.FromResult<IReadOnlyList<WebSearchResult>>([.. results.Take(request.MaxResults)]);
        }

        #endregion
    }

    #endregion
}
