namespace Drover.Engine.Rules;

/// <summary>The result of a change to a rule. With neither a rule nor a problem, there is no such bot or rule.</summary>
public sealed record RuleChange(InheritedRule? Rule, string? Problem)
{
    public static RuleChange NotFound { get; } = new(null, null);
}

/// <summary>What trying a sample found. With a problem, the sample or the draft could not be read.</summary>
public sealed record RuleTrial(RuleOutcome? Outcome, string? Problem, bool BotMissing = false);

/// <summary>
/// A bot's rules: reading the ones it follows, changing its own, and trying a sample against them. A rule is
/// checked when it is saved, so nothing that reaches a run needs to be guessed at. Every change is audited.
/// </summary>
public sealed class RuleService(
    BotRepository bots,
    BotRuleRepository rules,
    ToolRepository tools,
    AuditLog audit,
    TimeProvider time,
    ILogger<RuleService> logger)
{
    #region Public Methods

    /// <summary>
    /// The rules a bot follows as the evaluator takes them: switched on, its ancestors' first. This is what a run
    /// loads and what "Try it" uses, so the two cannot differ.
    /// </summary>
    public static IReadOnlyList<ActiveRule> Active(IEnumerable<InheritedRule> followed) =>
        [.. followed.Where(found => found.Rule.Enabled).Select(found => new ActiveRule(
            found.Rule.Id,
            found.Rule.Name,
            found.Rule.Checkpoint,
            found.Rule.Effect,
            found.Rule.Conditions,
            found.Rule.BotId))];

    /// <summary>Every rule the bot follows, inherited ones first, or null when there is no such bot.</summary>
    public async Task<List<InheritedRule>?> ListRules(Guid botId, CancellationToken cancellationToken) =>
        await FindBot(botId, cancellationToken) is null ? null : await rules.ListRulesAlongPath(botId, cancellationToken);

    /// <summary>
    /// The rules as the editor reads them back, each with what a person should know about it as things stand now:
    /// a tool it names that no tool is called (so that condition never holds, and a typo does not pass in silence),
    /// and a list on a named input saved before such a rule had to say which tools it is about.
    /// </summary>
    public async Task<List<BotRuleResponse>> Describe(IReadOnlyList<InheritedRule> found, CancellationToken cancellationToken)
    {
        var known = (await tools.ListAllToolNames(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. found.Select(rule => rule.ToResponse() with { Notes = Notes(rule.Rule, known) })];
    }

    public async Task<RuleChange> Create(Guid botId, BotRuleBody body, Guid actorUserId, CancellationToken cancellationToken)
    {
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
            CreatedByUserId = actorUserId,
        };
        rules.Add(rule);
        Place(own, rule, body.Position ?? own.Count);
        audit.Record(actorUserId, "rule.added", AuditTargets.Bot, botId, null, Snapshot(rule), About(rule));

        logger.LogInformation("Rule {RuleId} added to bot {BotId}", rule.Id, botId);
        return new RuleChange(new InheritedRule(rule, bot.Name, 0), null);
    }

    public async Task<RuleChange> Update(Guid botId, Guid ruleId, BotRuleBody body, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (await FindBot(botId, cancellationToken) is not { } bot)
        {
            return RuleChange.NotFound;
        }

        var own = await rules.GetRules(botId, cancellationToken);

        // Only the bot's own rules are found here, so a rule inherited from a bot above cannot be changed through this bot.
        if (own.Find(candidate => candidate.Id == ruleId) is not { } rule)
        {
            return RuleChange.NotFound;
        }

        if (Read(body) is not { Rule: { } read })
        {
            return new RuleChange(null, Read(body).Problem);
        }

        var before = Snapshot(rule);
        rule.Name = read.Name;
        rule.Enabled = body.Enabled;
        rule.Checkpoint = read.Checkpoint;
        rule.Effect = read.Effect;
        rule.Conditions = [.. read.Conditions];
        own.Remove(rule);
        Place(own, rule, body.Position ?? rule.Position);

        if (audit.Record(actorUserId, "rule.changed", AuditTargets.Bot, botId, before, Snapshot(rule), About(rule)))
        {
            logger.LogInformation("Rule {RuleId} of bot {BotId} changed", rule.Id, botId);
        }

        return new RuleChange(new InheritedRule(rule, bot.Name, 0), null);
    }

    /// <summary>Removes one of the bot's own rules. False when there is no such bot or rule.</summary>
    public async Task<bool> Remove(Guid botId, Guid ruleId, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (await FindBot(botId, cancellationToken) is null)
        {
            return false;
        }

        var own = await rules.GetRules(botId, cancellationToken);

        if (own.Find(candidate => candidate.Id == ruleId) is not { } rule)
        {
            return false;
        }

        own.Remove(rule);
        rules.Remove(rule);
        Renumber(own);
        audit.Record(actorUserId, "rule.removed", AuditTargets.Bot, botId, Snapshot(rule), null, About(rule));

        logger.LogInformation("Rule {RuleId} removed from bot {BotId}", rule.Id, botId);
        return true;
    }

    /// <summary>
    /// What a run would do with a sample, by the evaluator a run uses, over the rules the bot follows. A draft rule
    /// is checked like a saved one and tried as the bot's last.
    /// </summary>
    public async Task<RuleTrial> Try(Guid botId, TryRulesRequest request, CancellationToken cancellationToken)
    {
        if (await FindBot(botId, cancellationToken) is null)
        {
            return new RuleTrial(null, null, BotMissing: true);
        }

        if ((request.Text?.Length ?? 0) + (request.Json?.Length ?? 0) > MaxSampleLength)
        {
            return new RuleTrial(null, $"A sample can be up to {MaxSampleLength:N0} characters. Try a shorter one.");
        }

        if (Words.Read<RuleCheckpoint>(request.Checkpoint) is not { } checkpoint)
        {
            return new RuleTrial(null, "Choose the moment to try: before_tool, after_tool, before_reply, or message_arrives.");
        }

        var active = Active(await rules.ListRulesAlongPath(botId, cancellationToken)).ToList();

        if (request.Draft is { } draft)
        {
            if (Read(draft) is not { Rule: { } read })
            {
                return new RuleTrial(null, Read(draft).Problem);
            }

            active.Add(new ActiveRule(Guid.Empty, read.Name, read.Checkpoint, read.Effect, read.Conditions, botId));
        }

        var atATool = checkpoint is RuleCheckpoint.BeforeTool or RuleCheckpoint.AfterTool;
        var (json, unreadable) = atATool ? ReadJson(request.Json) : (null, null);

        if (unreadable is not null)
        {
            return new RuleTrial(null, unreadable);
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
        return new RuleTrial(RuleEvaluator.Evaluate(active, subject), null);
    }

    #endregion

    #region Private Methods

    private static List<string> Notes(BotRule rule, HashSet<string> knownTools)
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

        return notes;
    }

    private static JsonObject Snapshot(BotRule rule) =>
        AuditLog.Snapshot(new { rule.Name, rule.Enabled, rule.Position, rule.Checkpoint, rule.Effect, rule.Conditions });

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

    private static (ReadRule? Rule, string? Problem) Read(BotRuleBody body)
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

    // Puts the rule at a place among the bot's others and numbers them all from zero, in order.
    private static void Place(List<BotRule> others, BotRule rule, int position)
    {
        others.Insert(Math.Clamp(position, 0, others.Count), rule);
        Renumber(others);
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

    #endregion

    #region Nested Types

    private sealed record ReadRule(string Name, RuleCheckpoint Checkpoint, RuleEffect Effect, List<RuleCondition> Conditions);

    #endregion
}
