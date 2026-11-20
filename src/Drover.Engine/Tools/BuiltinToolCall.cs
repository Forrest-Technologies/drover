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
/// events as <c>tool.output</c>, redacted, before the result. <see cref="Taint"/> records that the run read text
/// from an untrusted source. The record changes no grant. <see cref="Attach"/> hands the model a picture with the
/// result, for its next call only: pictures are never stored and cannot be redacted.
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

    /// <summary>The conversation the run works in. Null outside a run.</summary>
    public Guid? ThreadId { get; init; }

    /// <summary>The run is a delegated child run: its conversation is its own, not the person's.</summary>
    public bool IsDelegated { get; init; }

    /// <summary>
    /// Takes out of a text anything the run knows to be secret, as the run's events and stored results are. A tool
    /// that keeps text the model wrote calls it first, so no secret is kept or given to a later run.
    /// </summary>
    public Func<string, string> Redact { get; init; } = text => text;

    /// <summary>Whether the run has read text from an untrusted source, as it stands when the tool asks.</summary>
    public Func<bool> IsTainted { get; init; } = () => false;

    /// <summary>
    /// Tells the run's watchers that a message the tool changed is now as given, with <c>message.completed</c>: a
    /// file shared into the reply that asked for it.
    /// </summary>
    public Func<Message, Task> AnnounceMessage { get; init; } = _ => Task.CompletedTask;

    #endregion
}


/// <summary>The tokens one model call a tool made used, with the model that answered and its prices.</summary>
public sealed record ToolModelUsage(Guid ModelConnectionId, string Model, ModelPricing Pricing, long InputTokens, long OutputTokens)
{
    #region Properties

    /// <summary>A price reported for this call itself. Null lets token pricing apply where tokens were reported.</summary>
    public decimal? DirectCost { get; init; }

    /// <summary>Whether the provider supplied token usage that can be priced.</summary>
    public bool HasTokenUsage { get; init; } = true;

    #endregion
}


/// <summary>
/// A built-in tool could not do what was asked. The message is the tool's own plain words for the model, which reads
/// it as the call's error result. It never holds a secret or text from outside Drover.
/// </summary>
public sealed class BuiltinToolException(string message) : Exception(message);
