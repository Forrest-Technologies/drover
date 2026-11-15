namespace Drover.Ui.Starters;

/// <summary>What a point on a run's timeline is.</summary>
public enum TimelineKind
{
    Step,
    Tool,
    Delegation,
    Waiting,
    Rule,
}

/// <summary>How a point on the timeline stands. Live is the only one that moves.</summary>
public enum TimelineState
{
    Live,
    Done,
    Failed,
    Waiting,
    Cancelled,
}

/// <summary>A bot that took part in a run.</summary>
public sealed record TimelineBot(Guid Id, string Name);

/// <summary>
/// One thing that happened in a run: a bot thinking and writing, a tool call, work handed to another bot, a wait
/// for a person, a rule that fired. <see cref="Label"/> says it in plain words ("Ran a command").
/// <see cref="ChildBotId"/> is the bot that work was handed to, for a delegation.
/// </summary>
public sealed record TimelineEvent(
    Guid Id,
    Guid BotId,
    TimelineKind Kind,
    string Label,
    TimelineState State,
    DateTimeOffset Started,
    TimeSpan? Took = null,
    string? Summary = null,
    Guid? ChildBotId = null);

/// <summary>
/// An event placed on the timeline: its lane, its row in time order, and for a delegation the lane the work
/// branches into. <see cref="BranchLane"/> is null when the event stays in its own lane.
/// </summary>
public sealed record TimelineNode(TimelineEvent Event, int Lane, int Row, int? BranchLane);

/// <summary>A run laid out: one lane for each bot, in the order the bots joined, and every event in time order.</summary>
public sealed record TimelineModel(IReadOnlyList<TimelineBot> Lanes, IReadOnlyList<TimelineNode> Nodes)
{
    public static TimelineModel Empty { get; } = new([], []);
}

/// <summary>
/// The run as a timeline. This is a projection of the run's stored events and nothing else, so a live run and a
/// reloaded one lay out the same. The caller turns the event log into <see cref="TimelineEvent"/>s.
/// </summary>
public static class Timelines
{
    #region Public Methods

    /// <summary>
    /// Lays a run out. Lanes are in the order bots first appear, so the bot that was asked is first and each bot
    /// it handed work to follows. Events keep the order they are given for the same instant.
    /// </summary>
    public static TimelineModel Build(IReadOnlyList<TimelineBot> bots, IEnumerable<TimelineEvent> events)
    {
        var ordered = events.Select((item, index) => (item, index)).OrderBy(pair => pair.item.Started).ThenBy(pair => pair.index)
            .Select(pair => pair.item).ToArray();

        List<Guid> laneIds = [];
        foreach (var item in ordered)
        {
            Join(laneIds, item.BotId);
            if (item.ChildBotId is { } child)
            {
                Join(laneIds, child);
            }
        }

        var names = bots.ToDictionary(bot => bot.Id, bot => bot.Name);
        TimelineBot[] lanes = [.. laneIds.Select(id => new TimelineBot(id, names.GetValueOrDefault(id) ?? "A bot that was removed"))];
        TimelineNode[] nodes =
        [
            ..ordered.Select((item, row) => new TimelineNode(
                item,
                laneIds.IndexOf(item.BotId),
                row,
                item is { Kind: TimelineKind.Delegation, ChildBotId: { } child } ? laneIds.IndexOf(child) : null)),
        ];
        return new(lanes, nodes);
    }

    /// <summary>
    /// Where a key moves the selection. Up and down go back and forward in time. Left and right go to the nearest
    /// event in time in the lane beside this one. Home and End go to the first and last. Any other key stays.
    /// </summary>
    public static int Move(TimelineModel model, int current, string key)
    {
        if (model.Nodes.Count is 0)
        {
            return 0;
        }

        current = Math.Clamp(current, 0, model.Nodes.Count - 1);
        return key switch
        {
            "ArrowUp" => Math.Max(current - 1, 0),
            "ArrowDown" => Math.Min(current + 1, model.Nodes.Count - 1),
            "Home" => 0,
            "End" => model.Nodes.Count - 1,
            "ArrowLeft" => NearestIn(model, current, model.Nodes[current].Lane - 1),
            "ArrowRight" => NearestIn(model, current, model.Nodes[current].Lane + 1),
            _ => current,
        };
    }

    public static string State(TimelineState state) => state switch
    {
        TimelineState.Live => "Working",
        TimelineState.Done => "Done",
        TimelineState.Failed => "Failed",
        TimelineState.Waiting => "Waiting for you",
        _ => "Cancelled",
    };

    public static BadgeKind Kind(TimelineState state) => state switch
    {
        TimelineState.Live => BadgeKind.Live,
        TimelineState.Done => BadgeKind.Ok,
        TimelineState.Failed => BadgeKind.Danger,
        TimelineState.Waiting => BadgeKind.Warn,
        _ => BadgeKind.Neutral,
    };

    /// <summary>
    /// A node as one sentence, for its accessible name and for the list form: "Sample bot: Ran a command.
    /// Done, took 12 seconds." A hand-off names the bot the work went to.
    /// </summary>
    public static string Sentence(TimelineModel model, TimelineNode node)
    {
        var who = model.Lanes[node.Lane].Name;
        var what = node.BranchLane is { } lane ? $"{node.Event.Label} to {model.Lanes[lane].Name}" : node.Event.Label;
        var took = node.Event.Took is { } duration ? $", took {Words.Duration(duration)}" : string.Empty;
        return $"{who}: {what}. {State(node.Event.State)}{took}.";
    }

    #endregion

    #region Private Methods

    private static void Join(List<Guid> lanes, Guid bot)
    {
        if (!lanes.Contains(bot))
        {
            lanes.Add(bot);
        }
    }

    private static int NearestIn(TimelineModel model, int current, int lane)
    {
        var nearest = model.Nodes.Where(node => node.Lane == lane).OrderBy(node => Math.Abs(node.Row - current)).FirstOrDefault();
        return nearest?.Row ?? current;
    }

    #endregion
}
