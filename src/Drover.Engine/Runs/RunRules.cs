namespace Drover.Engine.Runs;

/// <summary>
/// The bot's rules inside one run. Each check reads the rules as they are now, in a short transaction, and decides
/// with the one evaluator; a rule that applies is written to the run's event log, without the text that matched.
/// </summary>
public sealed class RunRules(RunSession session, ILogger logger)
{
    #region Private Fields

    public const string StoppedByRule = "stopped_by_rule";

    public const string BlockedByRule = "blocked_by_rule";

    #endregion

    #region Public Methods

    /// <summary>The rules the run's bot follows right now: its own and those of every bot above it, switched on.</summary>
    public Task<IReadOnlyList<ActiveRule>> Load(CancellationToken cancellationToken) =>
        session.Tenant.Transact(
            async services => RuleService.Active(await services.GetRequiredService<BotRuleRepository>().ListRulesAlongPath(session.BotId, cancellationToken)),
            cancellationToken);

    /// <summary>
    /// What the rules say about a subject, or null when none applies. The run's clock and counts are filled in here.
    /// </summary>
    public async Task<RuleOutcome?> Check(RuleSubject subject, Guid? toolCallId, CancellationToken cancellationToken)
    {
        var rules = await Load(cancellationToken);

        if (rules.Count == 0)
        {
            return null;
        }

        var outcome = RuleEvaluator.Evaluate(rules, subject with
        {
            Now = session.Now,
            ToolCalls = session.ToolCallCount,
            Tokens = session.Usage.InputTokens + session.Usage.OutputTokens,
        });

        if (outcome is null)
        {
            return null;
        }

        await session.Writer.Append(
            session.RunId,
            RunEventTypes.RuleFired,
            new RuleFired(outcome.Rule.Id, outcome.Rule.Name, Words.Of(subject.Checkpoint), Words.Of(outcome.Effect), outcome.Why, toolCallId));
        logger.LogInformation(
            "Rule {RuleId} applied in run {RunId} at {Checkpoint}: {Effect}",
            outcome.Rule.Id,
            session.RunId,
            subject.Checkpoint,
            outcome.Effect);
        return outcome;
    }

    /// <summary>
    /// Checks a person's messages before the model sees them. A rule that blocks or stops ends the run; the sentence
    /// the run fails with is returned, or null when every message may pass.
    /// </summary>
    public async Task<string?> CheckArrivals(IEnumerable<string> texts, CancellationToken cancellationToken)
    {
        foreach (var text in texts)
        {
            var outcome = await Check(new RuleSubject { Checkpoint = RuleCheckpoint.MessageArrives, Text = text }, null, cancellationToken);

            if (outcome is not null)
            {
                var message = $"The rule \"{outcome.Rule.Name}\" did not let this message through: {outcome.Why}.";
                session.EndByRule(outcome.Effect is RuleEffect.Stop ? StoppedByRule : BlockedByRule, message);
                return message;
            }
        }

        return null;
    }

    #endregion
}
