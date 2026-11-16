namespace Drover.Contracts.Runs;

/// <summary>
/// A person hands a bot's screen back after it asked them to take over: done, or could not. The note is what they
/// chose to tell the bot when they could not, and may be left out. Nothing they did on the screen is part of it.
/// </summary>
public sealed record ToolHandbackRequest(bool Done, string? Note = null);
