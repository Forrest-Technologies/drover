namespace Drover.Domain.Rules;

/// <summary>How one check of a judged rule came out, as the run's event log names it.</summary>
public enum RuleJudgment
{
    /// <summary>The decision model was sure enough that the rule is about the action.</summary>
    Applies,

    /// <summary>The decision model was sure enough that the rule is not about the action, or what it requires is done.</summary>
    Passes,

    /// <summary>The probability fell between the two lines, or no check was made. A person is asked, or the reply is held.</summary>
    Unsure,

    /// <summary>The check was made and gave no answer that can be used. Treated as unsure.</summary>
    Failed,
}

/// <summary>
/// What a decision model said about one judged rule: the probability that the rule is about the action, and the
/// probability that what the rule requires before it has been done. Probabilities are null when there is no answer;
/// <see cref="Failure"/> or <see cref="NotChecked"/> then says why, in words that hold none of the checked text.
/// </summary>
public sealed record JudgedVerdict(double? Applies, double? Met)
{
    #region Properties

    /// <summary>The check was made and failed: it timed out, the provider failed, or the answer could not be read.</summary>
    public string? Failure { get; init; }

    /// <summary>No check was made: no decision model is connected, the run's checks are spent, or the server turned them off.</summary>
    public string? NotChecked { get; init; }

    #endregion

    #region Public Methods

    public static JudgedVerdict Failed(string reason) => new(null, null) { Failure = reason };

    public static JudgedVerdict Unchecked(string reason) => new(null, null) { NotChecked = reason };

    #endregion
}

/// <summary>What a verdict comes to for one rule: the judgment, its probability, and the effect it takes, if any.</summary>
public sealed record JudgedSettlement(RuleJudgment Judgment, double? Probability, RuleEffect? Effect, string Why);

/// <summary>
/// Turns a decision model's probabilities into what a judged rule does, in code. Judged rules only narrow: the
/// outcome is nothing, asking a person, refusing, or stopping, never allowing. Whatever is not sure goes to a
/// person: before a tool a person is asked, and before a reply the reply is held back.
/// </summary>
public static class JudgedRules
{
    #region Private Fields

    public const double DefaultConfidence = 0.80;

    public const double MinConfidence = 0.50;

    public const double MaxConfidence = 0.99;

    /// <summary>What a person is told when a rule written as a sentence cannot be saved on this server.</summary>
    public const string TurnedOff =
        "Rules written as a sentence are turned off on this server (deployment rule AllowJudgedRules).";

    public const string NoDecisionModel = "no decision model is connected";

    private const string TurnedOffReason = "rules written as a sentence are turned off on this server";

    #endregion

    #region Public Methods

    /// <summary>
    /// What one judged rule does at a checkpoint, given its verdict. No verdict counts as unsure. A rule that asks
    /// asks when it applies; one that stops stops; one that blocks blocks only when what it requires has not been
    /// shown to be done, so the bot is held to the order of work and no person is needed.
    /// </summary>
    public static JudgedSettlement Settle(ActiveRule rule, RuleCheckpoint checkpoint, JudgedVerdict? verdict)
    {
        var sure = rule.Judged?.Confidence ?? DefaultConfidence;
        var unsure = UnsureEffect(checkpoint);

        if (rule.JudgedOff)
        {
            return new(RuleJudgment.Unsure, null, unsure, $"{TurnedOffReason}, so {Consequence(checkpoint)}");
        }

        if (verdict is null or { Applies: null, Failure: null, NotChecked: null })
        {
            return new(RuleJudgment.Unsure, null, unsure, $"it was not checked, so {Consequence(checkpoint)}");
        }

        if (verdict.Failure is { } failure)
        {
            return new(RuleJudgment.Failed, null, unsure, $"the check failed ({failure}), so {Consequence(checkpoint)}");
        }

        if (verdict.NotChecked is not null || verdict.Applies is not { } applies)
        {
            return new(RuleJudgment.Unsure, null, unsure, $"it was not checked ({verdict.NotChecked}), so {Consequence(checkpoint)}");
        }

        if (applies <= PassLine(sure))
        {
            return new(RuleJudgment.Passes, applies, null, $"the rule does not apply ({Percent(1 - applies)})");
        }

        if (applies < sure)
        {
            return new(RuleJudgment.Unsure, applies, unsure, $"the check was not sure ({Percent(applies)}), so {Consequence(checkpoint)}");
        }

        return rule.Effect switch
        {
            RuleEffect.Block when verdict.Met is { } met && met >= sure =>
                new(RuleJudgment.Passes, applies, null, $"what the rule requires was done first ({Percent(met)})"),
            RuleEffect.Block =>
                new(RuleJudgment.Applies, applies, RuleEffect.Block, $"the rule applies ({Percent(applies)}) and what it requires was not shown to be done first"),
            RuleEffect.Stop or RuleEffect.Ask =>
                new(RuleJudgment.Applies, applies, rule.Effect, $"the rule applies ({Percent(applies)})"),

            // A judged rule never removes text; one stored that way refuses rather than guesses what to remove.
            _ => new(RuleJudgment.Applies, applies, RuleEffect.Block, $"the rule applies ({Percent(applies)})"),
        };
    }

    /// <summary>What a judged rule does when nobody is sure: before a tool a person is asked, elsewhere the thing is held back.</summary>
    public static RuleEffect UnsureEffect(RuleCheckpoint checkpoint) =>
        checkpoint is RuleCheckpoint.BeforeTool ? RuleEffect.Ask : RuleEffect.Block;

    /// <summary>At or below this, the rule does not apply: one minus how sure the rule asks the model to be.</summary>
    public static double PassLine(double confidence) => Math.Round(1 - confidence, 6);

    public static string Percent(double probability) =>
        $"{Math.Round(Math.Clamp(probability, 0, 1) * 100, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)}%";

    #endregion

    #region Private Methods

    private static string Consequence(RuleCheckpoint checkpoint) =>
        checkpoint is RuleCheckpoint.BeforeTool ? "a person is asked" : "it is held back";

    #endregion
}
