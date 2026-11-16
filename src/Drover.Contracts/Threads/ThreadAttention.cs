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
/// the run's log holds, as safe to show as the log is; a screen shows it only when asked.
/// </summary>
public sealed record RunFailure(Guid RunId, string Cause, string Reported);

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
