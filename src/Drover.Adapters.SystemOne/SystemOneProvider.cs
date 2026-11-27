namespace Drover.Adapters.SystemOne;

/// <summary>
/// One kind of decision model connection over the state-and-questions API. Every request goes through the named
/// client <see cref="HttpClientName"/>, which the server gives the checked outbound handler, so the deployment's rule
/// on private addresses applies to every kind without this project knowing it.
/// </summary>
public sealed class SystemOneProvider(SystemOneKind kind, IHttpClientFactory clients, int maxAnswerBytes = SystemOneProvider.MaxAnswerBytes) : IDecisionModelProvider
{
    #region Private Fields

    public const string HttpClientName = "drover-decisions";

    /// <summary>The most bytes one answer may hold. Sixty-four choice questions with many options fit well inside it.</summary>
    public const int MaxAnswerBytes = 1024 * 1024;

    /// <summary>The fixed question a test asks, with an answer every working decision model gives.</summary>
    private static readonly DecisionRequest testQuestion = new(
        new Dictionary<string, string> { ["text"] = "The sky on a clear day is blue." },
        [new DecisionQuestion("test", "Does the text say that the sky on a clear day is blue?")]);

    #endregion

    #region Properties

    public string Kind => kind.Kind;

    public string DisplayName => kind.DisplayName;

    public ModelKeyRule KeyRule => kind.KeyRequired ? ModelKeyRule.Required : ModelKeyRule.Optional;

    public DecisionAddressRule AddressRule => kind.AddressRule;

    public Uri? FixedAddress => kind.FixedAddress;

    public SystemOneKind Definition => kind;

    #endregion

    #region Interface Implementations

    public Uri? AddressFor(string? address, string? account) => kind.AddressRule switch
    {
        DecisionAddressRule.Fixed => kind.FixedAddress,
        DecisionAddressRule.Account => string.IsNullOrWhiteSpace(account) ? null : kind.AccountAddress!(account.Trim()),
        _ => Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var given) && given.Scheme is "http" or "https" ? given : null,
    };

    public IDecisionModel CreateClient(ModelEndpoint endpoint, string? apiKey, string model) =>
        new SystemOneClient(kind, clients.CreateClient(HttpClientName), kind.FixedAddress ?? endpoint.BaseAddress, apiKey, model, maxAnswerBytes);

    /// <summary>
    /// Asks the one fixed question and says how long the answer took, or what is wrong in the words the failure maps
    /// to. An answer that is not yes is a model that does not work for rule checks.
    /// </summary>
    public async Task<ConnectionTestResult> TestConnection(ModelEndpoint endpoint, string? apiKey, string model, CancellationToken cancellationToken)
    {
        if (kind.KeyRequired && string.IsNullOrWhiteSpace(apiKey))
        {
            return ConnectionTestResult.Failed(ModelFailure.InvalidKey, $"{kind.DisplayName} needs a key. Add one to the connection.");
        }

        var started = Stopwatch.GetTimestamp();

        try
        {
            var answer = await CreateClient(endpoint, apiKey, model).Decide(testQuestion, cancellationToken);
            var elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            return answer.Answers.TryGetValue("test", out var result) && result.Yes >= 0.5
                ? ConnectionTestResult.Ok($"Connected. Answered in {elapsed.ToString(CultureInfo.InvariantCulture)} ms.")
                : ConnectionTestResult.Failed(ModelFailure.Rejected, "The model answered, but not correctly. Choose a decision model.");
        }
        catch (ModelProviderException failure)
        {
            return ConnectionTestResult.Failed(failure.Failure, failure.Message);
        }
    }

    #endregion
}
