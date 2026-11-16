namespace Drover.Engine.Threads;

/// <summary>
/// What a thread has for one person, from the facts stored about it: their run waits for them, their newest run
/// failed after they last looked, or a bot said something they have not seen.
/// </summary>
public static class Attentions
{
    #region Public Methods

    public static ThreadAttention ToAttention(this ThreadFacts facts)
    {
        var newest = facts.NewestRun;
        var failed = newest is { Status: RunStatus.Failed } ? newest : null;

        return new ThreadAttention(
            newest is { Status: RunStatus.Waiting } && (facts.ApprovalOpen || newest.StatusReason is not RunReasons.AwaitingApproval),
            failed is not null && (facts.SeenAt is null || facts.SeenAt < failed.FinishedAt),
            facts.HasNew,
            failed?.ToFailure());
    }

    /// <summary>The run's failure as a cause a person can act on, and the error as the run's log holds it.</summary>
    public static RunFailure ToFailure(this Run run) =>
        new(run.Id, Cause(run.Error?.Code), run.Error is { } error ? $"{error.Code}: {error.Message}" : string.Empty);

    public static string Cause(string? code) => code switch
    {
        "unreachable" or "invalid_key" or "model_unavailable" => FailureCauses.ModelUnreachable,
        "rejected" or "retry_later" => FailureCauses.ModelRefused,
        "out_of_credit" or RunReasons.StepLimit => FailureCauses.BudgetReached,
        RunRules.StoppedByRule or RunRules.BlockedByRule => FailureCauses.StoppedByRule,
        RunReasons.LeaseLost => FailureCauses.ServerRestarted,
        _ => FailureCauses.Unknown,
    };

    #endregion
}
