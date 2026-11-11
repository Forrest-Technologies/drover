namespace Drover.Adapters.Conformance;

/// <summary>A provider wired to canned responses for one scenario, plus the model the scenario talks to.</summary>
public sealed record ProviderUnderTest(
    IModelClientProvider Provider,
    ModelConnection Connection,
    string ApiKey,
    string ModelId)
{
    public IChatClient CreateChatClient() =>
        Provider.CreateChatClient(Connection, ApiKey, ModelId, new ModelSettings());
}
