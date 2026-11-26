namespace Drover.Contracts.Bots;

/// <summary>
/// One tool as a bot's editor shows it: what it is, what this bot is set to, and the most it may be set to.
/// The source is "builtin" or "connection". The grant is this bot's own setting and the ceiling is what its parent
/// holds, each "off", "ask", or "allow"; the ceiling's bot name is that parent, when the bot has one. The state is
/// "available", or "missing" when its server no longer lists it. <see cref="AlwaysAsks"/> is a built-in tool that waits
/// for a person before every use on every server, whatever its grant: allow behaves as ask.
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
    string State,
    bool AlwaysAsks = false)
{
    #region Properties

    /// <summary>
    /// The tool's definition changed after this bot was granted it. Until a person accepts the change, the bot asks
    /// before each use, whatever its grant says.
    /// </summary>
    public bool NeedsReview { get; init; }

    /// <summary>Names this version of the tool's definition. Sent back when a person accepts a change.</summary>
    public string DefinitionHash { get; init; } = string.Empty;

    /// <summary>The definition a person last accepted, to compare with the one shown, while a review is waiting.</summary>
    public ToolDefinitionSnapshot? Reviewed { get; init; }

    #endregion
}

/// <summary>Sets one tool for one bot: "off", "ask", or "allow".</summary>
public sealed record SetToolGrantRequest(string? Grant);

/// <summary>
/// Sets several tools for one bot at once, as a group's quick setting in the editor does. Each change is "off",
/// "ask", or "allow"; either every change holds or none does.
/// </summary>
public sealed record SetToolGrantsRequest(List<ToolGrantChange> Grants);

/// <summary>One tool's new setting in <see cref="SetToolGrantsRequest"/>.</summary>
public sealed record ToolGrantChange(Guid ToolId, string? Grant);
