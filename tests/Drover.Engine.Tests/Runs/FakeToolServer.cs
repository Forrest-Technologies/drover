namespace Drover.Engine.Tests.Runs;

/// <summary>
/// A tool server for engine tests, standing for the outside world: it lists what the test tells it to, answers
/// calls, and remembers every endpoint it was reached with, credential included, so a test can say whose
/// credential a call carried. The real adapter has its own conformance suite.
/// </summary>
public sealed class FakeToolServer : IToolSource
{
    #region Private Fields

    private readonly ConcurrentQueue<(string Tool, ToolEndpoint Endpoint)> calls = new();

    private readonly ConcurrentDictionary<string, Func<JsonElement, ToolEndpoint, ToolOutcome>> answers = new(StringComparer.Ordinal);

    #endregion

    #region Properties

    public ToolSourceKind Kind => ToolSourceKind.Mcp;

    public List<ListedTool> Tools { get; } = [];

    /// <summary>When set, the server is down: listing and calling both fail with it.</summary>
    public ToolSourceException? Down { get; set; }

    public IReadOnlyList<(string Tool, ToolEndpoint Endpoint)> Calls => [.. calls];

    #endregion

    #region Public Methods

    public FakeToolServer Offer(string name, string description, bool readOnly, Func<JsonElement, ToolEndpoint, ToolOutcome> answer)
    {
        Tools.RemoveAll(tool => tool.Name == name);
        Tools.Add(new ListedTool(name, null, description, JsonElement.Parse("""{"type":"object","properties":{}}""")) { ClaimsReadOnly = readOnly });
        answers[name] = answer;
        return this;
    }

    public FakeToolServer Offer(string name, string description, bool readOnly, string resultJson) =>
        Offer(name, description, readOnly, (_, _) => new ToolOutcome(JsonElement.Parse(resultJson)));

    public int Count(string tool) => calls.Count(call => call.Tool == tool);

    #endregion

    #region Interface Implementations

    public Task<ToolListing> ListTools(ToolEndpoint endpoint, CancellationToken cancellationToken) =>
        Down is { } down ? throw down : Task.FromResult(new ToolListing([.. Tools], "2026-07-28", true));

    public Task<ToolOutcome> CallTool(ToolEndpoint endpoint, string toolName, JsonElement arguments, CancellationToken cancellationToken)
    {
        if (Down is { } down)
        {
            throw down;
        }

        calls.Enqueue((toolName, endpoint));
        return Task.FromResult(answers[toolName](arguments, endpoint));
    }

    #endregion
}
