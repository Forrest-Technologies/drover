namespace Drover.Ui.Chat;

/// <summary>
/// One bot turn, one compact ledger of work. A run writes a message for each step it takes; shown one by one, every
/// step stood its own head and every call its own card. Here the bot's messages of one run, with nothing from a person
/// between, are one turn: its first message carries the head and the ledger, the turn's reasoning and calls as one
/// list of single line rows, and the messages after it hang under it with only their words. A call that waits for a
/// person, hands work to another bot, asks for the screen, or keeps a note a person may forget stays a card, so
/// nothing that needs a person hides in a collapsed row.
/// </summary>
public static class TurnLedger
{
    #region Private Fields

    /// <summary>The fewest calls of one tool in a row that are grouped into one step. Two stay apart.</summary>
    public const int GroupFrom = 3;

    private const int SummaryLength = 80;

    #endregion

    #region Public Methods

    /// <summary>
    /// The messages with their turns marked: <see cref="ChatMessage.ContinuesTurn"/> on each bot message that goes on
    /// a turn, and on the first message of each turn with work, <see cref="ChatMessage.Ledger"/>.
    /// </summary>
    public static IReadOnlyList<ChatMessage> InTurns(this IReadOnlyList<ChatMessage> messages)
    {
        List<ChatMessage> marked = [.. messages];
        var start = 0;

        for (var index = 1; index <= marked.Count; index++)
        {
            if (index < marked.Count && Continues(marked[index - 1], marked[index]))
            {
                marked[index] = marked[index] with { ContinuesTurn = true };
                continue;
            }

            if (!marked[start].FromPerson && marked[start].RunId is not null && Steps(marked[start..index]) is { Count: > 0 } steps)
            {
                marked[start] = marked[start] with { Ledger = steps };
            }

            start = index;
        }

        return marked;
    }

    /// <summary>True for a call drawn as a row of the ledger; false for one that stays a card.</summary>
    public static bool Folds(ToolCard tool) =>
        tool is { Delegation: null, Takeover: null, NoteId: null } && tool.Phase is not ToolPhase.WaitingForYou;

    /// <summary>The steps of a turn's messages, in order: each message's reasoning, then its calls that fold.</summary>
    public static IReadOnlyList<LedgerStep> Steps(IEnumerable<ChatMessage> turn)
    {
        List<LedgerStep> steps = [];

        foreach (var message in turn)
        {
            if (message.Reasoning is { } reasoning)
            {
                steps.Add(LedgerStep.Thought(message.Key, reasoning, message.Thinking));
            }

            foreach (var tool in message.Tools.Where(Folds))
            {
                steps.Add(new([tool], null, false));
            }
        }

        return Grouped(steps);
    }

    /// <summary>What the ledger is called for someone who cannot see it: "Work in this reply, 6 steps".</summary>
    public static string ListName(IReadOnlyList<LedgerStep> steps)
    {
        var count = steps.Sum(step => Math.Max(1, step.Calls.Count));
        return $"Work in this reply, {count.ToString("N0", CultureInfo.InvariantCulture)} {(count == 1 ? "step" : "steps")}";
    }

    /// <summary>A step's label on its row: the call's label, or for a group, the count in words ("Ran 4 commands").</summary>
    public static string Label(LedgerStep step) => step switch
    {
        { Reasoning: not null, Thinking: true } => Thinking,
        { Reasoning: not null } => Reasoned,
        { IsGroup: true } => GroupLabel(step.Calls),
        _ => step.Calls[0].Label,
    };

    /// <summary>What the row says after its label: the command or path, on one line, cut at a readable length.</summary>
    public static string Summary(LedgerStep step) => step switch
    {
        { IsGroup: true } or { Reasoning: not null } => string.Empty,
        _ => Shortened(step.Calls[0].Summary),
    };

    /// <summary>The state a step is in: the worst of its calls, so a group with one failure reads as failed.</summary>
    public static LedgerState StateOf(LedgerStep step)
    {
        if (step.Reasoning is not null)
        {
            return step.Thinking ? LedgerState.Running : LedgerState.Done;
        }

        return step.Calls.Select(StateOf).Max();
    }

    /// <summary>The state of one call.</summary>
    public static LedgerState StateOf(ToolCard tool) => tool.Phase switch
    {
        ToolPhase.Done => LedgerState.Done,
        ToolPhase.Starting or ToolPhase.Approved or ToolPhase.Running or ToolPhase.WaitingForYou => LedgerState.Running,
        ToolPhase.Failed => LedgerState.Failed,
        _ => LedgerState.Stopped,
    };

    /// <summary>A state in words, as it is read out and shown in a tooltip.</summary>
    public static string StateWords(LedgerState state) => state switch
    {
        LedgerState.Done => "done",
        LedgerState.Running => "running",
        LedgerState.Failed => "failed",
        _ => "stopped",
    };

    /// <summary>How long a step took, short: "0.4 s", "38 s", "2 min 5 s". Null when no call says.</summary>
    public static string? Duration(LedgerStep step) => TotalMs(step) is > 0 and var total ? ShortDuration(total) : null;

    /// <summary>A failed step's reason on one line, shown in its row so a failure is seen without opening it.</summary>
    public static string? FailureLine(LedgerStep step)
    {
        if (StateOf(step) is not LedgerState.Failed || step.Calls.FirstOrDefault(tool => tool.Phase is ToolPhase.Failed) is not { } failed)
        {
            return null;
        }

        var reason = failed.CommandError is { Length: > 0 } error ? error
            : failed.ExitCode is not (null or 0) ? ExitLine(failed)
            : failed.Result is { Length: > 0 } result ? result
            : null;

        return reason is null ? null : Shortened(reason);
    }

