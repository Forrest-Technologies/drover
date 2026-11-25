namespace Drover.Ui.Chat;

/// <summary>How far work handed to another bot has got.</summary>
public enum DelegationPhase
{
    Working,
    WaitingForYou,
    Done,
    Failed,
    Cancelled,
    Refused,
}

/// <summary>Why the server refused to hand the work over.</summary>
public enum DelegationRefusal
{
    None,
    Loop,
    TooDeep,
    TooManyRuns,
    TooManyHandoffs,
    OverBudget,
    TargetPaused,
    TargetRemoved,
    NotAllowed,
}

/// <summary>How many handoffs a bot had made against its cap when a delegation was refused for handoffs.</summary>
public sealed record DelegationLimit(long Used, long Cap);

/// <summary>
/// Work one bot handed to a bot beneath it, as the parent's conversation shows it. <see cref="Depth"/> is how many
/// delegations this one sits inside: 0 for one made by the bot being talked to.
/// </summary>
public sealed record DelegationItem(
    Guid Id,
    string ParentName,
    Guid ChildId,
    string ChildName,
    string Task,
    DelegationPhase Phase,
    string? Answer,
    DelegationRefusal Refusal,
    string? ChildHref,
    int Depth)
{
    #region Properties

    /// <summary>True while the work is out: the child is working or waits for a person.</summary>
    public bool IsOut => Phase is DelegationPhase.Working or DelegationPhase.WaitingForYou;

    /// <summary>
    /// The child's own activity in order, its words and its tool calls, as the conversation shows a bot's. Empty past
    /// <see cref="DelegationWords.DeepestShown"/>, where the card links to the child's side instead.
    /// </summary>
    public IReadOnlyList<ChatMessage> Work { get; init; } = [];

    /// <summary>The handoff count and cap when the server refused for the bot's handoff limit.</summary>
    public DelegationLimit? RefusalLimit { get; init; }

    /// <summary>The bot's Limits settings, for a person who can edit it when a handoff limit refused this call.</summary>
    public string? LimitsHref { get; init; }

    #endregion
}

/// <summary>Work passing between bots, in words.</summary>
public static partial class DelegationWords
{
    #region Properties

    /// <summary>Past this many levels a card says who is working and links there, so a phone still reads it.</summary>
    public const int DeepestShown = 2;

    #endregion

    #region Public Methods

    /// <summary>The card's closed line: "Sample lead asked Sample scout: find three sources".</summary>
    public static string Sentence(DelegationItem item) => item.Phase is DelegationPhase.Refused
        ? $"{item.ParentName} could not hand work to {item.ChildName}"
        : $"{item.ParentName} asked {item.ChildName}: {FirstLine(item.Task)}";

    public static string State(DelegationPhase phase) => phase switch
    {
        DelegationPhase.Working => "Working",
        DelegationPhase.WaitingForYou => "Waiting for you",
        DelegationPhase.Done => "Done",
        DelegationPhase.Failed => "Failed",
        DelegationPhase.Cancelled => "Cancelled",
        _ => "Not handed over",
    };

    public static BadgeKind Kind(DelegationPhase phase) => phase switch
    {
        DelegationPhase.Working => BadgeKind.Live,
        DelegationPhase.WaitingForYou => BadgeKind.Warn,
        DelegationPhase.Done => BadgeKind.Ok,
        DelegationPhase.Failed => BadgeKind.Danger,
        DelegationPhase.Refused => BadgeKind.Warn,
        _ => BadgeKind.Neutral,
    };

    /// <summary>What the child's head shows while its work is shown inside the parent's conversation.</summary>
    public static AvatarState Head(DelegationPhase phase) => phase switch
    {
        DelegationPhase.Working => AvatarState.Thinking,
        DelegationPhase.Failed => AvatarState.Failed,
        _ => AvatarState.Rest,
    };

