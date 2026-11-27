namespace Drover.Presentation.ViewModels;

/// <summary>The Tools section of the bot editor: every tool of the space, each off, ask, or allow for this bot.</summary>
public sealed class ToolsSectionViewModel(BotEditorViewModel editor, DroverApi api, ILogger logger)
{
    #region Properties

    public static string CannotCallTools =>
        "The model this bot uses cannot call tools, so its tools are not used. Choose a model with tool calling in the Model section to set them.";

    public static string LoadingTools => "Loading the tools.";

    public static string Hint => "A change here applies at once, even to a run in progress. Ask means the bot waits for a person to approve each use.";

    public static string AlwaysAsks => "Asks every time. A person approves each use, whatever this is set to.";

    public static string NeedsReview => "Changed, needs review";

    public static string LastReviewed => "What was last reviewed";

    public static string ReviewedTerm => "Reviewed";

    public static string NowTerm => "Now";

    public static string SameWords => "The description is the same. The tool's input changed.";

    public static string Accept => "Accept the change for every bot";

    public static string NoConnectionsBefore => "This space has no tool connections.";

    public static string AddToolServer => "Add a tool server";

    public static string NoConnectionsAfter => "and its tools are listed here.";

    public static string ToolServersAddress => Addresses.Tools;

    public static string QuickLabel => "All tools";

    public static string NoReadingTools => "No tools in this group are marked read-only.";

    public bool ModelCallsTools => editor.ModelCallsTools;

    public bool Loading => editor.Tools is null;

    public string? Problem { get; private set; }

    public string? Announcement { get; private set; }

    /// <summary>The tool a change is being asked for, whose controls wait meanwhile.</summary>
    public Guid? Busy { get; private set; }

    /// <summary>The group whose tools are being set all at once, whose controls wait meanwhile.</summary>
    public string? BusyGroup { get; private set; }

    // Bumped when the server refuses a change, so the control is drawn again showing the grant that still holds.
    public int Version { get; private set; }

    // Built-in tools first, then a group for each connection, in the order the server lists them.
    public IReadOnlyList<ToolGroup> Groups =>
    [
        new("Built in", null, null, [.. editor.Tools!.Where(tool => tool.Source is "builtin")]),
        .. editor.ToolConnections.Select(connection => new ToolGroup(
            connection.Name,
            connection.Id,
            connection.Status is ConnectionStatuses.Ready ? null : connection.LastError,
            [.. editor.Tools!.Where(tool => tool.ConnectionId == connection.Id)])),
    ];

    public bool NoConnectionsShown => editor.ToolConnections.Count == 0;

    #endregion

    #region Public Methods

    public static string Count(ToolGroup group)
    {
        var on = $"{group.Tools.Count(tool => tool.Grant is not "off")} of {group.Tools.Count} on";
        var waiting = group.Tools.Count(tool => tool.NeedsReview);
        return waiting == 0 ? on : $"{on} · {waiting} to review";
    }

    public static string Empty(ToolGroup group) => $"{group.Name} has listed no tools.{(group.Problem is null ? null : $" {group.Problem}")}";

    public static string Facts(BotToolEntry tool) => $"{tool.Name} · {Sensitivity(tool)}";

    public static string Sensitivity(BotToolEntry tool) => tool.Sensitivity switch
    {
        "read" => "reads",
        "write" => "may change things",
        _ => tool.Sensitivity.Replace('_', ' '),
    };

    public static string NoLongerListed(ToolGroup group) => $"{group.Name} no longer lists this tool, so it cannot be used.";

    public static string? Limit(BotToolEntry tool) => (tool.CeilingBotName, tool.Ceiling) switch
    {
        ({ } parent, "off") => $"{parent} does not have this.",
        ({ } parent, "ask") => $"{parent} has this set to ask, so this bot asks too.",
        _ => null,
    };

    public static IReadOnlyList<SegmentOption<string>> OptionsFor(BotToolEntry tool) =>
    [
        new("off", "Off"),
        new("ask", "Ask", Disabled: tool.Ceiling is "off"),
        new("allow", "Allow", Disabled: tool.Ceiling is not "allow" || tool.AlwaysAsks),
    ];

    public string GrantLabel(BotToolEntry tool) => $"{tool.Title} for {editor.Saved!.Name}";

    public string QuickGrantLabel(ToolGroup group) => $"Every tool in {group.Name} for {editor.Saved!.Name}";

    /// <summary>A group of two or more tools that can be set offers to set them all at once.</summary>
    public static bool OffersQuick(ToolGroup group) => Settable(group).Count() > 1;

    /// <summary>
    /// The settings for a whole group, like a tool's own: off, read (only), ask, allow. Each gives every tool the most it
    /// may have up to that setting, so one that cannot be told apart from a lesser one cannot be taken.
    /// </summary>
    public static IReadOnlyList<SegmentOption<string>> QuickOptions(ToolGroup group) =>
    [
        new("off", "Off"),
        new("read", "Read only", Disabled: SameAs(group, "read", "off")),
        new("ask", "Ask", Disabled: SameAs(group, "ask", "off")),
        new("allow", "Allow", Disabled: SameAs(group, "allow", "ask")),
    ];

    /// <summary>The group setting the tools are at now, or null when they are set one by one in some other way.</summary>
    public static string? QuickValue(ToolGroup group) =>
        QuickOptions(group).FirstOrDefault(option => !option.Disabled && Targets(group, option.Value).All(target => target.Tool.Grant == target.Grant))?.Value;

    /// <summary>Why Read only cannot be taken, when the reason is that no tool here is known only to read.</summary>
    public static string? QuickHint(ToolGroup group) => Settable(group).Any(tool => tool.Sensitivity is "read") ? null : NoReadingTools;

