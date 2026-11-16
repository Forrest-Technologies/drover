namespace Drover.Ui.Starters;

/// <summary>What a bot has for the person looking at the crew. Only one is shown, the most pressing.</summary>
public enum Attention
{
    None,
    New,
    Failed,
    WaitingForYou,
}

/// <summary>Why a run failed, as far as the server can tell. Each has a sentence a person can act on.</summary>
public enum FailureCause
{
    Unknown,
    ModelUnreachable,
    ModelRefused,
    BudgetReached,
    ToolFailed,
    ServerRestarted,
}

/// <summary>
/// The three things a bot can have for a person, in words. They are facts, not live states: nothing glows or
/// moves for them, and each is a word as well as a color.
/// </summary>
public static class AttentionWords
{
    #region Public Methods

    /// <summary>The one thing to show: waiting for the person first, then a failure, then something unseen.</summary>
    public static Attention Of(bool waitingForYou, bool failed, bool hasNew) => (waitingForYou, failed, hasNew) switch
    {
        (true, _, _) => Attention.WaitingForYou,
        (_, true, _) => Attention.Failed,
        (_, _, true) => Attention.New,
        _ => Attention.None,
    };

    /// <summary>The word beside the bot's name, or null when there is nothing to say.</summary>
    public static string? Word(Attention attention) => attention switch
    {
        Attention.WaitingForYou => "Waiting for you",
        Attention.Failed => "Failed",
        Attention.New => "New",
        _ => null,
    };

    public static BadgeKind Kind(Attention attention) => attention switch
    {
        Attention.WaitingForYou => BadgeKind.Warn,
        Attention.Failed => BadgeKind.Danger,
        _ => BadgeKind.Neutral,
    };

    /// <summary>What the bot's row is read out as: "Sample bot, waiting for you".</summary>
    public static string Announce(string botName, Attention attention) =>
        Word(attention) is { } word ? $"{botName}, {word.ToLowerInvariant()}" : botName;

    /// <summary>The browser tab's title, with how many bots wait for the person in front of it.</summary>
    public static string TabTitle(string page, int waiting) => waiting > 0 ? $"({waiting}) {page}" : page;

    /// <summary>What failed and what to do about it, in a sentence.</summary>
    public static string Failure(FailureCause cause, string botName) => cause switch
    {
        FailureCause.ModelUnreachable => $"{botName} could not reach its model. Check the model connection, then try again.",
        FailureCause.ModelRefused => $"{botName}'s model refused the request. Try again, or change what was asked.",
        FailureCause.BudgetReached => $"{botName} stopped because a limit on this work was reached. An owner can raise the limit.",
        FailureCause.ToolFailed => $"{botName} stopped because a tool it needed failed. Try again.",
        FailureCause.ServerRestarted => $"The server restarted while {botName} was working, and the outcome is not known. Check what was done before trying again.",
        _ => $"{botName} stopped because something went wrong on the server. Try again.",
    };

    #endregion
}
