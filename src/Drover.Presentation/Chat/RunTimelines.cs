namespace Drover.Ui.Chat;

/// <summary>
/// A run tree as a timeline: each moment of its log becomes a node in its bot's lane, said in the words the
/// conversation uses for the same thing. It reads nothing but the tree, so a run watched live and the same run
/// read again later lay out alike.
/// </summary>
public static class RunTimelines
{
    #region Private Fields

    private const string UnknownBot = "A bot this run no longer names";

    private static readonly ConditionalWeakTable<ImmutableList<ToolCallView>, Dictionary<Guid, ToolCallView>> callsByList = [];

    #endregion

    #region Public Methods

    /// <summary>
    /// Lays the tree out. <paramref name="bots"/> names the space's bots; <paramref name="results"/> holds the stored
    /// result of a call where the conversation has it, which says more than the start its event keeps.
    /// </summary>
    public static TimelineModel Lay(
        RunTreeView tree,
        IEnumerable<TimelineBot> bots,
        IReadOnlyDictionary<Guid, PartView>? results = null) =>
        Timelines.Build(
            [.. bots, new(Guid.Empty, UnknownBot)],
            tree.Moments.Select(moment => Event(tree, moment, results)));

    /// <summary>The call a node stands for, as its card would show it, for a tool call, a wait, or a hand-off.</summary>
    public static ToolCard? CardOf(RunTreeView tree, RunMoment moment, IReadOnlyDictionary<Guid, PartView>? results = null) =>
        CallOf(tree, moment) is { } call ? call.ToCard(results?.GetValueOrDefault(call.ToolCallId)) : null;

    /// <summary>
    /// The call a node stands for, as its run's log leaves it. A call of a run that is over and was still open then
    /// was stopped with it; a call of a run still working stands as it is, even when this log stops short.
    /// </summary>
    public static ToolCallView? CallOf(RunTreeView tree, RunMoment moment)
    {
        if ((moment.ToolCallId ?? (moment.Kind is RunMomentKind.Tool ? moment.Id : null)) is not { } id)
        {
            return null;
        }

        var run = tree.RunOf(moment.RunId);
        var call = CallsOf(run).GetValueOrDefault(id);
        return run.IsActive ? call : call?.Settled();
    }

    /// <summary>The text a step wrote, once its message has any.</summary>
    public static string TextOf(RunTreeView tree, RunMoment moment) =>
        string.Concat(
            tree.RunOf(moment.RunId).Messages
                .Where(message => message.Id == moment.Id)
                .SelectMany(message => message.Parts)
                .Where(part => part.Kind is "text")
                .Select(part => part.Text));

    #endregion

    #region Private Methods

    // A run's calls by id, built once for each version of its list, so laying out a long run does not scan the list
    // once per node.
    private static Dictionary<Guid, ToolCallView> CallsOf(RunView run) =>
        callsByList.GetValue(run.ToolCalls, calls => calls
            .GroupBy(call => call.ToolCallId)
            .ToDictionary(group => group.Key, group => group.Last()));

    private static TimelineEvent Event(RunTreeView tree, RunMoment moment, IReadOnlyDictionary<Guid, PartView>? results)
    {
        var bot = tree.Bots.GetValueOrDefault(moment.RunId);
        var over = !tree.RunOf(moment.RunId).IsActive;
        var took = moment is { Kind: not (RunMomentKind.Answer or RunMomentKind.Rule), EndedAt: { } ended } ? ended - moment.At : (TimeSpan?)null;
        var card = CardOf(tree, moment, results);
        var (label, state, summary) = moment.Kind switch
        {
            RunMomentKind.Step => Step(tree, moment, over),
            RunMomentKind.Tool => (card!.Label, StateOf(card.Phase), Summary(card)),
            RunMomentKind.Approval => Approval(moment, card, over),
            RunMomentKind.Delegation => Delegation(tree, moment, card, over),
            RunMomentKind.Answer => Answer(moment),
            _ => ("A rule applied", TimelineState.Done, moment.Text),
        };

        var placed = new TimelineEvent(
            moment.Id,
            bot,
            KindOf(moment.Kind),
            label,
            state,
            moment.At,
            moment.Kind is RunMomentKind.Tool ? Duration(CallOf(tree, moment)) : took,
            summary,
            moment.ChildBotId);
        return moment.Kind is RunMomentKind.Step && state is TimelineState.Live && TextOf(tree, moment).Length > 0
            ? placed with { Writing = true }
            : placed;
    }

