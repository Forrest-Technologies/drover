namespace Drover.Tests.Common;

/// <summary>
/// A decision model that answers from a script, records every request, and notes whether a tenant scope was open
/// while it was asked. It answers each rule's two questions with the probabilities it was given, rule by rule.
/// </summary>
public sealed class ScriptedDecisionModel : IDecisionModel
{
    #region Private Fields

    private readonly ConcurrentQueue<DecisionRequest> requests = new();

    private readonly ConcurrentQueue<bool> scopes = new();

    #endregion

    #region Properties

    public IReadOnlyList<DecisionRequest> Requests => [.. requests];

    /// <summary>For each request, whether a tenant scope was open in the flow that asked.</summary>
    public IReadOnlyList<bool> ScopeOpen => [.. scopes];

    /// <summary>What every rule's "applies" question is answered.</summary>
    public double Applies { get; set; } = 0.95;

    /// <summary>What every rule's "met" question is answered.</summary>
    public double Met { get; set; }

    /// <summary>When set, the answer for each request, in place of <see cref="Applies"/> and <see cref="Met"/>.</summary>
    public Func<DecisionRequest, CancellationToken, Task<DecisionAnswer>>? Answer { get; set; }

    public long InputTokens { get; set; } = 120;

    #endregion

    #region Interface Implementations

    public Task<DecisionAnswer> Decide(DecisionRequest request, CancellationToken cancellationToken)
    {
        requests.Enqueue(request);
        scopes.Enqueue(TenantScope.IsOpenInCurrentFlow);
        return Answer?.Invoke(request, cancellationToken) ?? Task.FromResult(Uniform(request));
    }

    #endregion

    #region Public Methods

    /// <summary>A host change that gives every space this decision model.</summary>
    public Action<IServiceCollection> InEverySpace() =>
        services => services.AddSingleton<FindDecisionModel>(_ => (_, _) => Task.FromResult<SpaceDecisionModel?>(new(this, "scripted-judge", null)));

    #endregion

    #region Private Methods

    private DecisionAnswer Uniform(DecisionRequest request) =>
        new(
            request.Questions.ToDictionary(
                question => question.Name,
                question => new DecisionResult { Yes = question.Name.EndsWith(JudgedChecks.AppliesQuestion, StringComparison.Ordinal) ? Applies : Met }),
            InputTokens);

    #endregion
}
