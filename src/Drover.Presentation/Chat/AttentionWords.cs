namespace Drover.Ui.Chat;

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
    ModelBusy,
    ModelOutOfCredit,
    BudgetReached,
    ServerRestarted,
    StoppedByRule,
}

/// <summary>
/// The three things a bot can have for a person, in words. They are facts, not live states: nothing glows or
/// moves for them, and each is a word as well as a color.
/// </summary>
public static class AttentionWords
{
    #region Properties

    public const string OpenLimitsLabel = "Open limits";

    #endregion

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

    /// <summary>The cause the server sent. One this screen does not know is not guessed at.</summary>
    public static FailureCause CauseOf(string cause) => cause switch
    {
        FailureCauses.ModelUnreachable => FailureCause.ModelUnreachable,
        FailureCauses.ModelRefused => FailureCause.ModelRefused,
        FailureCauses.ModelBusy => FailureCause.ModelBusy,
        FailureCauses.ModelOutOfCredit => FailureCause.ModelOutOfCredit,
        FailureCauses.BudgetReached => FailureCause.BudgetReached,
        FailureCauses.ServerRestarted => FailureCause.ServerRestarted,
        FailureCauses.StoppedByRule => FailureCause.StoppedByRule,
        _ => FailureCause.Unknown,
    };

    /// <summary>
    /// The one thing each bot has for the person, across every thread of theirs with it. A bot with nothing is left
    /// out.
    /// </summary>
    public static IReadOnlyDictionary<Guid, Attention> OfCrew(IEnumerable<ThreadSummary> threads) =>
        threads
            .Where(thread => thread.Attention is not null)
            .GroupBy(thread => thread.BotId)
            .Select(bot => (bot.Key, Shown: Of(
                bot.Any(thread => thread.Attention!.WaitingForYou),
                bot.Any(thread => thread.Attention!.Failed),
                bot.Any(thread => thread.Attention!.HasNew))))
            .Where(bot => bot.Shown is not Attention.None)
            .ToDictionary(bot => bot.Key, bot => bot.Shown);

    /// <summary>How many bots wait for the person, which the tab's title counts.</summary>
    public static int Waiting(IReadOnlyDictionary<Guid, Attention> crew) =>
        crew.Values.Count(attention => attention is Attention.WaitingForYou);

    /// <summary>What failed and what to do about it, in a sentence.</summary>
    public static string Failure(FailureCause cause, string botName, RunFailureLimit? limit = null) => cause switch
    {
        FailureCause.ModelUnreachable => $"{botName} could not reach its model. Check the model connection, then try again.",
        FailureCause.ModelRefused => $"{botName}'s model refused the request. Try again, or change what was asked.",
        FailureCause.ModelBusy => $"{botName}'s model is busy right now. Try again shortly.",
        FailureCause.ModelOutOfCredit => $"{botName}'s model account is out of credit. Add credit with the model's provider, then try again.",
        FailureCause.BudgetReached => BudgetFailure(botName, limit),
        FailureCause.ServerRestarted => $"The server restarted while {botName} was working, and the outcome is not known. Check what was done before trying again.",
        FailureCause.StoppedByRule => $"{botName} stopped because one of its rules ended this work. An owner can change the rule in the bot's settings.",
        _ => $"{botName} stopped because something went wrong on the server. Try again.",
    };

    /// <summary>What failed and what to do about it, in a sentence, naming the limit when the server reported one.</summary>
    public static string Failure(RunFailure failure, string botName) =>
        Failure(CauseOf(failure.Cause), botName, failure.Limit);

    #endregion

    #region Private Methods

    private static string BudgetFailure(string botName, RunFailureLimit? limit) => limit?.Kind switch
    {
        FailureLimits.ModelCalls =>
            $"{botName} stopped after {Count(limit.Used)} of {Count(limit.Cap)} {(limit.Cap == 1 ? "model call" : "model calls")}.",
        FailureLimits.Tokens =>
            $"{botName} stopped after {Count(limit.Used)} of {Count(limit.Cap)} {(limit.Cap == 1 ? "token" : "tokens")}.",
        FailureLimits.UnmeteredModelCalls =>
            $"{botName} stopped after {Count(limit.Used)} of {Count(limit.Cap)} {(limit.Cap == 1 ? "model call" : "model calls")} that reported no token use.",
        _ => $"{botName} stopped at a limit on how much one piece of work may use. An owner can change it in the bot's settings, under Limits.",
    };

    private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    #endregion
}