    /// <summary>Why the work was not handed over, as a sentence.</summary>
    public static string Refused(DelegationRefusal refusal, string childName, DelegationLimit? limit = null) => refusal switch
    {
        DelegationRefusal.Loop => $"{childName} is already part of this work further up, so handing it over would go round in a circle.",
        DelegationRefusal.TooDeep => "This work has been handed down as many levels as this space allows.",
        DelegationRefusal.TooManyRuns => "This work has started as many runs as this space allows.",
        DelegationRefusal.TooManyHandoffs when limit is not null =>
            $"Handoff limit reached: {limit.Used.ToString("N0", CultureInfo.InvariantCulture)} of {limit.Cap.ToString("N0", CultureInfo.InvariantCulture)}.",
        DelegationRefusal.TooManyHandoffs => "This bot has handed work over as many times as its limits allow for one piece of work.",
        DelegationRefusal.OverBudget => "This work has reached the spending limit this space sets for one piece of work.",
        DelegationRefusal.TargetPaused => $"{childName} is paused.",
        DelegationRefusal.TargetRemoved => $"{childName} has been removed.",
        DelegationRefusal.NotAllowed => $"This bot may not hand work to {childName}.",
        _ => "The server did not hand the work over.",
    };

    /// <summary>
    /// Why the server refused, from a <c>delegate</c> call's result: <c>{"error": ..., "reason": ..., "detail": ...}</c>.
    /// <see cref="DelegationRefusal.None"/> for a refusal whose reason this screen does not know, and null when the
    /// result is not a refusal or is cut off before it says.
    /// </summary>
    public static DelegationRefusal? RefusalIn(string? result) =>
        result is not null && Reason().Match(result) is { Success: true } reason
            ? RefusalOf(reason.Groups[1].Value, Detail().Match(result) is { Success: true } detail ? detail.Groups[1].Value : null)
            : null;

    /// <summary>
    /// The handoff count and cap carried in a refused <c>delegate</c> call's result, or null when the result does not
    /// carry both numbers.
    /// </summary>
    public static DelegationLimit? RefusalLimitIn(string? result) =>
        result is not null
        && UsedCount().Match(result) is { Success: true } used
        && CapCount().Match(result) is { Success: true } cap
        && long.TryParse(used.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var usedValue)
        && long.TryParse(cap.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var capValue)
            ? new DelegationLimit(usedValue, capValue)
            : null;

    #endregion

    #region Private Methods

    private static DelegationRefusal RefusalOf(string reason, string? detail) => (reason, detail) switch
    {
        ("loop_detected", _) => DelegationRefusal.Loop,
        ("budget_exceeded", "depth") => DelegationRefusal.TooDeep,
        ("budget_exceeded", "runs") => DelegationRefusal.TooManyRuns,
        ("budget_exceeded", "handoffs") => DelegationRefusal.TooManyHandoffs,
        ("budget_exceeded", _) => DelegationRefusal.OverBudget,
        ("target_unavailable", "paused") => DelegationRefusal.TargetPaused,
        ("target_unavailable", _) => DelegationRefusal.TargetRemoved,
        ("no_grant" or "not_beneath", _) => DelegationRefusal.NotAllowed,
        _ => DelegationRefusal.None,
    };

    // Read from the text, not parsed: the start of a result in a call's events may be cut off before it closes, and a
    // stored result can hold the object as an escaped string.
    [GeneratedRegex(@"\\?""reason\\?""\s*:\s*\\?""([a-z_]+)")]
    private static partial Regex Reason();

    [GeneratedRegex(@"\\?""detail\\?""\s*:\s*\\?""([a-z_]+)")]
    private static partial Regex Detail();

    [GeneratedRegex(@"\\?""used\\?""\s*:\s*(\d+)")]
    private static partial Regex UsedCount();

    [GeneratedRegex(@"\\?""cap\\?""\s*:\s*(\d+)")]
    private static partial Regex CapCount();

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;

    #endregion
}
