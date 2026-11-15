namespace Drover.Ui.Starters;

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
    OverBudget,
    TargetPaused,
    TargetRemoved,
    NotAllowed,
}

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
    /// <summary>True while the work is out: the child is working or waits for a person.</summary>
    public bool IsOut => Phase is DelegationPhase.Working or DelegationPhase.WaitingForYou;
}

/// <summary>Work passing between bots, in words.</summary>
public static class DelegationWords
{
    #region Properties

    /// <summary>Past this many levels a card says who is working and links there, so a phone still reads it.</summary>
    public const int DeepestShown = 2;

    #endregion

    #region Public Methods

    /// <summary>The card's closed line: "Sample lead asked Sample scout: find three sources".</summary>
    public static string Sentence(DelegationItem item) => item.Phase is DelegationPhase.Refused
        ? $"{item.ParentName} could not hand work to {item.ChildName}"
        : $"{item.ParentName} asked {item.ChildName}: {RoutineWords.FirstLine(item.Task)}";

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
    public static string Refused(DelegationRefusal refusal, string childName) => refusal switch
    {
        DelegationRefusal.Loop => $"{childName} is already part of this work further up, so handing it over would go round in a circle.",
        DelegationRefusal.TooDeep => "This work has been handed down as many levels as this space allows.",
        DelegationRefusal.TooManyRuns => "This work has started as many runs as this space allows.",
        DelegationRefusal.OverBudget => "This work has reached the spending limit this space sets for one piece of work.",
        DelegationRefusal.TargetPaused => $"{childName} is paused.",
        DelegationRefusal.TargetRemoved => $"{childName} has been removed.",
        DelegationRefusal.NotAllowed => $"This bot may not hand work to {childName}.",
        _ => "The server did not hand the work over.",
    };

    #endregion
}
