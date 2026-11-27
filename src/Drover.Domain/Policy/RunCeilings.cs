namespace Drover.Domain.Policy;

/// <summary>
/// What one run may use before it is stopped: the deployment's ceilings, lowered by the space's own settings, and
/// lowered again by the bot's. Each level only narrows the one above it.
/// </summary>
public sealed record RunCeilings(
    int MaxSteps,
    int MaxTokens,
    int MaxDelegations,
    int MaxUnmeteredModelCalls = DeploymentPolicy.SelfHostedMaxUnmeteredModelCallsPerRun)
{
    public bool CanRemoveUsageLimit { get; init; }

    public bool TokensUnlimited { get; init; }

    /// <summary>The most rule checks by a decision model one run makes. Past it, every judged rule counts as unsure.</summary>
    public int MaxJudgedChecks { get; init; } = int.MaxValue;

    #region Public Methods

    /// <summary>These limits as one bot's runs have them: each the bot's own value where that is lower.</summary>
    public RunCeilings NarrowedBy(RunLimits bot) =>
        new(Lower(bot.MaxSteps, MaxSteps), Lower(bot.MaxTokens, MaxTokens), Lower(bot.MaxDelegations, MaxDelegations), MaxUnmeteredModelCalls)
        {
            TokensUnlimited = CanRemoveUsageLimit && bot.UncappedUsageAcknowledged && bot.MaxTokens is null,
            MaxJudgedChecks = Lower(bot.MaxJudgedChecks, MaxJudgedChecks),
        };

    #endregion

    #region Private Methods

    private static int Lower(int? own, int above) => own is { } value ? Math.Min(value, above) : above;

    #endregion
}
