namespace Drover.Agents.Runtime;

/// <summary>A tool's picture waiting for the model's next call, with the call that made it.</summary>
public sealed record ShownImage(string CallId, string ToolName, ToolImage Image);
