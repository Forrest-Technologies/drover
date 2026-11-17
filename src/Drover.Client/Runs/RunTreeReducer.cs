namespace Drover.Client.Runs;

/// <summary>
/// Turns a run tree's events, as one root run's stream carries them, into the tree a timeline lays out. It is a pure
/// function: each event goes to its own run's <see cref="RunReducer"/> view and, where it opens or closes something,
/// to the tree's moments. An event at or below <see cref="RunTreeView.LastSeq"/> changes nothing, so any prefix of a
/// log followed by the rest ends where the whole log at once does.
/// </summary>
public static class RunTreeReducer
{
    #region Public Methods

    public static RunTreeView Apply(RunTreeView tree, RunEventResponse stored)
    {
        if (stored.Seq <= tree.LastSeq)
        {
            return tree;
        }

        var seen = tree with
        {
            LastSeq = stored.Seq,
            FirstSeq = tree.FirstSeq is 0 ? stored.Seq : tree.FirstSeq,
            Runs = tree.Runs.SetItem(stored.RunId, RunReducer.Apply(tree.RunOf(stored.RunId), stored)),
        };
        return Record(seen, stored);
    }

    public static RunTreeView Apply(RunTreeView tree, IEnumerable<RunEventResponse> events) => events.Aggregate(tree, Apply);

    #endregion

    #region Moments

    private static RunTreeView Record(RunTreeView tree, RunEventResponse stored) => stored.Type switch
    {
        RunEventTypes.StepStarted => StartStep(tree, stored, RunEventTypes.Read<StepChanged>(stored)),
        RunEventTypes.StepDiscarded => tree with { Moments = tree.Moments.RemoveAll(moment => moment.Id == RunEventTypes.Read<StepChanged>(stored).MessageId) },
        RunEventTypes.MessageCompleted => Close(tree, RunEventTypes.Read<MessageCompleted>(stored).Message.Id, stored.CreatedAt),
        RunEventTypes.UsageRecorded => AddCost(tree, stored.RunId, RunEventTypes.Read<UsageRecorded>(stored)),
        RunEventTypes.ToolRequested => Open(tree, new(RunEventTypes.Read<ToolRequested>(stored).ToolCallId, stored.RunId, RunMomentKind.Tool, stored.CreatedAt)),
        RunEventTypes.ToolApprovalRequested => Open(tree, new(FromSeq(stored.Seq), stored.RunId, RunMomentKind.Approval, stored.CreatedAt)
        {
            ToolCallId = RunEventTypes.Read<ToolApprovalRequested>(stored).ToolCallId,
        }),
        RunEventTypes.ToolApproved => Answer(tree, RunEventTypes.Read<ToolDecided>(stored).ToolCallId, "approved", stored.CreatedAt),
        RunEventTypes.ToolDenied => Answer(tree, RunEventTypes.Read<ToolDecided>(stored).ToolCallId, "denied", stored.CreatedAt),
        RunEventTypes.ToolCompleted => Close(tree, RunEventTypes.Read<ToolCompleted>(stored).ToolCallId, stored.CreatedAt),
        RunEventTypes.DelegationStarted => HandOver(tree, stored, RunEventTypes.Read<DelegationStarted>(stored)),
        RunEventTypes.DelegationCompleted => TakeBack(tree, stored, RunEventTypes.Read<DelegationCompleted>(stored)),
        RunEventTypes.RuleFired => ApplyRule(tree, stored, RunEventTypes.Read<RuleFired>(stored)),
        _ => tree,
    };

    private static RunTreeView StartStep(RunTreeView tree, RunEventResponse stored, StepChanged step) =>
        Open(tree, new(step.MessageId, stored.RunId, RunMomentKind.Step, stored.CreatedAt) { Step = step.Step });

    // Only a model call's usage has a step: its cost belongs to that step's moment.
    private static RunTreeView AddCost(RunTreeView tree, Guid runId, UsageRecorded used) =>
        Change(
            tree,
            moment => moment is { Kind: RunMomentKind.Step } && moment.RunId == runId && moment.Step == used.Step,
            moment => moment with { Cost = (moment.Cost ?? 0m) + used.Cost, Currency = used.Currency });

    private static RunTreeView Answer(RunTreeView tree, Guid toolCallId, string outcome, DateTimeOffset at) =>
        Change(
            tree,
            moment => moment is { Kind: RunMomentKind.Approval, Outcome: null } && moment.ToolCallId == toolCallId,
            moment => moment with { Outcome = outcome, EndedAt = at });

    // The call that handed the work over becomes the hand-off, keeping its place, and the child run joins the tree.
    private static RunTreeView HandOver(RunTreeView tree, RunEventResponse stored, DelegationStarted started)
    {
        var handed = tree with { Bots = tree.Bots.SetItem(started.ChildRunId, started.ChildBotId) };
        var opened = Open(handed, new(started.ToolCallId, stored.RunId, RunMomentKind.Delegation, stored.CreatedAt));
        return Change(
            opened,
            moment => moment.Id == started.ToolCallId,
            moment => moment with
            {
                Kind = RunMomentKind.Delegation,
                ToolCallId = started.ToolCallId,
                ChildRunId = started.ChildRunId,
                ChildBotId = started.ChildBotId,
            });
    }

    private static RunTreeView TakeBack(RunTreeView tree, RunEventResponse stored, DelegationCompleted completed)
    {
        var closed = Close(tree, completed.ToolCallId, stored.CreatedAt);
        return Open(closed, new(FromSeq(stored.Seq), stored.RunId, RunMomentKind.Answer, stored.CreatedAt)
        {
            EndedAt = stored.CreatedAt,
            ToolCallId = completed.ToolCallId,
            ChildRunId = completed.ChildRunId,
            ChildBotId = completed.ChildBotId,
            Outcome = completed.Outcome,
            Text = completed.Summary,
        });
    }

    private static RunTreeView ApplyRule(RunTreeView tree, RunEventResponse stored, RuleFired fired) =>
        Open(tree, new(FromSeq(stored.Seq), stored.RunId, RunMomentKind.Rule, stored.CreatedAt)
        {
            EndedAt = stored.CreatedAt,
            ToolCallId = fired.ToolCallId,
            Outcome = fired.Effect,
            Text = fired.RuleName,
            Why = fired.Why,
        });

    private static RunTreeView Open(RunTreeView tree, RunMoment moment) =>
        tree.Moments.Any(known => known.Id == moment.Id) ? tree : tree with { Moments = tree.Moments.Add(moment) };

    private static RunTreeView Close(RunTreeView tree, Guid id, DateTimeOffset at) =>
        Change(tree, moment => moment.Id == id && moment.EndedAt is null, moment => moment with { EndedAt = at });

    private static RunTreeView Change(RunTreeView tree, Predicate<RunMoment> which, Func<RunMoment, RunMoment> change)
    {
        var index = tree.Moments.FindLastIndex(which);
        return index < 0 ? tree : tree with { Moments = tree.Moments.SetItem(index, change(tree.Moments[index])) };
    }

    // An event's number is unique in its tree, so a moment that has no id of its own is keyed by the one that opened it.
    private static Guid FromSeq(long seq)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes[8..], seq);
        return new(bytes);
    }

    #endregion
}
