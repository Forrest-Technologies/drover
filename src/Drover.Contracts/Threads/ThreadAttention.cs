namespace Drover.Contracts.Threads;

/// <summary>
/// What a thread has for the person asking, from their own runs only: a run of theirs waits for their answer
/// (<see cref="WaitingForYou"/>), their newest run failed and they have not opened the thread since
/// (<see cref="Failed"/>), or a bot said something in a run of theirs after the last message they saw
/// (<see cref="HasNew"/>). <see cref="LastFailure"/> is that newest run when it failed, whether seen or not, so the
/// conversation can end with it until a later run takes its place.
/// </summary>
public sealed record ThreadAttention(bool WaitingForYou, bool Failed, bool HasNew, RunFailure? LastFailure);

/// <summary>
/// A run that failed. <see cref="Cause"/> is one of <see cref="FailureCauses"/>. <see cref="Reported"/> is the error
/// the run's log holds, as safe to show as the log is; a screen shows it only when asked. <see cref="Limit"/> is set
/// when the run stopped at a Drover limit, with which limit it hit, what it used, and the cap; older runs have none.
/// </summary>
public sealed record RunFailure(Guid RunId, string Cause, string Reported, RunFailureLimit? Limit = null);

/// <summary>
/// The Drover limit a run stopped at: <see cref="Kind"/> is one of <see cref="FailureLimits"/>, <see cref="Used"/> is
/// how much the run used when it stopped, and <see cref="Cap"/> is the limit in force.
/// </summary>
public sealed record RunFailureLimit(string Kind, long Used, long Cap);

public static class FailureLimits
{
    #region Limits

    public const string ModelCalls = "model_calls";

    public const string Tokens = "tokens";

    public const string UnmeteredModelCalls = "unmetered_model_calls";

    #endregion
}

public static class FailureCauses
{
    #region Causes

    public const string ModelUnreachable = "model_unreachable";

    public const string ModelRefused = "model_refused";

    public const string ModelBusy = "model_busy";

    public const string ModelOutOfCredit = "model_out_of_credit";

    public const string BudgetReached = "budget_reached";

    public const string ServerRestarted = "server_restarted";

    public const string StoppedByRule = "stopped_by_rule";

    public const string Unknown = "unknown";

    #endregion
}
