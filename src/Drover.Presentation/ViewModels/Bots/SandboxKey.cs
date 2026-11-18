namespace Drover.Presentation.ViewModels.Bots;

/// <summary>A key button: the words on it, its accessible name, and the key names it presses together.</summary>
public sealed record SandboxKey(string Label, string Name, IReadOnlyList<string> Keys);
