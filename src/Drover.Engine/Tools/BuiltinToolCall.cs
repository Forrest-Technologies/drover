namespace Drover.Engine.Tools;

/// <summary>A piece of a running tool's output, as it arrives. <see cref="Stream"/> is stdout or stderr.</summary>
public sealed record ToolOutputChunk(string Stream, string Text)
{
    public const string Stdout = "stdout";

    public const string Stderr = "stderr";
}


/// <summary>
/// One call of a built-in tool, with the run it belongs to. <see cref="Tenant"/> opens short transactions as the run's
/// person in the run's space; none is open while the tool runs. <see cref="Output"/> streams output into the run's
/// events as <c>tool.output</c>, redacted, before the result. <see cref="Taint"/> marks the run as having read text
/// from an untrusted source. <see cref="Attach"/> hands the model a picture with the result, for its next call only:
/// pictures are never stored and cannot be redacted.
/// </summary>
public sealed record BuiltinToolCall(
    JsonElement Arguments,
    Guid BotId,
    Guid RunId,
    Guid ToolCallId,
    RunTenant Tenant,
    Func<ToolOutputChunk, Task> Output,
    Func<Task> Taint,
    Action<ToolImage> Attach)
{
    #region Properties

    /// <summary>Whether the run's own model reads images, so a picture handed over with <see cref="Attach"/> is seen.</summary>
    public bool ModelSeesImages { get; init; }

    /// <summary>
    /// Counts a model call the tool is about to make for its run against the run's limit on model calls, or refuses
    /// it in words. Outside a run there is no such limit to count against, so the call is refused.
    /// </summary>
    public Func<Task> BeginModelCall { get; init; } = () => throw new BuiltinToolException("A model can be called this way only inside a run.");

    /// <summary>Records what a model call the tool made cost, against the run, as the run's own model calls are.</summary>
    public Func<ToolModelUsage, Task> RecordModelUsage { get; init; } = _ => Task.CompletedTask;

    #endregion
}


/// <summary>The tokens one model call a tool made used, with the model that answered and its prices.</summary>
public sealed record ToolModelUsage(Guid ModelConnectionId, string Model, ModelPricing Pricing, long InputTokens, long OutputTokens);


/// <summary>
/// A built-in tool could not do what was asked. The message is the tool's own plain words for the model, which reads
/// it as the call's error result. It never holds a secret or text from outside Drover.
/// </summary>
public sealed class BuiltinToolException(string message) : Exception(message);
