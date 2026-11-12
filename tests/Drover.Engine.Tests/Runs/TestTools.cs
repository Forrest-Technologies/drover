namespace Drover.Engine.Tests.Runs;

/// <summary>
/// Built-in tools for tests, standing for the outside world: they outlive a killed host, count every time they
/// really run, and remember whether a tenant transaction was open in the flow that called them.
/// </summary>
public sealed class TestTools
{
    #region Private Fields

    private readonly ConcurrentQueue<string> invocations = new();

    private readonly ConcurrentQueue<bool> transactionWasOpen = new();

    private readonly TaskCompletionSource paused = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly List<BuiltinTool> tools = [];

    #endregion

    #region Properties

    public IReadOnlyList<BuiltinTool> All => tools;

    /// <summary>Completes when a pausing tool starts.</summary>
    public Task Paused => paused.Task;

    public IReadOnlyList<bool> TransactionWasOpen => [.. transactionWasOpen];

    #endregion

    #region Public Methods

    public int Count(string name) => invocations.Count(invoked => invoked == name);

    public TestTools Add(string name, ToolSensitivity sensitivity, string resultJson = """{"ok":true}""") =>
        Add(name, sensitivity, (_, _) => Task.FromResult(JsonElement.Parse(resultJson)));

    /// <summary>A tool that starts and never finishes until cancelled.</summary>
    public TestTools AddPausing(string name, ToolSensitivity sensitivity) =>
        Add(name, sensitivity, async (_, cancellationToken) =>
        {
            paused.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new UnreachableException();
        });

    /// <summary>A tool that starts and waits, ignoring cancellation, until the test calls <see cref="OpenGate"/>.</summary>
    public TestTools AddGated(string name, ToolSensitivity sensitivity, string resultJson = """{"ok":true}""") =>
        Add(name, sensitivity, async (_, _) =>
        {
            paused.TrySetResult();
            await gate.Task;
            return JsonElement.Parse(resultJson);
        });

    public void OpenGate() => gate.TrySetResult();

    /// <summary>A tool that never finishes the first time it starts, and answers every time after that.</summary>
    public TestTools AddPausingOnce(string name, ToolSensitivity sensitivity, string resultJson)
    {
        var started = 0;
        return Add(name, sensitivity, async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref started) == 1)
            {
                paused.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return JsonElement.Parse(resultJson);
        });
    }

    public TestTools Add(string name, ToolSensitivity sensitivity, Func<JsonElement, CancellationToken, Task<JsonElement>> run)
    {
        tools.Add(new BuiltinTool(
            name,
            name,
            $"The {name} tool.",
            JsonElement.Parse("""{"type":"object","properties":{}}"""),
            sensitivity,
            (arguments, cancellationToken) =>
            {
                invocations.Enqueue(name);
                transactionWasOpen.Enqueue(TenantScope.IsOpenInCurrentFlow);
                return run(arguments, cancellationToken);
            }));
        return this;
    }

    #endregion
}