    /// <summary>A command's exit code in words, "Exit code 2", or nothing for a call without one.</summary>
    public static string? ExitLine(ToolCard tool) =>
        tool.ExitCode is { } code ? $"Exit code {code.ToString(CultureInfo.InvariantCulture)}" : null;

    /// <summary>
    /// The row's name for someone who cannot see it: the label, the summary, the state, and the time, as in "Ran a
    /// command, curl -s https://example.test, done, 1.2 seconds".
    /// </summary>
    public static string RowName(LedgerStep step)
    {
        List<string> parts = [Label(step)];

        if (Summary(step) is { Length: > 0 } summary)
        {
            parts.Add(summary);
        }

        parts.Add(StateWords(StateOf(step)));

        if (TotalMs(step) is > 0 and var total)
        {
            parts.Add(SpokenDuration(total));
        }

        if (FailureLine(step) is { } failure)
        {
            parts.Add(failure);
        }

        return string.Join(", ", parts);
    }

    #endregion

    #region Private Methods

    // The next message goes on the turn: a bot's message of the same run as the bot's message before it.
    private static bool Continues(ChatMessage before, ChatMessage message) =>
        message is { FromPerson: false, RunId: { } run } && before is { FromPerson: false } && before.RunId == run;

    // Three or more calls of one tool in a row are one step. A reasoning step between them ends the run.
    private static List<LedgerStep> Grouped(List<LedgerStep> steps)
    {
        List<LedgerStep> grouped = [];
        var index = 0;

        while (index < steps.Count)
        {
            var end = index + 1;
            while (end < steps.Count && SameTool(steps[index], steps[end]))
            {
                end++;
            }

            if (end - index >= GroupFrom)
            {
                grouped.Add(new([.. steps[index..end].SelectMany(step => step.Calls)], null, false));
            }
            else
            {
                grouped.AddRange(steps[index..end]);
            }

            index = end;
        }

        return grouped;
    }

    private static bool SameTool(LedgerStep first, LedgerStep next) =>
        first is { Reasoning: null, Calls: [var a, ..] } && next is { Reasoning: null, Calls: [var b] } && a.Tool.Length > 0 && a.Tool == b.Tool;

    private static string GroupLabel(IReadOnlyList<ToolCard> calls)
    {
        var count = calls.Count.ToString("N0", CultureInfo.InvariantCulture);
        return calls[0].Tool switch
        {
            "sandbox.run" => $"Ran {count} commands",
            "sandbox.read_file" => $"Read {count} files",
            "sandbox.write_file" => $"Wrote {count} files",
            "sandbox.list_files" => $"Listed files {count} times",
            "computer.screenshot" => $"Took {count} screenshots",
            "computer.click" => $"Clicked {count} times",
            "computer.type" => $"Typed {count} times",
            "computer.key" => $"Pressed keys {count} times",
            "computer.scroll" => $"Scrolled {count} times",
            _ => $"{calls[^1].Label}, {count} times",
        };
    }

    private static long TotalMs(LedgerStep step) => step.Calls.Sum(tool => tool.DurationMs);

    private static string ShortDuration(long ms) => ms switch
    {
        < 10_000 => $"{(ms / 1000d).ToString("0.0", CultureInfo.InvariantCulture)} s",
        < 60_000 => $"{(ms / 1000).ToString(CultureInfo.InvariantCulture)} s",
        _ => $"{(ms / 60_000).ToString(CultureInfo.InvariantCulture)} min {(ms % 60_000 / 1000).ToString(CultureInfo.InvariantCulture)} s",
    };

    private static string SpokenDuration(long ms) => ms switch
    {
        < 10_000 => $"{(ms / 1000d).ToString("0.0", CultureInfo.InvariantCulture)} seconds",
        < 60_000 => $"{(ms / 1000).ToString(CultureInfo.InvariantCulture)} seconds",
        _ => $"{(ms / 60_000).ToString(CultureInfo.InvariantCulture)} minutes {(ms % 60_000 / 1000).ToString(CultureInfo.InvariantCulture)} seconds",
    };

    // The first line, cut at a readable length with an ellipsis.
    private static string Shortened(string text)
    {
        var line = text.Trim().Split('\n')[0].Trim();
        return line.Length <= SummaryLength ? line : $"{line[..(SummaryLength - 1)].TrimEnd()}…";
    }

    #endregion

    #region Fixed Words

    public const string Reasoned = "Reasoned";

    public const string Thinking = "Thinking";

    #endregion
}

/// <summary>
/// One row of a turn's ledger: one call, a group of calls of one tool, or the bot's reasoning. <see cref="Key"/> keeps
/// a row the same while the turn grows, so an expanded row stays expanded.
/// </summary>
public sealed record LedgerStep(IReadOnlyList<ToolCard> Calls, string? Reasoning, bool Thinking)
{
    #region Properties

    /// <summary>The row's identity: its first call, or the message its reasoning came with.</summary>
    public string Key { get; init; } = Calls is [var first, ..] ? first.Id.ToString() : string.Empty;

    /// <summary>True for a group of calls of one tool.</summary>
    public bool IsGroup => Calls.Count > 1;

    #endregion

    #region Public Methods

    public static LedgerStep Thought(string messageKey, string reasoning, bool thinking) =>
        new([], reasoning, thinking) { Key = $"reasoning-{messageKey}" };

    #endregion
}

/// <summary>A step's state, ordered from best to worst, so a group takes the worst of its calls.</summary>
public enum LedgerState
{
    Done,
    Running,
    Stopped,
    Failed,
}
