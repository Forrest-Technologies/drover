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

    public static string Schedules(IReadOnlyList<BotScheduleResponse>? schedules)
    {
        if (schedules is null)
        {
            return "Loading";
        }

        if (schedules.Count == 0)
        {
            return "None";
        }

        var next = schedules.Where(schedule => schedule.NextRunAt is not null).Min(schedule => schedule.NextRunAt);
        var count = schedules.Count == 1 ? ScheduleWords.Describe(schedules[0].Cron) : $"{schedules.Count} schedules";
        return next is { } moment ? $"{count} · next {ScheduleWords.Moment(moment)}" : $"{count} · off";
    }

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
