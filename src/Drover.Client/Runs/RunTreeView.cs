namespace Drover.Client.Runs;

/// <summary>What one moment of a run tree is: a model call, a tool call, a wait for a person, work handed to another bot, its answer coming back, or a rule that applied.</summary>
public enum RunMomentKind
{
    Step,
    Tool,
    Approval,
    Delegation,
    Answer,
    Rule,
}


/// <summary>
/// One thing that happened in a run tree, as its log tells it, at the time the log wrote it. <see cref="EndedAt"/> is
/// when it closed, if it has. A step is keyed by its message id and a tool call or delegation by its call id; a wait,
/// an answer, and a rule are keyed by the number of the event that opened them, so a moment has the same id however
/// its log was read.
/// </summary>
public sealed record RunMoment(Guid Id, Guid RunId, RunMomentKind Kind, DateTimeOffset At)
{
    #region Properties

    public DateTimeOffset? EndedAt { get; init; }

    /// <summary>The model call's number in its run, for a step.</summary>
    public int? Step { get; init; }

    /// <summary>The call a wait, a delegation, an answer, or a rule is about.</summary>
    public Guid? ToolCallId { get; init; }

    public Guid? ChildRunId { get; init; }

    public Guid? ChildBotId { get; init; }

    /// <summary>approved or denied for a wait; completed, failed, or cancelled for an answer; a rule's effect.</summary>
    public string? Outcome { get; init; }

    /// <summary>The start of the answer a delegation returned, redacted; a rule's name.</summary>
    public string? Text { get; init; }

    /// <summary>Why a rule applied, in words.</summary>
    public string? Why { get; init; }

    /// <summary>What a step's model call cost, once its usage is recorded, and in which currency.</summary>
    public decimal? Cost { get; init; }

    public string? Currency { get; init; }

    #endregion
}


/// <summary>
/// A whole run tree as one stream tells it: every run's own view, the bot each run belongs to, and every moment in
/// the order the log wrote it. It is built only by <see cref="RunTreeReducer"/>, so the same events always give the
/// same tree, whether they arrived live, after a reconnect, or all at once from a finished run.
/// </summary>
public sealed record RunTreeView
{
    #region Properties

    public required Guid RootRunId { get; init; }

    /// <summary>The last event applied. Pass it to <c>WatchRun</c> to go on from here.</summary>
    public long LastSeq { get; init; }

    /// <summary>The first event this tree was given; zero before any.</summary>
    public long FirstSeq { get; init; }

    /// <summary>Each run of the tree, by its id, reduced from its own events.</summary>
    public ImmutableDictionary<Guid, RunView> Runs { get; init; } = [];

    /// <summary>The bot of each run: the root's from the start, a child's from the delegation that began it.</summary>
    public ImmutableDictionary<Guid, Guid> Bots { get; init; } = [];

    public ImmutableList<RunMoment> Moments { get; init; } = [];

    /// <summary>True when the log's first events are no longer kept: the stream from the start began past event 1.</summary>
    public bool LostEarlierEvents => FirstSeq > 1;

    /// <summary>True until the root run has completed, failed, or been cancelled.</summary>
    public bool IsActive => Runs.GetValueOrDefault(RootRunId)?.IsActive ?? true;

    #endregion

    #region Public Methods

    public static RunTreeView Start(Guid rootRunId, Guid rootBotId) =>
        new() { RootRunId = rootRunId, Bots = ImmutableDictionary<Guid, Guid>.Empty.Add(rootRunId, rootBotId) };

    /// <summary>The run's view, or a run that has not been heard from yet.</summary>
    public RunView RunOf(Guid runId) => Runs.GetValueOrDefault(runId) ?? RunView.Start(runId);

    #endregion
}
