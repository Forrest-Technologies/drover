namespace Drover.Ui.Chat;

/// <summary>One thing a tool call would do, named: "Name", "Sample scout".</summary>
public sealed record ToolFact(string Name, string Value);
