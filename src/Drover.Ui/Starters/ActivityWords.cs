namespace Drover.Ui.Starters;

/// <summary>How a run stands.</summary>
public enum RunOutcome
{
    Working,
    Waiting,
    Finished,
    Failed,
    Cancelled,
}

/// <summary>What started a run.</summary>
public enum RunStart
{
    Message,
    Routine,
    Call,
}

/// <summary>
/// One run as the Activity screen lists it. <see cref="FailureSentence"/> names the cause of a failed run.
/// <see cref="StartName"/> is the routine's sentence when a routine started it.
/// </summary>
public sealed record ActivityRun(
    Guid Id,
    Guid BotId,
    string BotName,
    RunOutcome Outcome,
    RunStart Start,
    string Asked,
    DateTimeOffset When,
    string ConversationHref,
    TimeSpan? Took = null,
    string? FailureSentence = null,
    string? StartName = null);

/// <summary>
/// A tool call that waits for the person. <see cref="Sentence"/> is the approval sentence ("Sample bot asks to
/// run a command") and <see cref="Summary"/> its arguments on one line. <see cref="Decided"/> says what was
/// decided once it has been, here or elsewhere.
/// </summary>
public sealed record WaitingCall(Guid Id, Guid BotId, string BotName, string Sentence, string Summary, DateTimeOffset When, string? Decided = null);

/// <summary>The Activity screen in words.</summary>
public static class ActivityWords
{
    #region Public Methods

    /// <summary>The outcome first, because it decides what the person does next.</summary>
    public static string Outcome(ActivityRun run) => run.Outcome switch
    {
        RunOutcome.Working => "Working",
        RunOutcome.Waiting => "Waiting for you",
        RunOutcome.Finished => "Finished",
        RunOutcome.Failed => "Failed",
        _ => "Cancelled",
    };

    public static BadgeKind Kind(RunOutcome outcome) => outcome switch
    {
        RunOutcome.Working => BadgeKind.Live,
        RunOutcome.Waiting => BadgeKind.Warn,
        RunOutcome.Finished => BadgeKind.Ok,
        RunOutcome.Failed => BadgeKind.Danger,
        _ => BadgeKind.Neutral,
    };

    /// <summary>How the run started: "You asked", "Routine: every weekday at 08:00", "Called from outside".</summary>
    public static string Started(ActivityRun run) => run.Start switch
    {
        RunStart.Routine when run.StartName is { Length: > 0 } name => $"Routine: {name}",
        RunStart.Routine => "A routine",
        RunStart.Call => "Called from outside",
        _ => "You asked",
    };

    /// <summary>The row's one line: who, how it started, and what was asked.</summary>
    public static string Line(ActivityRun run) => $"{run.BotName}. {Started(run)}: {RoutineWords.FirstLine(run.Asked)}";

    /// <summary>When, and how long it took once it has ended.</summary>
    public static string Timing(ActivityRun run, string moment) =>
        run.Took is { } took ? $"{moment}, took {Words.Duration(took)}" : moment;

    /// <summary>The control that brings in runs that arrived while the list was being read: "3 new runs".</summary>
    public static string NewRuns(int count) => $"Show {Words.Count(count, "new run")}";

    /// <summary>What the two bulk buttons say for a selection: "Approve the 3 chosen".</summary>
    public static string Bulk(bool approve, int count) => $"{(approve ? "Approve" : "Deny")} the {count} chosen";

    /// <summary>The confirmation's heading for a selection.</summary>
    public static string Confirm(bool approve, int count) =>
        $"{(approve ? "Approve" : "Deny")} {(count is 1 ? "this call" : $"these {count} calls")}?";

    #endregion
}
