namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// OpenRouter with the key a person's own sign-in made (<see cref="OpenRouterSignIn"/>). The key is the person's
/// credential, so the engine hands it in as the <c>apiKey</c> of the call: nothing here reads a stored key. Calls go
/// to the same address and on the same chat protocol as an OpenRouter connection made with a pasted key, so the
/// provider's prices and usage are the same too. The address is the kind's setting, never the connection's.
/// </summary>
public sealed class OpenRouterProvider(IHttpClientFactory httpClients, IOptionsMonitor<OpenRouterSignInOptions> options) : IModelClientProvider
{
    #region Private Fields

    public const string ConnectionKind = OpenRouterSignIn.ConnectionKind;

    public const string HttpClientName = "drover-openrouter";

    #endregion

    #region Properties

    public string Kind => ConnectionKind;

    public Uri? FixedAddress => options.CurrentValue.ApiAddress;

    public ModelProviderDescriptor Descriptor { get; } = new(
        ConnectionKind,
        "OpenRouter",
        ModelKeyRule.Required,
        ModelAddressRule.Fixed,
        AddressHint: null,
        KeyHint: null,
        ReportsCapabilities: false,
        SignIn: true);

    #endregion

    #region Interface Implementations

    public IModelChatClient CreateChatClient(ModelEndpoint connection, string? apiKey, string modelId, ModelSettings settings)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw ModelProviderException.FromStatus((int)HttpStatusCode.Unauthorized);
        }

        return new ProtocolChatClient(httpClients.CreateClient(HttpClientName), Endpoint(connection), apiKey, modelId, settings, false);
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModels(ModelEndpoint connection, string? apiKey, CancellationToken cancellation) =>
        [.. (await ReadIds(connection, apiKey, cancellation)).Select(id => new ModelInfo(id, id, new ModelCapabilities(), []))];

    public async Task<ConnectionTestResult> TestConnection(ModelEndpoint connection, string? apiKey, CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return ConnectionTestResult.Failed(ModelFailure.InvalidKey, "Sign in to OpenRouter to finish setup.");
        }

        try
        {
            await ReadIds(connection, apiKey, cancellation);

            return ConnectionTestResult.Ok("Connected to OpenRouter.");
        }
        catch (ModelProviderException failure)
        {
            return ConnectionTestResult.Failed(failure.Failure, failure.Message);
        }
    }

    #endregion

    #region Private Methods

    private ModelEndpoint Endpoint(ModelEndpoint connection) => connection with { BaseAddress = options.CurrentValue.ApiAddress };

    private async Task<List<string>> ReadIds(ModelEndpoint connection, string? apiKey, CancellationToken cancellation) =>
        await ProtocolModelList.ReadIds(httpClients.CreateClient(HttpClientName), options.CurrentValue.ApiAddress, apiKey, connection.Limits, cancellation)
            ?? throw ModelProviderException.FromStatus((int)HttpStatusCode.NotFound);

    #endregion
}
