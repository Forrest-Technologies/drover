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
        services => services.AddSingleton<FindDecisionModel>(_ => (_, _, _) => Task.FromResult<SpaceDecisionModel?>(new(this, "scripted-judge", null)));

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

/// <summary>
/// A decision model kind for tests: an address a person gives and a required key, a client that is
/// <see cref="Model"/>, and a test that answers <see cref="TestResult"/>. It records each test's key and whether a
/// tenant scope was open while it was asked.
/// </summary>
public sealed class ScriptedDecisionProvider : IDecisionModelProvider
{
    #region Private Fields

    public const string ConnectionKind = "scripted_decisions";

    private readonly ConcurrentQueue<(string? Key, bool ScopeOpen)> tests = new();

    #endregion

    #region Properties

    public string Kind => ConnectionKind;

    public string DisplayName => "Scripted decisions";

    public ModelKeyRule KeyRule => ModelKeyRule.Required;

    public DecisionAddressRule AddressRule => DecisionAddressRule.Address;

    public ScriptedDecisionModel Model { get; } = new();

    public ConnectionTestResult TestResult { get; set; } = ConnectionTestResult.Ok("Connected. Answered in 12 ms.");

    /// <summary>Each test: the key it was given, and whether a tenant scope was open in the flow that asked.</summary>
    public IReadOnlyList<(string? Key, bool ScopeOpen)> Tests => [.. tests];

    #endregion

    #region Interface Implementations

    public Uri? AddressFor(string? address, string? account) =>
        Uri.TryCreate(address, UriKind.Absolute, out var given) ? given : null;

    public IDecisionModel CreateClient(ModelEndpoint endpoint, string? apiKey, string model) => Model;

    public Task<ConnectionTestResult> TestConnection(ModelEndpoint endpoint, string? apiKey, string model, CancellationToken cancellationToken)
    {
        tests.Enqueue((apiKey, TenantScope.IsOpenInCurrentFlow));
        return Task.FromResult(TestResult);
    }

    #endregion
}
