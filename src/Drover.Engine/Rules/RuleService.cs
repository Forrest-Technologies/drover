namespace Drover.Engine.Rules;

/// <summary>The result of a change to a rule. With neither a rule nor a problem, there is no such bot or rule.</summary>
public sealed record RuleChange(InheritedRule? Rule, string? Problem)
{
    public static RuleChange NotFound { get; } = new(null, null);
}

/// <summary>What trying a sample found. With a problem, the sample or the draft could not be read.</summary>
public sealed record RuleTrial(RuleOutcome? Outcome, string? Problem, bool BotMissing = false)
{
    #region Properties

    /// <summary>Whether a rule tried at this moment looks for something in text, whether or not any rule applied.</summary>
    public bool TriedAPattern { get; init; }

    /// <summary>
    /// How the check of the rule written as a sentence came out: the draft's when the draft is one, otherwise the
    /// deciding rule's, otherwise the first one checked. Null when no such rule was tried.
    /// </summary>
    public JudgedSettlement? Judged { get; init; }

    #endregion
}

/// <summary>
/// A bot's rules: reading the ones it follows, changing its own, and trying a sample against them. A rule is
/// checked when it is saved, so nothing that reaches a run needs to be guessed at. Every change is audited.
/// </summary>
public sealed class RuleService(
    BotRepository bots,
    BotRuleRepository rules,
    ToolRepository tools,
    PolicyService policy,
    AuditLog audit,
    TenantScope scope,
    TenantContext tenant,
    FindDecisionModel findDecisionModel,
    TimeProvider time,
    ILogger<RuleService> logger)
{
    #region Public Methods

    /// <summary>
    /// The rules a bot follows as the evaluator takes them: switched on, its ancestors' first. This is what a run
    /// loads and what "Try it" uses, so the two cannot differ.
    /// </summary>
    public static IReadOnlyList<ActiveRule> Active(IEnumerable<InheritedRule> followed, DeploymentPolicy deployment) =>
        [.. followed.Where(found => found.Rule.Enabled).Select(found => new ActiveRule(
            found.Rule.Id,
            found.Rule.Name,
            found.Rule.Checkpoint,
            found.Rule.Effect,
            found.Rule.Conditions,
            found.Rule.BotId)
        {
            WrittenPatternsOff = !deployment.AllowWrittenRulePatterns,
            JudgedOff = !deployment.AllowJudgedRules,
        })];

    /// <summary>Whether a rule on this server may look for text with a pattern a person wrote.</summary>
    public bool AllowsWrittenPatterns => policy.Deployment.AllowWrittenRulePatterns;

    /// <summary>Whether a rule on this server may be written as a sentence that a decision model judges.</summary>
    public bool AllowsJudgedRules => policy.Deployment.AllowJudgedRules;

    /// <summary>Every rule the bot follows, inherited ones first, or null when there is no such bot.</summary>
    public async Task<List<InheritedRule>?> ListRules(Guid botId, CancellationToken cancellationToken) =>
        await FindBot(botId, cancellationToken) is null ? null : await rules.ListRulesAlongPath(botId, cancellationToken);

    /// <summary>
    /// The rules as the editor reads them back, each with what a person should know about it as things stand now:
    /// a tool it names that no tool is called (so that condition never holds, and a typo does not pass in silence),
    /// a list on a named input saved before such a rule had to say which tools it is about, and a written pattern
    /// that this server does not run or that is refused now.
    /// </summary>
    public async Task<List<BotRuleResponse>> Describe(IReadOnlyList<InheritedRule> found, CancellationToken cancellationToken)
    {
        var known = (await tools.ListAllToolNames(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var decisionModel = AllowsJudgedRules && found.Any(rule => rule.Rule.Conditions.Any(condition => condition.IsJudged)) && tenant is { SpaceId: { } spaceId, UserId: { } userId }
            ? await findDecisionModel(spaceId, userId, cancellationToken)
            : null;
        return [.. found.Select(rule => rule.ToResponse() with { Notes = Notes(rule.Rule, known, AllowsWrittenPatterns, AllowsJudgedRules, decisionModel is not null) })];
    }

    public Task<RuleChange> Create(Guid botId, BotRuleBody body, Guid actorUserId, CancellationToken cancellationToken) =>
        Create(botId, body, AuditActor.Person(actorUserId), cancellationToken);

    internal async Task<RuleChange> Create(Guid botId, BotRuleBody body, AuditActor actor, CancellationToken cancellationToken)
    {
        // Rule writes share the tree lock with a remove that copies rules up. Without it a rule saved while a bot
        // is removed can land on a bot that no longer binds anyone, or be lost from the copies the move makes.
        await bots.LockTree(cancellationToken);

        if (await FindBot(botId, cancellationToken) is not { } bot)
        {
            return RuleChange.NotFound;
        }

        if (Read(body) is not { Rule: { } read })
        {
            return new RuleChange(null, Read(body).Problem);
        }

        var own = await rules.GetRules(botId, cancellationToken);

        if (own.Count >= RuleValidator.MaxRulesPerBot)
        {
            return new RuleChange(null, $"A bot has at most {RuleValidator.MaxRulesPerBot} rules of its own. Remove one first.");
        }

        var rule = new BotRule
        {
            BotId = botId,
            Name = read.Name,
            Enabled = body.Enabled,
            Checkpoint = read.Checkpoint,
            Effect = read.Effect,
            Conditions = [.. read.Conditions],
            CreatedByUserId = actor.UserId,
            ManagingBotId = actor.BotId,
        };
        rules.Add(rule);
        Place(own, rule, body.Position ?? own.Count);
        audit.Record(actor, "rule.added", AuditTargets.Bot, botId, null, Snapshot(rule), About(rule));

        logger.LogInformation("Rule {RuleId} added to bot {BotId}", rule.Id, botId);
        return new RuleChange(new InheritedRule(rule, bot.Name, 0), null);
    }

    public Task<RuleChange> Update(Guid botId, Guid ruleId, BotRuleBody body, Guid actorUserId, CancellationToken cancellationToken) =>
        Update(botId, ruleId, body, AuditActor.Person(actorUserId), cancellationToken);

    internal async Task<RuleChange> Update(Guid botId, Guid ruleId, BotRuleBody body, AuditActor actor, CancellationToken cancellationToken)
    {
        await bots.LockTree(cancellationToken);

        if (await FindBot(botId, cancellationToken) is not { } bot)
        {
            return RuleChange.NotFound;
        }

        var own = await rules.GetRules(botId, cancellationToken);

        // Only the bot's own rules are found here, so a rule inherited from a bot above cannot be changed through this bot.
        if (own.Find(candidate => candidate.Id == ruleId) is not { } rule
            || !await MayChange(rule, actor, cancellationToken))
        {
            return RuleChange.NotFound;
        }

        if (Read(body) is not { Rule: { } read })
        {
            return new RuleChange(null, Read(body).Problem);
        }

        var before = Snapshot(rule);
        rule.ManagingBotId = actor.BotId;
        rule.Name = read.Name;
        rule.Enabled = body.Enabled;
        rule.Checkpoint = read.Checkpoint;
        rule.Effect = read.Effect;
        rule.Conditions = [.. read.Conditions];
        own.Remove(rule);
        Place(own, rule, body.Position ?? rule.Position);

        if (audit.Record(actor, "rule.changed", AuditTargets.Bot, botId, before, Snapshot(rule), About(rule)))
        {
            logger.LogInformation("Rule {RuleId} of bot {BotId} changed", rule.Id, botId);
        }

        return new RuleChange(new InheritedRule(rule, bot.Name, 0), null);
    }

    /// <summary>Removes one of the bot's own rules. False when there is no such bot or rule.</summary>
    public Task<bool> Remove(Guid botId, Guid ruleId, Guid actorUserId, CancellationToken cancellationToken) =>
        Remove(botId, ruleId, AuditActor.Person(actorUserId), cancellationToken);

    internal async Task<bool> Remove(Guid botId, Guid ruleId, AuditActor actor, CancellationToken cancellationToken)
    {
        await bots.LockTree(cancellationToken);

        if (await FindBot(botId, cancellationToken) is null)
        {
            return false;
        }

        var own = await rules.GetRules(botId, cancellationToken);

        if (own.Find(candidate => candidate.Id == ruleId) is not { } rule
            || !await MayChange(rule, actor, cancellationToken))
        {
            return false;
        }

        own.Remove(rule);
        rules.Remove(rule);
        Renumber(own);
        audit.Record(actor, "rule.removed", AuditTargets.Bot, botId, Snapshot(rule), null, About(rule));

        logger.LogInformation("Rule {RuleId} removed from bot {BotId}", rule.Id, botId);
        return true;
    }

    /// <summary>
    /// What a run would do with a sample, by the evaluator a run uses, over the rules the bot follows. A draft rule
    /// is checked like a saved one and tried as the bot's last. A rule written as a sentence goes through the same two
    /// steps as in a run: the rules are read in a short scope, the space's decision model is asked with no scope open,
    /// and the evaluator decides. The caller opens no scope: this opens its own.
    /// </summary>
    public async Task<RuleTrial> Try(Guid botId, TryRulesRequest request, CancellationToken cancellationToken)
    {
        RuleSample sample;

        await using (var work = await scope.Begin(cancellationToken))
        {
            var (read, problem) = await ReadSample(botId, request, cancellationToken);
            await work.Commit(cancellationToken);

            if (read is null)
            {
                return problem!;
            }

            sample = read;
        }

        var due = RuleEvaluator.FindJudgmentsDue(sample.Rules, sample.Subject);
        var verdicts = due.Rules.Count == 0 ? new Dictionary<ActiveRule, JudgedVerdict>() : await Judge(due, sample.Subject, cancellationToken);
        var outcome = RuleEvaluator.Evaluate(sample.Rules, sample.Subject, verdicts);
        var tried = new[] { sample.Draft, outcome?.Rule }.FirstOrDefault(rule => rule is { Judged: not null } && due.Rules.Contains(rule))
            ?? (due.Rules.Count > 0 ? due.Rules[0] : null);
        return new RuleTrial(outcome, null)
        {
            TriedAPattern = sample.Rules.Any(rule => rule.Checkpoint == sample.Subject.Checkpoint && rule.Conditions.Any(condition => condition.IsPattern)),
            Judged = tried is null ? null : JudgedRules.Settle(tried, sample.Subject.Checkpoint, verdicts.GetValueOrDefault(tried)),
        };
    }

    /// <summary>
    /// What is wrong with a rule as it is written, or null. Whether this server takes a written pattern is not
    /// asked here: that is decided when the rule is added to a bot.
    /// </summary>
    public static string? FindProblem(BotRuleBody body) => ReadWords(body).Problem;

    /// <summary>The rule as an audit entry keeps it.</summary>
    public static JsonObject Snapshot(BotRule rule) =>
        AuditLog.Snapshot(new
        {
            rule.Name,
            rule.Enabled,
            rule.Position,
            rule.Checkpoint,
            rule.Effect,
            rule.Conditions,
            rule.ManagingBotId,
        });

    /// <summary>
    /// Puts the rule at a place among the bot's others and numbers them all from zero, in order. Shared with
    /// BotService, which copies rules when a bot is kept and its children move up.
    /// </summary>
    public static void Place(List<BotRule> others, BotRule rule, int position)
    {
        others.Insert(Math.Clamp(position, 0, others.Count), rule);
        Renumber(others);
    }

    #endregion

    #region Private Methods

    // A bot may change a rule its own branch controls. An edit by a person or an ancestor transfers control
    // to that actor, so a bot farther down cannot undo their change in a later run.
    internal async Task<bool> MayChange(BotRule rule, AuditActor actor, CancellationToken cancellationToken) =>
        actor.BotId is not { } caller
        || (rule.ManagingBotId is { } author && (author == caller || await bots.IsBeneath(author, caller, cancellationToken)));

    // The sample as the evaluator takes it, with the rules the bot follows and the draft, or why it cannot be tried.
    private async Task<(RuleSample? Sample, RuleTrial? Problem)> ReadSample(Guid botId, TryRulesRequest request, CancellationToken cancellationToken)
    {
        if (await FindBot(botId, cancellationToken) is null)
        {
            return (null, new RuleTrial(null, null, BotMissing: true));
        }

        if ((request.Text?.Length ?? 0) + (request.Json?.Length ?? 0) > MaxSampleLength)
        {
            return (null, new RuleTrial(null, $"A sample can be up to {MaxSampleLength:N0} characters. Try a shorter one."));
        }

        if (Words.Read<RuleCheckpoint>(request.Checkpoint) is not { } checkpoint)
        {
            return (null, new RuleTrial(null, "Choose the moment to try: before_tool, after_tool, before_reply, or message_arrives."));
        }

        var active = Active(await rules.ListRulesAlongPath(botId, cancellationToken), policy.Deployment).ToList();
        ActiveRule? drafted = null;

        if (request.Draft is { } draft)
        {
            if (Read(draft) is not { Rule: { } read })
            {
                return (null, new RuleTrial(null, Read(draft).Problem));
            }

            drafted = new ActiveRule(Guid.Empty, read.Name, read.Checkpoint, read.Effect, read.Conditions, botId)
            {
                WrittenPatternsOff = !AllowsWrittenPatterns,
                JudgedOff = !AllowsJudgedRules,
            };
            active.Add(drafted);
        }

        var atATool = checkpoint is RuleCheckpoint.BeforeTool or RuleCheckpoint.AfterTool;
        var (json, unreadable) = atATool ? ReadJson(request.Json) : (null, null);

        if (unreadable is not null)
        {
            return (null, new RuleTrial(null, unreadable));
        }

        var tool = atATool && !string.IsNullOrWhiteSpace(request.Tool) ? await tools.FindToolByName(request.Tool.Trim(), cancellationToken) : null;
        var subject = new RuleSubject
        {
            Checkpoint = checkpoint,
            ToolName = atATool ? request.Tool?.Trim() : null,
            ToolKind = tool?.Sensitivity,
            Json = json,
            Text = atATool ? null : request.Text ?? string.Empty,
            Now = time.GetUtcNow(),
        };
        return (new RuleSample(active, subject, drafted), null);
    }

    // "Try it" asks the space's decision model as a run does, with the sample as the action and nothing else: a
    // sample has no person's messages and no tool results.
    private async Task<IReadOnlyDictionary<ActiveRule, JudgedVerdict>> Judge(JudgmentsDue due, RuleSubject subject, CancellationToken cancellationToken)
    {
        var model = tenant is { SpaceId: { } spaceId, UserId: { } userId } ? await findDecisionModel(spaceId, userId, cancellationToken) : null;
        var checks = new JudgedChecks(policy.Deployment.MaxJudgedRuleSeconds, time, text => text, logger);
        var action = subject.Checkpoint is RuleCheckpoint.BeforeTool
            ? JsonSerializer.Serialize(new { tool = subject.ToolName, kind = subject.ToolKind is { } kind ? Words.Of(kind) : null, arguments = due.Hard?.RedactedJson ?? subject.Json })
            : due.Hard?.RedactedText ?? subject.Text ?? string.Empty;
        return (await checks.Check(model, due.Rules, new JudgedState(action, string.Empty, string.Empty), cancellationToken)).Verdicts;
    }

    private static List<string> Notes(BotRule rule, HashSet<string> knownTools, bool writtenPatterns, bool judgedRules, bool hasDecisionModel)
    {
        List<string> notes =
        [
            .. rule.Conditions
                .Where(condition => condition.Kind is RuleConditionKind.ToolIs)
                .SelectMany(condition => condition.Tools)
                .Where(tool => !knownTools.Contains(tool))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(tool => $"No tool has that name now: {tool}. The rule does not apply to it until one does. Check the spelling."),
        ];

        if (rule.Conditions.Any(condition => condition is { Kind: RuleConditionKind.NotInList, Argument: not null })
            && !rule.Conditions.Any(condition => condition.Kind is RuleConditionKind.ToolIs))
        {
            notes.Add("This rule checks a named input and does not say for which tools, so it does not apply when the input is missing. Change it to add the tools it is about.");
        }

        if (rule.Conditions.Any(condition => condition.IsJudged))
        {
            notes.Add(JudgedNote(judgedRules, hasDecisionModel));
        }

        foreach (var condition in rule.Conditions.Where(condition => condition.Kind is RuleConditionKind.TextMatches))
        {
            // A rule saved before patterns were turned off, or before a pattern like its own was refused, is not run
            // as written: it takes its effect on everything it looks at. The person who reads it back is told.
            if (!writtenPatterns)
            {
                notes.Add($"{WrittenPatternsOff} Until this rule is changed it applies to everything its other conditions allow.");
                break;
            }

            if (RulePatterns.Compile(condition.Pattern).Problem is { } refused)
            {
                notes.Add($"{refused} Until the pattern is changed, this rule applies to everything its other conditions allow.");
            }
        }

        return notes;
    }

    // A judged rule is not a guarantee, and one that cannot be checked goes to a person: the read-back says which.
    private static string JudgedNote(bool judgedRules, bool hasDecisionModel) =>
        (judgedRules, hasDecisionModel) switch
        {
            (false, _) => $"{JudgedRules.TurnedOff} Until that changes, this rule is never checked: before a tool a person is asked, and before a reply the reply is held back.",
            (_, false) => "No decision model is connected, so this rule cannot be checked: before a tool a person is asked, and before a reply the reply is held back.",
            _ => "A decision model checks this rule. It is judged, not guaranteed: never let it be the only thing between a bot and a tool that cannot be undone.",
        };

    private static JsonObject About(BotRule rule) => new() { ["rule_id"] = rule.Id };

    // The sample's JSON, or why it cannot be read. Nothing is an empty object: a tool called with no arguments.
    private static (JsonElement? Json, string? Problem) ReadJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (JsonSerializer.SerializeToElement(new { }), null);
        }

        try
        {
            return (JsonElement.Parse(text), null);
        }
        catch (JsonException unreadable)
        {
            return (null, $"Write the tool's arguments or result as JSON, such as {{\"to\": \"sam@example.org\"}}. {unreadable.Message}");
        }
    }

    private (ReadRule? Rule, string? Problem) Read(BotRuleBody body)
    {
        var (rule, problem) = ReadWords(body);

        if (rule is not null && !AllowsJudgedRules && rule.Conditions.Any(condition => condition.IsJudged))
        {
            return (null, JudgedRules.TurnedOff);
        }

        return rule is not null && !AllowsWrittenPatterns && rule.Conditions.Any(condition => condition.Kind is RuleConditionKind.TextMatches)
            ? (null, WrittenPatternsOff)
            : (rule, problem);
    }

    private static (ReadRule? Rule, string? Problem) ReadWords(BotRuleBody body)
    {
        if (Words.Read<RuleCheckpoint>(body.Checkpoint) is not { } checkpoint)
        {
            return (null, "Choose when the rule is checked: before_tool, after_tool, before_reply, or message_arrives.");
        }

        if (Words.Read<RuleEffect>(body.Effect) is not { } effect)
        {
            return (null, "Choose what the rule does: redact, ask, block, or stop.");
        }

        List<RuleCondition> conditions = [.. (body.Conditions ?? []).Select(condition => condition.ToCondition())];

        return RuleValidator.FindProblem(body.Name, checkpoint, effect, conditions) is { } problem
            ? (null, problem)
            : (new ReadRule(body.Name!.Trim(), checkpoint, effect, conditions), null);
    }

    private static void Renumber(List<BotRule> ordered)
    {
        for (var index = 0; index < ordered.Count; index++)
        {
            ordered[index].Position = index;
        }
    }

    private async Task<Bot?> FindBot(Guid botId, CancellationToken cancellationToken) =>
        await bots.FindBot(botId, cancellationToken) is { Status: not BotStatus.Removed } bot ? bot : null;

    #endregion

    #region Limits

    /// <summary>The longest sample "Try it" takes. A run's text has no such cap; the evaluator's time budget bounds both.</summary>
    public const int MaxSampleLength = 20_000;

    /// <summary>What a person is told when a rule has a written pattern and the server does not run them.</summary>
    public const string WrittenPatternsOff =
        "Patterns a person writes are turned off on this server (deployment rule AllowWrittenRulePatterns). Look for text with \"contains\" or a built-in kind.";

    #endregion

    #region Nested Types

    private sealed record ReadRule(string Name, RuleCheckpoint Checkpoint, RuleEffect Effect, List<RuleCondition> Conditions);

    private sealed record RuleSample(List<ActiveRule> Rules, RuleSubject Subject, ActiveRule? Draft);

    #endregion
}
