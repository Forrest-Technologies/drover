namespace Drover.Ui.Bots;

/// <summary>
/// The one line each closed section of the bot editor shows: what the section is set to, in as few words as say it.
/// </summary>
public static class BotSummaries
{
    #region Public Methods

    public static string Model(BotDraft draft, string? connectionName)
    {
        List<string> parts = [connectionName ?? "No connection", draft.Model.Length == 0 ? "no model" : draft.Model];

        if (draft.ReasoningEffort.Length > 0)
        {
            parts.Add($"effort {draft.ReasoningEffort}");
        }

        if (draft.WebSearch is not "off")
        {
            parts.Add(draft.WebSearch is "on" ? "web search" : "web search when needed");
        }

        if (draft.StructuredOutput)
        {
            parts.Add("answers in JSON");
        }

        return string.Join(" · ", parts);
    }

    public static string Tools(IReadOnlyList<BotToolEntry>? tools, bool modelCallsTools)
    {
        if (!modelCallsTools)
        {
            return "This model cannot use tools";
        }

        if (tools is null)
        {
            return "Loading";
        }

        // A tool its server no longer lists cannot be used, and a grant that waits for review asks, whatever it is set to.
        var usable = tools.Where(tool => tool.State is not "missing").ToList();
        var allowed = usable.Count(tool => tool.Grant is "allow" && !tool.NeedsReview);
        var asking = usable.Count(tool => tool.Grant is "ask" || (tool.Grant is "allow" && tool.NeedsReview));
        var waiting = usable.Count(tool => tool.NeedsReview);
        var set = (allowed, asking) switch
        {
            (0, 0) => $"None on · {usable.Count} available",
            (_, 0) => $"{allowed} allowed",
            (0, _) => $"{asking} ask first",
            _ => $"{allowed} allowed · {asking} ask first",
        };
        return waiting == 0 ? set : $"{set} · {waiting} to review";
    }

    public static string Memory(BotDraft draft) =>
        $"{(draft.MemoryEnabled ? "Remembers" : "Does not remember")} · {Compaction(draft.Compaction).ToLowerInvariant()}";

    public static string Routines(IReadOnlyList<BotScheduleResponse>? schedules, string botName) =>
        schedules is null ? "Loading" : RoutineWords.Summary([.. schedules.Select(schedule => RoutineWords.Item(schedule, botName))]);

    /// <summary>
    /// The sandbox's settings, which model reads its screen when another one does, and what the bot may do in it when
    /// its tools are known.
    /// </summary>
    public static string Sandbox(BotDraft draft, IReadOnlyList<BotToolEntry>? tools = null, string? screenReaderName = null)
    {
        if (draft.SandboxPolicy is "none")
        {
            return "No sandbox";
        }

        var screen = (draft.Desktop, draft.ScreenReader) switch
        {
            (true, { } reader) => $" · desktop · screen read by {screenReaderName ?? reader.Model}",
            (true, null) => " · desktop",
            _ => string.Empty,
        };
        var settings = $"{SandboxPolicy(draft.SandboxPolicy)} · network {Network(draft.Network).ToLowerInvariant()}{screen}";
        var may = SandboxGrants.Standing(tools, SandboxGrants.SandboxTools, draft.StagedGrants) switch
        {
            ToolStanding.Allowed => " · may run commands",
            ToolStanding.AllowedOnSave => " · allowed when saved",
            ToolStanding.Partial => " · some tools allowed",
            ToolStanding.NotAllowed => " · not allowed yet",
            ToolStanding.ParentForbids => " · not allowed by its parent",
            _ => string.Empty,
        };
        return $"{settings}{may}";
    }

    public static string Team(BotDraft draft) =>
        (draft.MaxChildren.Trim(), draft.MaxDepthBelow.Trim()) switch
        {
            ("", "") => "The space's caps",
            (var children, "") => $"Up to {children} bots",
            ("", var depth) => $"Up to {depth} levels deep",
            (var children, var depth) => $"Up to {children} bots · {depth} levels deep",
        };

    /// <summary>
    /// What one piece of the bot's work may use, each limit the bot's own or else its space's. Before the server has
    /// said what the space allows, only the bot's own limits are named.
    /// </summary>
    public static string Limits(BotDraft draft, BotRunLimitOptions? space)
    {
        (string Own, int? Space, string One, string Many)[] limits =
        [
            (draft.MaxSteps, space?.MaxSteps, "model call", "model calls"),
            (draft.MaxTokens, space?.MaxTokens, "token", "tokens"),
            (draft.MaxDelegations, space?.MaxDelegations, "handoff", "handoffs"),
        ];
        var said = limits
            .Select(limit => (Most: int.TryParse(limit.Own.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var own) ? own : limit.Space, limit.One, limit.Many))
            .Where(limit => limit.Most is not (null or BotRunLimitOptions.NoLimit))
            .Select(limit => $"{Count(limit.Most)} {(limit.Most is 1 ? limit.One : limit.Many)}")
            .ToList();
        var hasStepLimit = int.TryParse(draft.MaxSteps.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
            space?.MaxSteps is { } stepLimit && stepLimit != BotRunLimitOptions.NoLimit;
        if (space is not null && !hasStepLimit)
        {
            said.Insert(0, "No limit");
        }
        return said.Count == 0 ? "The space's limits" : string.Join(" · ", said);
    }

    /// <summary>A count as a person reads it: 5,000,000. Empty for none.</summary>
    public static string Count(int? count) => count?.ToString("N0", CultureInfo.InvariantCulture) ?? string.Empty;

    public static string Compaction(string strategy) => strategy switch
    {
        "sliding_window" => "Keeps recent messages only",
        "summarization" => "Summarizes older messages",
        "tool_results" => "Shortens old tool results",
        "truncation" => "Cuts the oldest messages",
        _ => "Keeps every message",
    };

    public static string SandboxPolicy(string policy) => policy switch
    {
        "private" => "Private",
        "subtree" => "Shared with its team",
        "named" => "Shared with chosen bots",
        _ => "No sandbox",
    };

    public static string Network(string network) => network switch
    {
        "allowlist" => "Named hosts only",
        "open" => "Open",
        _ => "Off",
    };

    #endregion
}
