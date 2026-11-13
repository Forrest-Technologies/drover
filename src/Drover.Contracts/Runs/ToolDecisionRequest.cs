namespace Drover.Contracts.Runs;

/// <summary>A person's answer to a tool call that waits for approval. The reason is told to the bot when it is denied.</summary>
public sealed record ToolDecisionRequest(bool Approved, string? Reason = null);
