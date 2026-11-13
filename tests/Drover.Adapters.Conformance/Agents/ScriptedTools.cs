namespace Drover.Adapters.Conformance.Agents;

/// <summary>
/// The tools a conformance run may call, standing for the outside world: they outlive a killed host and count every
/// time they really run.
/// </summary>
public sealed class ScriptedTools
{
    #region Private Fields

    private readonly ConcurrentDictionary<string, Func<ToolCallRequest, CancellationToken, Task<ToolOutcome>>> handlers =
        new(StringComparer.Ordinal);

    private readonly ConcurrentQueue<string> invocations = new();

    private readonly TaskCompletionSource paused = new(TaskCreationOptions.RunContinuationsAsynchronously);

    #endregion

    #region Properties

    public IReadOnlyList<string> Invocations => [.. invocations];

    /// <summary>Completes when a tool made with <see cref="AddPausing"/> starts.</summary>
    public Task Paused => paused.Task;

    #endregion

    #region Public Methods

    public static JsonElement Json(string json) => JsonElement.Parse(json);

    public ScriptedTools Add(string name, string resultJson = """{"ok":true}""") =>
        Add(name, (_, _) => Task.FromResult(new ToolOutcome(Json(resultJson))));

    public ScriptedTools Add(string name, Func<ToolCallRequest, CancellationToken, Task<ToolOutcome>> handler)
    {
        handlers[name] = handler;
        return this;
    }

    /// <summary>A tool that starts and then never finishes until cancelled.</summary>
    public ScriptedTools AddPausing(string name) =>
        Add(name, async (_, cancellationToken) =>
        {
            paused.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        });

    /// <summary>A tool that never finishes the first time it starts, and answers every time after that.</summary>
    public ScriptedTools AddPausingOnce(string name, string resultJson = """{"ok":true}""")
    {
        var started = 0;
        return Add(name, async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref started) == 1)
            {
                paused.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return new ToolOutcome(Json(resultJson));
        });
    }

    public int Count(string name) => invocations.Count(invoked => invoked == name);

    public async Task<ToolOutcome> Invoke(ToolCallRequest request, CancellationToken cancellationToken)
    {
        invocations.Enqueue(request.ToolName);
        return await handlers[request.ToolName](request, cancellationToken);
    }

    #endregion
}
