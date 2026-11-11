namespace Drover.Domain.Agents;

/// <summary>The result of one tool call, after redaction.</summary>
public sealed record ToolOutcome(JsonElement Result, bool IsError = false);
