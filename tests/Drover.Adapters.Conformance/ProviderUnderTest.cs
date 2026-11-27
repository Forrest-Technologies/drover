namespace Drover.Adapters.Conformance;

/// <summary>A request as the adapter put it on the wire.</summary>
public sealed record WireRequest(string Method, Uri Uri, string Body)
{
    /// <summary>The JSON body, or a default element when the request had none.</summary>
    public JsonElement Json => string.IsNullOrEmpty(Body) ? default : JsonElement.Parse(Body);
}

/// <summary>
/// A provider wired to canned responses for one scenario, plus the model the scenario talks to.
/// SentRequests returns what the adapter has sent so far, so the suite can check the wire as well as the result;
/// <see cref="Wire"/> says how the adapter's wire format carries what the suite checks.
/// </summary>
public sealed record ProviderUnderTest(
    IModelClientProvider Provider,
    ModelEndpoint Connection,
    string ApiKey,
    string ModelId,
    Func<IReadOnlyList<WireRequest>> SentRequests)
{
    #region Properties

    public IChatWire Wire { get; init; } = ChatCompletionsWire.Instance;

    #endregion

    #region Public Methods

    public IModelChatClient CreateChatClient() =>
        Provider.CreateChatClient(Connection, ApiKey, ModelId, new ModelSettings());

    /// <summary>The model calls sent so far, leaving out model lists and other reads.</summary>
    public IReadOnlyList<WireRequest> ChatRequests() => [.. SentRequests().Where(Wire.IsModelCall)];

    public IReadOnlyList<string> SentToolNames(WireRequest request) => Wire.SentToolNames(request);

    public string? SentToolResult(WireRequest request, string callId) => Wire.SentToolResult(request, callId);

    public StructuredFormat SentStructuredFormat(WireRequest request) => Wire.SentStructuredFormat(request);

    public IReadOnlyList<string> SentProtectedReasoning(WireRequest request) => Wire.SentProtectedReasoning(request);

    #endregion
}
