namespace Drover.Adapters.Conformance.Agents;

/// <summary>What the scripted model does in one call, in order.</summary>
public abstract record ModelOutput;

/// <summary>Streams one chunk of text.</summary>
public sealed record TextOutput(string Text) : ModelOutput;

/// <summary>Asks for a tool call.</summary>
public sealed record ToolCallOutput(string CallId, string ToolName, string ArgumentsJson = "{}") : ModelOutput;

/// <summary>Stops in the middle of the call until it is cancelled. A host abandoned here is a killed process.</summary>
public sealed record PauseOutput : ModelOutput;


/// <summary>One message the model received, reduced to what a test asserts on.</summary>
public sealed record SeenMessage(string Role, string Summary);


/// <summary>
/// A model that answers each call with the next scripted turn and remembers every request. It stands for the
/// provider, outside any Drover process, so it outlives a host that is killed and restarted.
/// </summary>
public sealed class ScriptedModel(params IReadOnlyList<ModelOutput>[] turns)
{
    #region Private Fields

    private readonly ConcurrentQueue<IReadOnlyList<ModelOutput>> remaining = new(turns);

    private readonly ConcurrentQueue<IReadOnlyList<SeenMessage>> requests = new();

    private readonly TaskCompletionSource paused = new(TaskCreationOptions.RunContinuationsAsynchronously);

    #endregion

    #region Properties

    public IReadOnlyList<IReadOnlyList<SeenMessage>> Requests => [.. requests];

    /// <summary>Completes when a call reaches a <see cref="PauseOutput"/>.</summary>
    public Task Paused => paused.Task;

    #endregion

    #region Public Methods

    public IReadOnlyList<ModelOutput> NextTurn(IReadOnlyList<SeenMessage> request)
    {
        requests.Enqueue(request);
        return remaining.TryDequeue(out var turn)
            ? turn
            : throw new InvalidOperationException($"The model was called {requests.Count} times; the script has fewer turns.");
    }

    public void SignalPaused() => paused.TrySetResult();

    #endregion
}
