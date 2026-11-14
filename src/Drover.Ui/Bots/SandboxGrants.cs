namespace Drover.Ui.Bots;

/// <summary>Where a set of tools stands for a bot: what the bot may do with them, counting grants staged for the next save.</summary>
public enum ToolStanding
{
    /// <summary>The tools are not in the catalog: the server has no sandbox provider, or the tools are still loading.</summary>
    Unknown,

    /// <summary>Some may be allowed and are not, staged or saved.</summary>
    NotAllowed,

    /// <summary>Every tool the parent allows is allowed or asks first, saved.</summary>
    Allowed,

    /// <summary>Allowed in the draft; the next save applies it.</summary>
    AllowedOnSave,

    /// <summary>The bot's parent allows none of them, so this bot cannot have them.</summary>
    ParentForbids,
}


/// <summary>
/// The sandbox tools and the screen tools as the bot editor's Sandbox section offers them: what the bot may do now,
/// from the grants the Tools section shows (the bot's own and its parent's ceiling), and the grants one tap stages.
/// Nothing is granted until the person saves, and never above the parent's ceiling.
/// </summary>
public static class SandboxGrants
{
    #region Properties

    public static IReadOnlyList<string> SandboxTools { get; } = ["sandbox.run", "sandbox.read_file", "sandbox.write_file", "sandbox.list_files"];

    public static IReadOnlyList<string> ScreenTools { get; } =
        ["computer.screenshot", "computer.click", "computer.type", "computer.key", "computer.scroll"];

    #endregion

    #region Public Methods

    public static ToolStanding Standing(IReadOnlyList<BotToolEntry>? tools, IReadOnlyList<string> names, IReadOnlyDictionary<Guid, string> staged)
    {
        var entries = Entries(tools, names);

        if (entries.Count == 0)
        {
            return ToolStanding.Unknown;
        }

        var open = entries.Where(entry => entry.Ceiling is not "off").ToList();

        if (open.Count == 0)
        {
            return ToolStanding.ParentForbids;
        }

        if (open.Any(entry => Effective(entry, staged) is "off"))
        {
            return ToolStanding.NotAllowed;
        }

        return open.Any(entry => staged.ContainsKey(entry.ToolId)) ? ToolStanding.AllowedOnSave : ToolStanding.Allowed;
    }

    /// <summary>
    /// The staged grants with these tools added: each one that is off is set to the most its parent allows, allow or
    /// ask. A tool the parent keeps off is left alone.
    /// </summary>
    public static IReadOnlyDictionary<Guid, string> Stage(IReadOnlyList<BotToolEntry>? tools, IReadOnlyList<string> names, IReadOnlyDictionary<Guid, string> staged)
    {
        var next = staged.ToDictionary(pair => pair.Key, pair => pair.Value);

        foreach (var entry in Entries(tools, names).Where(entry => entry.Ceiling is not "off" && Effective(entry, staged) is "off"))
        {
            next[entry.ToolId] = entry.Ceiling is "ask" ? "ask" : "allow";
        }

        return next;
    }

    /// <summary>Whether any of the tools asks a person before each use, once the staged grants are saved.</summary>
    public static bool Asks(IReadOnlyList<BotToolEntry>? tools, IReadOnlyList<string> names, IReadOnlyDictionary<Guid, string> staged) =>
        Entries(tools, names).Any(entry => Effective(entry, staged) is "ask");

    /// <summary>The bot above that keeps the tools off, by name, when the tools say who it is.</summary>
    public static string? Forbidder(IReadOnlyList<BotToolEntry>? tools, IReadOnlyList<string> names) =>
        Entries(tools, names).Select(entry => entry.CeilingBotName).FirstOrDefault(name => name is not null);

    #endregion

    #region Private Methods

    private static List<BotToolEntry> Entries(IReadOnlyList<BotToolEntry>? tools, IReadOnlyList<string> names) =>
        [.. (tools ?? []).Where(tool => tool.Source is "builtin" && tool.State is not "missing" && names.Contains(tool.Name, StringComparer.Ordinal))];

    private static string Effective(BotToolEntry entry, IReadOnlyDictionary<Guid, string> staged) =>
        staged.TryGetValue(entry.ToolId, out var grant) ? grant : entry.Grant;

    #endregion
}
