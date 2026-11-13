namespace Drover.Engine.Runs;

/// <summary>
/// The bot's rules inside one run. Each check reads the rules as they are now, in a short transaction, and decides
/// with the one evaluator; a rule that applies is written to the run's event log, without the text that matched.
/// </summary>
public sealed class RunRules(RunSession session, ILogger logger)
{
    #region Public Methods

    /// <summary>The rules the run's bot follows right now: its own and those of every bot above it, switched on.</summary>
    public Task<IReadOnlyList<ActiveRule>> Load(CancellationToken cancellationToken) =>
        session.Tenant.Transact(
            async services => RuleService.Active(await services.GetRequiredService<BotRuleRepository>().ListRulesAlongPath(session.BotId, cancellationToken)),
            cancellationToken);

    /// <summary>
    /// What the rules say about a subject, or null when none applies, written to the run's event log.
    /// </summary>
    public async Task<RuleOutcome?> Check(RuleSubject subject, Guid? toolCallId, CancellationToken cancellationToken)
    {
        var outcome = await Find(subject, cancellationToken);

        if (outcome is not null)
        {
            await Record(outcome, subject.Checkpoint, toolCallId);
        }

        return outcome;
    }

    /// <summary>What the rules say about a subject, without writing it down. The run's clock and counts are filled in here.</summary>
    public async Task<RuleOutcome?> Find(RuleSubject subject, CancellationToken cancellationToken)
    {
        var rules = await Load(cancellationToken);

        return rules.Count == 0
            ? null
            : RuleEvaluator.Evaluate(rules, subject with
            {
                Now = session.Now,
                ToolCalls = session.ToolCallCount,
                Tokens = session.Usage.InputTokens + session.Usage.OutputTokens,
            });
    }

    /// <summary>Writes a rule that applied to the run's event log and the server log. Neither gets the text that matched.</summary>
    public async Task Record(RuleOutcome outcome, RuleCheckpoint checkpoint, Guid? toolCallId)
    {
        await session.Writer.Append(
            session.RunId,
            RunEventTypes.RuleFired,
            new RuleFired(outcome.Rule.Id, outcome.Rule.Name, Words.Of(checkpoint), Words.Of(outcome.Effect), outcome.Why, toolCallId));
        logger.LogInformation(
            "Rule {RuleId} applied in run {RunId} at {Checkpoint}: {Effect}",
            outcome.Rule.Id,
            session.RunId,
            checkpoint,
            outcome.Effect);
    }

    /// <summary>
    /// Checks a person's messages before the model sees them. Every message is checked, also after one was refused:
    /// each that a rule blocks or stops is marked withheld, so no later run reads it either, and the run ends. The
    /// sentence the run fails with is returned, or null when every message may pass.
    /// </summary>
    public async Task<string?> CheckArrivals(IReadOnlyList<Message> messages, CancellationToken cancellationToken)
    {
        string? refusal = null;
        var stopped = false;

        foreach (var message in messages.Where(message => !message.Withheld))
        {
            var text = string.Join('\n', message.Parts.OfType<TextPart>().Select(part => part.Text));
            var outcome = await Check(new RuleSubject { Checkpoint = RuleCheckpoint.MessageArrives, Text = text }, null, cancellationToken);

            if (outcome is null)
            {
                continue;
            }

            await session.Tenant.Transact(
                services => services.GetRequiredService<MessageRepository>().MarkWithheld(message.Id, CancellationToken.None),
                CancellationToken.None);
            refusal ??= $"The rule \"{outcome.Rule.Name}\" did not let this message through: {outcome.Why}.";
            stopped |= outcome.Effect is RuleEffect.Stop;
        }

        if (refusal is not null)
        {
            session.EndByRule(stopped ? StoppedByRule : BlockedByRule, refusal);
        }

        return refusal;
    }

    #endregion

    #region Reasons

    public const string StoppedByRule = "stopped_by_rule";

    public const string BlockedByRule = "blocked_by_rule";

    #endregion
}