    private static (string, TimelineState, string?) Step(RunTreeView tree, RunMoment moment, bool over)
    {
        var wrote = TextOf(tree, moment).Length > 0;
        return (moment.EndedAt, over) switch
        {
            (not null, _) => (wrote ? "Thought and wrote" : "Thought", TimelineState.Done, null),
            (null, true) => (wrote ? "Thought and wrote" : "Thought", TimelineState.Cancelled, null),
            _ => (wrote ? "Writing" : "Thinking", TimelineState.Live, null),
        };
    }

    // A wait is a node of its own: it is the time the run spent on a person, and it ends with their answer.
    private static (string, TimelineState, string?) Approval(RunMoment moment, ToolCard? card, bool over)
    {
        var label = $"Asked to {card?.Request ?? "go on"}";
        var state = (moment.Outcome, over) switch
        {
            ("approved", _) => TimelineState.Done,
            ("denied", _) => TimelineState.Denied,
            (_, true) => TimelineState.Cancelled,
            _ => TimelineState.Waiting,
        };
        return (label, state, card is null ? null : Summary(card));
    }

    // A hand-off stays open until its answer comes back. While the work beneath it waits for a person, so does it.
    private static (string, TimelineState, string?) Delegation(RunTreeView tree, RunMoment moment, ToolCard? card, bool over)
    {
        var state = (moment.EndedAt, over) switch
        {
            (not null, _) => TimelineState.Done,
            (null, true) => TimelineState.Cancelled,
            _ when moment.ChildRunId is { } child && Waits(tree, child) => TimelineState.Waiting,
            _ => TimelineState.Live,
        };
        return ("Handed work", state, card is null ? null : TaskOf(card));
    }

    // A run waits for a person when it says so, or when work it handed down is still out and waits.
    private static bool Waits(RunTreeView tree, Guid runId) =>
        tree.RunOf(runId).Status is "waiting"
        || tree.RunOf(runId).ToolCalls.Any(call => call.State is "awaiting_approval")
        || tree.Moments.Any(moment => moment is { Kind: RunMomentKind.Delegation, EndedAt: null, ChildRunId: { } child }
            && moment.RunId == runId && Waits(tree, child));

    private static (string, TimelineState, string?) Answer(RunMoment moment) => moment.Outcome switch
    {
        "completed" => ("Got the answer", TimelineState.Done, null),
        "failed" => ("Got no answer", TimelineState.Failed, null),
        _ => ("Got no answer", TimelineState.Cancelled, null),
    };

    private static TimelineKind KindOf(RunMomentKind kind) => kind switch
    {
        RunMomentKind.Step => TimelineKind.Step,
        RunMomentKind.Tool => TimelineKind.Tool,
        RunMomentKind.Approval => TimelineKind.Waiting,
        RunMomentKind.Delegation => TimelineKind.Delegation,
        RunMomentKind.Answer => TimelineKind.Answer,
        _ => TimelineKind.Rule,
    };

    private static TimelineState StateOf(ToolPhase phase) => phase switch
    {
        ToolPhase.Starting or ToolPhase.Approved or ToolPhase.Running => TimelineState.Live,
        ToolPhase.WaitingForYou => TimelineState.Pending,
        ToolPhase.Done => TimelineState.Done,
        ToolPhase.Failed or ToolPhase.NotAllowed or ToolPhase.Interrupted => TimelineState.Failed,
        ToolPhase.Denied => TimelineState.Denied,
        _ => TimelineState.Cancelled,
    };

    private static TimeSpan? Duration(ToolCallView? call) =>
        call is { IsOpen: false, DurationMs: > 0 } ? TimeSpan.FromMilliseconds(call.DurationMs) : null;

    private static string? Summary(ToolCard card) => card.Summary.Length > 0 ? card.Summary : null;

    // The first line of the task, as the parent wrote it. It is shown as text, never as markup.
    private static string? TaskOf(ToolCard card)
    {
        var task = JsonElement.Parse(card.Arguments) is { ValueKind: JsonValueKind.Object } arguments
            && arguments.TryGetProperty("task", out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;
        return task?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
    }

    #endregion
}
