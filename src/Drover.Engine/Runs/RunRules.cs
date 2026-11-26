namespace Drover.Engine.Runs;

/// <summary>
/// The bot's rules inside one run. Each check reads the rules as they are now, in a short transaction, and decides
/// with the one evaluator; a rule that applies is written to the run's event log, without the text that matched.
/// Rules written as a sentence are decided in two steps around one request to the space's decision model, made with
/// no transaction open: the evaluator says which still need a verdict, the model is asked, and the evaluator decides.
/// </summary>
public sealed class RunRules(RunSession session, JudgedChecks judged, ILogger logger)
{
    #region Private Fields

    private const int PersonMessages = 10;

    private const int EvidenceCalls = 20;

    private const int EvidencePerCall = 1_000;

    private static readonly IReadOnlyDictionary<ActiveRule, JudgedVerdict> noVerdicts = new Dictionary<ActiveRule, JudgedVerdict>();

    #endregion

    #region Public Methods

    /// <summary>The rules the run's bot follows right now: its own and those of every bot above it, switched on.</summary>
    public Task<IReadOnlyList<ActiveRule>> Load(CancellationToken cancellationToken) =>
        session.Tenant.Transact(
            async services => RuleService.Active(
                await services.GetRequiredService<BotRuleRepository>().ListRulesAlongPath(session.BotId, cancellationToken),
                services.GetRequiredService<PolicyService>().Deployment),
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
    public Task<RuleOutcome?> Find(RuleSubject subject, CancellationToken cancellationToken) =>
        Find(subject, null, null, cancellationToken);

    /// <summary>
    /// What the rules say about a subject. A rule written as a sentence is checked only when
    /// <paramref name="mayBeJudged"/> agrees, or is not given: a call that its grant or the bot's state refuses is never
    /// sent to the decision model. The check is written to the run's event log as <c>rule.checked</c>.
    /// </summary>
    public async Task<RuleOutcome?> Find(
        RuleSubject subject,
        Guid? toolCallId,
        Func<CancellationToken, Task<bool>>? mayBeJudged,
        CancellationToken cancellationToken)
    {
        var rules = await Load(cancellationToken);

        if (rules.Count == 0)
        {
            return null;
        }

        var filled = subject with
        {
            Now = session.Now,
            ToolCalls = session.ToolCallCount,
            Tokens = session.Usage.InputTokens + session.Usage.OutputTokens,
        };
        var due = RuleEvaluator.FindJudgmentsDue(rules, filled);
        var verdicts = due.Rules.Count == 0 || (mayBeJudged is not null && !await mayBeJudged(cancellationToken))
            ? noVerdicts
            : await Judge(due, filled, toolCallId, cancellationToken);
        return RuleEvaluator.Evaluate(rules, filled, verdicts);
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

    #region Judging

    // One request for every rule due, counted against the run's checks. With no model, or past the run's checks, no
    // request is made and every rule is unsure. Each rule's check is written down without anything that was sent.
    private async Task<IReadOnlyDictionary<ActiveRule, JudgedVerdict>> Judge(
        JudgmentsDue due,
        RuleSubject subject,
        Guid? toolCallId,
        CancellationToken cancellationToken)
    {
        JudgedCheck check;

        if (judged.Model is not { } model)
        {
            check = new(JudgedChecks.NotChecked(due.Rules, JudgedRules.NoDecisionModel), 0, 0);
        }
        else if (session.Usage.JudgedChecks >= session.Limits.MaxJudgedChecks)
        {
            check = new(JudgedChecks.NotChecked(due.Rules, "the run has made every rule check it may"), 0, 0);
        }
        else
        {
            var state = await State(due.Hard, subject, cancellationToken);
            session.CountJudgedCheck();
            check = await judged.Check(due.Rules, state, cancellationToken);
            await session.RecordModelUsage(new ToolModelUsage(model.ConnectionId, model.Model, model.Pricing, check.InputTokens, 0));
        }

        foreach (var rule in due.Rules.Distinct())
        {
            var settled = JudgedRules.Settle(rule, subject.Checkpoint, check.Verdicts.GetValueOrDefault(rule));
            await session.Writer.Append(
                session.RunId,
                RunEventTypes.RuleChecked,
                new RuleChecked(rule.Id, Words.Of(subject.Checkpoint), Words.Of(settled.Judgment), settled.Probability, check.ElapsedMs, toolCallId));
            logger.LogInformation(
                "Rule {RuleId} checked in run {RunId} at {Checkpoint}: {Judgment}",
                rule.Id,
                session.RunId,
                subject.Checkpoint,
                settled.Judgment);
        }

        return check.Verdicts;
    }

    // What a check is about, read in a short transaction that is closed before the model is asked.
    private async Task<JudgedState> State(RuleOutcome? hard, RuleSubject subject, CancellationToken cancellationToken)
    {
        var (person, evidence) = await session.Tenant.Transact(
            async services =>
            {
                var messages = await services.GetRequiredService<MessageRepository>().ListLatestMessages(session.Thread.Id, PersonMessages * 3, cancellationToken);
                var calls = await services.GetRequiredService<ToolCallRepository>().ListLatestFinishedCalls(session.RunId, EvidenceCalls, cancellationToken);
                return (messages, calls);
            },
            cancellationToken);
        return new(Action(hard, subject), Person(person), Evidence(evidence));
    }

    // The action as the rules decided by code left it: what was removed is not sent.
    private static string Action(RuleOutcome? hard, RuleSubject subject) =>
        subject.Checkpoint is RuleCheckpoint.BeforeTool
            ? JsonSerializer.Serialize(new
            {
                tool = subject.ToolName,
                kind = subject.ToolKind is { } kind ? Words.Of(kind) : null,
                arguments = hard?.RedactedJson ?? subject.Json,
            })
            : hard?.RedactedText ?? subject.Text ?? string.Empty;

    // The run's person's own words, the latest last. Nobody else's message, and no bot's, is sent.
    private string Person(IReadOnlyList<Message> messages) =>
        string.Join(
            "\n",
            messages
                .Where(message => message is { Role: MessageRole.User, Withheld: false } && message.AuthorUserId == session.Tenant.UserId)
                .TakeLast(PersonMessages)
                .Select(message => string.Join(' ', message.Parts.OfType<TextPart>().Select(part => part.Text))));

    // Each tool by its catalog name, as the action names it.
    private string Evidence(IReadOnlyList<ToolCall> calls) =>
        string.Join(
            "\n",
            calls.Select(call => $"{session.FindTool(call.ToolId)?.Tool.Name ?? call.ToolName}: {Shorten(call.Result is { } result ? result.GetRawText() : call.ResultFileId is null ? "no result" : "a file")}"));

    private static string Shorten(string text) => text.Length <= EvidencePerCall ? text : $"{text[..EvidencePerCall]}[cut]";

    #endregion

    #region Reasons

    public const string StoppedByRule = "stopped_by_rule";

    public const string BlockedByRule = "blocked_by_rule";

    #endregion
}
