namespace Drover.Contracts.Bots;

/// <summary>
/// One tool as a bot's editor shows it: what it is, what this bot is set to, and the most it may be set to.
/// The source is "builtin" or "connection". The grant is this bot's own setting and the ceiling is what its parent
/// holds, each "off", "ask", or "allow"; the ceiling's bot name is that parent, when the bot has one.
/// </summary>
public sealed record BotToolEntry(
    Guid ToolId,
    string Name,
    string Title,
    string Description,
    string Source,
    Guid? ConnectionId,
    string? ConnectionName,
    string Sensitivity,
    string Grant,
    string Ceiling,
    string? CeilingBotName,
    bool NeedsReview);

/// <summary>Sets one tool for one bot: "off", "ask", or "allow".</summary>
public sealed record SetToolGrantRequest(string? Grant);