    /// <summary>Sets every tool of the group to what the group setting gives it, in one change the server takes whole or not at all.</summary>
    public async Task SetAll(ToolGroup group, string quick)
    {
        List<ToolGrantChange> changes = [.. Targets(group, quick).Where(target => target.Tool.Grant != target.Grant).Select(target => new ToolGrantChange(target.Tool.ToolId, target.Grant))];

        if (changes.Count == 0)
        {
            return;
        }

        BusyGroup = group.Name;
        Problem = null;
        editor.Notify();

        try
        {
            var changed = (await api.SetBotToolGrants(editor.SpaceId, editor.BotId, changes)).ToDictionary(entry => entry.ToolId);
            var label = QuickOptions(group).Single(option => option.Value == quick).Label;
            Announcement = $"{group.Name} is set to {label.ToLowerInvariant()}. {Tools(changes.Count)} changed.";
            editor.Tools = [.. editor.Tools!.Select(entry => changed.GetValueOrDefault(entry.ToolId) ?? entry)];
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ToolsSectionViewModel));
            Problem = Failures.Sentence(exception, "Check that the server is running.");
            Version++;
        }
        finally
        {
            BusyGroup = null;
            editor.Notify();
        }
    }

    public string ReviewWords => $"The server changed what this tool says it does. Until a person accepts the change, {editor.Saved!.Name} asks before each use.";

    public static string ReviewedWords(BotToolEntry tool, ToolDefinitionSnapshot reviewed) =>
        $"{(string.IsNullOrWhiteSpace(reviewed.Title) ? tool.Name : reviewed.Title)}\n{reviewed.Description}";

    public static string NowWords(BotToolEntry tool) => $"{tool.Title}\n{tool.Description}";

    public static bool WordsAreSame(BotToolEntry tool, ToolDefinitionSnapshot reviewed) =>
        reviewed.Title == tool.Title && reviewed.Description == tool.Description;

    public async Task Set(BotToolEntry tool, string grant)
    {
        Busy = tool.ToolId;
        Problem = null;
        editor.Notify();

        try
        {
            var changed = await api.SetBotToolGrant(editor.SpaceId, editor.BotId, tool.ToolId, grant);
            Announcement = $"{changed.Title} is now {changed.Grant}.";
            editor.Tools = [.. editor.Tools!.Select(entry => entry.ToolId == changed.ToolId ? changed : entry)];
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ToolsSectionViewModel));
            Problem = Failures.Sentence(exception, "Check that the server is running.");
            Version++;
        }
        finally
        {
            Busy = null;
            editor.Notify();
        }
    }

    /// <summary>Accepts a tool's changed definition. The server counts every bot's grant on it as current again.</summary>
    public async Task AcceptChange(BotToolEntry tool, Guid connectionId)
    {
        Busy = tool.ToolId;
        Problem = null;
        editor.Notify();

        try
        {
            await api.ConfirmToolReview(editor.SpaceId, connectionId, tool.ToolId, tool.DefinitionHash);
            Announcement = $"The change to {tool.Title} is accepted.";
            editor.Tools = await api.ListBotTools(editor.SpaceId, editor.BotId);
        }
        catch (DroverApiException changedAgain) when (changedAgain.StatusCode is HttpStatusCode.Conflict)
        {
            // The tool changed while it was being read: show what it says now, so the next yes is to that.
            logger.LogInformation(changedAgain, "A tool changed again while it was reviewed on the {Screen} screen", nameof(ToolsSectionViewModel));
            Problem = Failures.Sentence(changedAgain, "Check that the server is running.");
            editor.Tools = await api.ListBotTools(editor.SpaceId, editor.BotId);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ToolsSectionViewModel));
            Problem = Failures.Sentence(exception, "Check that the server is running.");
        }
        finally
        {
            Busy = null;
            editor.Notify();
        }
    }

    /// <summary>The section was closed: what it said goes with it, as it does on the web.</summary>
    public void Reset() => (Problem, Announcement, Busy, BusyGroup) = (null, null, null, null);

    #endregion

    #region Group Settings

    // A tool its server no longer lists cannot be used, so a group setting leaves it as it is.
    private static IEnumerable<BotToolEntry> Settable(ToolGroup group) => group.Tools.Where(tool => tool.State is not "missing");

    // What a group setting gives each tool: as much as it asks for, never more than the tool may have.
    private static IEnumerable<(BotToolEntry Tool, string Grant)> Targets(ToolGroup group, string quick) =>
        Settable(group).Select(tool => (tool, quick switch
        {
            "read" => tool.Sensitivity is "read" ? Most(tool, "allow") : "off",
            "ask" => Most(tool, "ask"),
            "allow" => Most(tool, "allow"),
            _ => "off",
        }));

    private static string Most(BotToolEntry tool, string wanted) => (tool.Ceiling, wanted) switch
    {
        ("off", _) => "off",
        (_, "allow") when tool.Ceiling is "ask" || tool.AlwaysAsks => "ask",
        _ => wanted,
    };

    private static bool SameAs(ToolGroup group, string quick, string lesser) =>
        Targets(group, quick).Select(target => target.Grant).SequenceEqual(Targets(group, lesser).Select(target => target.Grant));

    private static string Tools(int count) => count == 1 ? "1 tool" : $"{count} tools";

    #endregion

    #region Nested Types

    /// <summary>The tools of one source: the built-in ones, or one connection's.</summary>
    public sealed record ToolGroup(string Name, Guid? ConnectionId, string? Problem, List<BotToolEntry> Tools);

    #endregion
}
