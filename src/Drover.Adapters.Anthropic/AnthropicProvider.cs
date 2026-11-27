namespace Drover.Adapters.Anthropic;

/// <summary>
/// Anthropic with an API key, on its Messages API. The address is fixed, the key goes as <c>Authorization: Bearer</c>,
/// and every model, its context size, its longest answer and what it can do come from the provider's own model list;
/// no model is named in code. The list carries no prices, so they stay unknown until an admin fills them in.
/// Sources (read 2026-11-27): https://platform.claude.com/docs/en/api/overview (the base address
/// https://api.anthropic.com, <c>anthropic-version</c> required, <c>Authorization: Bearer</c> the primary way to send a
/// key and <c>x-api-key</c> the older one) and https://platform.claude.com/docs/en/api/models/list.
/// </summary>
public sealed class AnthropicProvider(IHttpClientFactory httpClients) : IModelClientProvider
{
    #region Private Fields

    public const string ConnectionKind = "anthropic";

    public const string HttpClientName = "drover-anthropic";

    public static readonly Uri BaseAddress = new("https://api.anthropic.com/");

    private static readonly IReadOnlyDictionary<string, string> noHeaders = new Dictionary<string, string>();

    #endregion

    #region Properties

    public string Kind => ConnectionKind;

    public Uri? FixedAddress => BaseAddress;

    /// <summary>
    /// The model list says what each model can do but carries no prices, so the kind does not claim to report both.
    /// </summary>
    public ModelProviderDescriptor Descriptor { get; } = new(
        ConnectionKind,
        "Anthropic",
        ModelKeyRule.Required,
        ModelAddressRule.Fixed,
        AddressHint: null,
        KeyHint: "Paste an API key from your Anthropic account. A personal plan cannot be used here.",
        ReportsCapabilities: false);

    #endregion

    #region Interface Implementations

    public IModelChatClient CreateChatClient(ModelEndpoint connection, string? apiKey, string modelId, ModelSettings settings)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw ModelProviderException.FromStatus((int)HttpStatusCode.Unauthorized);
        }

        return new AnthropicChatClient(httpClients, HttpClientName, KeyDestination(apiKey), connection.Limits, modelId, settings);
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModels(ModelEndpoint connection, string? apiKey, CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw ModelProviderException.FromStatus((int)HttpStatusCode.Unauthorized);
        }

        using var client = httpClients.CreateClient(HttpClientName);
        return await AnthropicModels.List(client, KeyDestination(apiKey), connection.Limits, cancellation);
    }

    public async Task<ConnectionTestResult> TestConnection(ModelEndpoint connection, string? apiKey, CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return ConnectionTestResult.Failed(ModelFailure.InvalidKey, "Paste an Anthropic API key to finish setup.");
        }

        try
        {
            using var client = httpClients.CreateClient(HttpClientName);
            await AnthropicModels.CheckKey(client, KeyDestination(apiKey), connection.Limits, cancellation);
            return ConnectionTestResult.Ok("Connected to Anthropic.");
        }
        catch (ModelProviderException failure)
        {
            return ConnectionTestResult.Failed(failure.Failure, failure.Message);
        }
    }

    #endregion

    #region Private Methods

    /// <summary>Always the fixed address: the connection's own address is never used for this kind.</summary>
    private static AnthropicDestination KeyDestination(string apiKey) => new(BaseAddress, apiKey, noHeaders);

    #endregion
}
