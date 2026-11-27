namespace Drover.Engine.Rules;

/// <summary>
/// Finds the decision model of a space, or null when it has none. It only reads and never waits on a provider: a run
/// and "Try it" ask it outside any tenant scope, and the read-back of a bot's rules asks it inside the request's.
/// Until a space can connect one, the server's own finder answers null for every space.
/// </summary>
public delegate Task<SpaceDecisionModel?> FindDecisionModel(Guid spaceId, CancellationToken cancellationToken);

/// <summary>The decision model of a space, as a rule check uses it. The connection is null until a space can connect one.</summary>
public sealed record SpaceDecisionModel(IDecisionModel Client, string Model, Guid? ConnectionId)
{
    #region Properties

    public ModelPricing Pricing { get; init; } = new();

    #endregion
}

/// <summary>
/// What one check is about, before it is redacted and bounded: the action (a tool's name, kind and arguments as the
/// rules decided by code left them, or a reply), the person's own recent messages, and what the run's tools returned.
/// </summary>
public sealed record JudgedState(string Action, string Person, string Evidence);

/// <summary>What one check came to: a verdict for every rule it was asked about, the input tokens used, and the time it took.</summary>
public sealed record JudgedCheck(IReadOnlyDictionary<ActiveRule, JudgedVerdict> Verdicts, long InputTokens, long ElapsedMs);

/// <summary>
/// One check of the judged rules due at a checkpoint: all of them go out as one request, two yes or no questions a
/// rule, and the answers come back as verdicts. The request is redacted and each part bounded; the call is made with
/// no tenant scope open and is cut off at <c>MaxJudgedRuleSeconds</c>. Nothing here fails: a timeout, a provider
/// error and an answer that cannot be read each come back as a failed verdict, which code treats as unsure.
/// </summary>
public sealed class JudgedChecks(SpaceDecisionModel? model, int maxSeconds, TimeProvider time, Func<string, string> redact, ILogger logger)
{
    #region Private Fields

    public const int MaxActionCharacters = 8_000;

    public const int MaxPersonCharacters = 4_000;

    public const int MaxEvidenceCharacters = 8_000;

    public const string AppliesQuestion = "applies";

    public const string MetQuestion = "met";

    private const string Cut = "[cut]";

    private const string Unreadable = "the answer could not be read";

    #endregion

    #region Properties

    /// <summary>The model checks are sent to, or null when the space has none.</summary>
    public SpaceDecisionModel? Model => model;

    #endregion

    #region Public Methods

    /// <summary>Verdicts for rules that were not checked, each saying why in the same words.</summary>
    public static IReadOnlyDictionary<ActiveRule, JudgedVerdict> NotChecked(IReadOnlyList<ActiveRule> rules, string reason) =>
        rules.Distinct().ToDictionary(rule => rule, _ => JudgedVerdict.Unchecked(reason));

    /// <summary>Asks the decision model about every rule due, in one request.</summary>
    public async Task<JudgedCheck> Check(IReadOnlyList<ActiveRule> due, JudgedState state, CancellationToken cancellationToken)
    {
        if (model is null)
        {
            return new(NotChecked(due, JudgedRules.NoDecisionModel), 0, 0);
        }

        if (TenantScope.IsOpenInCurrentFlow)
        {
            throw new InvalidOperationException("A rule check was asked for inside a tenant transaction. Close the scope before the call.");
        }

        var rules = due.Distinct().ToList();
        var request = BuildRequest(rules, state, redact);
        var started = time.GetTimestamp();
        var (answer, failure) = await Ask(request, cancellationToken);
        var elapsed = (long)time.GetElapsedTime(started).TotalMilliseconds;
        var verdicts = failure is null && answer is not null
            ? Read(rules, answer)
            : rules.ToDictionary(rule => rule, _ => JudgedVerdict.Failed(failure ?? Unreadable));
        return new(verdicts, Math.Max(0, answer?.InputTokens ?? 0), elapsed);
    }

    /// <summary>
    /// The request for the rules due: the state, redacted and bounded, and for each rule its two questions. The first
    /// reads only the action and the person's words; the second reads what the tools returned. Tool results are
    /// untrusted text, so they can sway only whether what a rule requires was done, never whether a rule applies.
    /// </summary>
    public static DecisionRequest BuildRequest(IReadOnlyList<ActiveRule> rules, JudgedState state, Func<string, string> redact)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["action"] = Bound(redact(state.Action), MaxActionCharacters, keepEnd: false),
            ["person"] = Bound(redact(state.Person), MaxPersonCharacters, keepEnd: true),
            ["evidence"] = Bound(redact(state.Evidence), MaxEvidenceCharacters, keepEnd: true),
        };
        List<DecisionQuestion> questions = [];

        for (var index = 0; index < rules.Count; index++)
        {
            var statement = redact(rules[index].Judged?.Statement ?? string.Empty);
            questions.Add(new(
                Name(index, AppliesQuestion),
                $"The rule is: \"{statement}\". Reading only \"action\" and \"person\", is the action something this rule is about?"));
            questions.Add(new(
                Name(index, MetQuestion),
                $"The rule is: \"{statement}\". Does \"evidence\" show that what this rule requires before the action has already been done?"));
        }

        return new(parts, questions);
    }

    public static string Name(int index, string question) => $"rule{index + 1}_{question}";

    #endregion

    #region Private Methods

    private async Task<(DecisionAnswer? Answer, string? Failure)> Ask(DecisionRequest request, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, maxSeconds)), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            // A model that does not watch its token is left behind when the time is up.
            return (await model!.Client.Decide(request, linked.Token).WaitAsync(linked.Token), null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("A rule check took longer than {Seconds} seconds", maxSeconds);
            return (null, $"it took longer than {maxSeconds} seconds");
        }
        catch (ModelProviderException failure)
        {
            logger.LogWarning("A rule check failed at the provider: {Failure}", failure.Failure);
            return (null, failure.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("A rule check failed: {ExceptionType}", exception.GetType().Name);
            return (null, "the decision model could not be used");
        }
    }

    // A verdict a rule: both questions answered with a probability from 0 to 1, or the rule's check failed.
    private static Dictionary<ActiveRule, JudgedVerdict> Read(List<ActiveRule> rules, DecisionAnswer answer) =>
        rules.Select((rule, index) => (rule, Applies: Probability(answer, Name(index, AppliesQuestion)), Met: Probability(answer, Name(index, MetQuestion))))
            .ToDictionary(
                read => read.rule,
                read => read.Applies is { } applies && read.Met is { } met ? new JudgedVerdict(applies, met) : JudgedVerdict.Failed(Unreadable));

    private static double? Probability(DecisionAnswer answer, string name) =>
        answer.Answers.TryGetValue(name, out var result) && result.Yes is { } yes && double.IsFinite(yes) && yes is >= 0 and <= 1 ? yes : null;

    private static string Bound(string text, int limit, bool keepEnd) =>
        text.Length <= limit ? text : keepEnd ? $"{Cut}{text[^(limit - Cut.Length)..]}" : $"{text[..(limit - Cut.Length)]}{Cut}";

    #endregion
}
