namespace Drover.Adapters.Conformance;

/// <summary>A request as the adapter put it on the wire.</summary>
public sealed record WireRequest(string Method, Uri Uri, string Body)
{
    /// <summary>The JSON body, or a default element when the request had none.</summary>
    public JsonElement Json => string.IsNullOrEmpty(Body) ? default : JsonDocument.Parse(Body).RootElement;
}

/// <summary>
/// A provider wired to canned responses for one scenario, plus the model the scenario talks to.
/// SentRequests returns what the adapter has sent so far, so the suite can check the wire as well as the result.
/// </summary>
public sealed record ProviderUnderTest(
    IModelClientProvider Provider,
    ModelEndpoint Connection,
    string ApiKey,
    string ModelId,
    Func<IReadOnlyList<WireRequest>> SentRequests)
{
    public IModelChatClient CreateChatClient() =>
        Provider.CreateChatClient(Connection, ApiKey, ModelId, new ModelSettings());
}
