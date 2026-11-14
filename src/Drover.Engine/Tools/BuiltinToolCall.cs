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
/// from an untrusted source.
/// </summary>
public sealed record BuiltinToolCall(
    JsonElement Arguments,
    Guid BotId,
    Guid RunId,
    Guid ToolCallId,
    RunTenant Tenant,
    Func<ToolOutputChunk, Task> Output,
    Func<Task> Taint);


/// <summary>
/// A built-in tool could not do what was asked. The message is the tool's own plain words for the model, which reads
/// it as the call's error result. It never holds a secret or text from outside Drover.
/// </summary>
public sealed class BuiltinToolException(string message) : Exception(message);
