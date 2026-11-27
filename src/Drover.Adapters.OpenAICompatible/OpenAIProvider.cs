namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// OpenAI with an API key, on its Responses API. The address is fixed and nothing is stored at the provider. The model
/// list names models and says nothing else about them, so every model starts able to do everything this kind carries:
/// a setting a model does not take comes back as the provider's own sentence on the run, and an admin can say more.
/// It lives in this project because this is the one project that references the vendor package (AGENTS.md section 4).
/// Sources (read 2026-11-26): https://developers.openai.com/api/reference/resources/models/methods/list (GET /v1/models
/// returns id, object, created and owned_by) and https://developers.openai.com/api/reference/overview (the base
/// address https://api.openai.com/v1 and the key as <c>Authorization: Bearer</c>).
/// </summary>
public sealed class OpenAIProvider(IHttpClientFactory httpClients) : IModelClientProvider
{
    #region Private Fields

    public const string ConnectionKind = "openai";

    public const string HttpClientName = "drover-openai";

    public static readonly Uri BaseAddress = new("https://api.openai.com/v1/");

    private static readonly IReadOnlyDictionary<string, string> noHeaders = new Dictionary<string, string>();

    /// <summary>
    /// What every listed model starts with. The list has no type field, so embedding, speech and image models are
    /// listed too and are not filtered by name. Context size and prices stay unknown.
    /// </summary>
    private static readonly ModelCapabilities permissive = new()
    {
        ToolCalling = true,
        Streaming = true,
        Vision = true,
        StructuredOutput = true,
        Reasoning = true,
        ReasoningEfforts = ["low", "medium", "high"],
    };

    #endregion

    #region Properties

    public string Kind => ConnectionKind;

    public Uri? FixedAddress => BaseAddress;

    public ModelProviderDescriptor Descriptor { get; } = new(
        ConnectionKind,
        "OpenAI",
        ModelKeyRule.Required,
        ModelAddressRule.Fixed,
        AddressHint: null,
        KeyHint: "Paste an API key from your OpenAI account.",
        ReportsCapabilities: false);

    #endregion

    #region Interface Implementations

    public IModelChatClient CreateChatClient(ModelEndpoint connection, string? apiKey, string modelId, ModelSettings settings)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw ModelProviderException.FromStatus((int)HttpStatusCode.Unauthorized);
        }

        return new ResponsesChatClient(
            httpClients.CreateClient(HttpClientName),
            new ResponsesDestination(BaseAddress, apiKey, noHeaders),
            modelId,
            settings);
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModels(
        ModelEndpoint connection,
        string? apiKey,
        CancellationToken cancellation) =>
        [.. (await ReadModelIds(connection, apiKey, cancellation)).Select(id => new ModelInfo(id, id, permissive, []))];

    public async Task<ConnectionTestResult> TestConnection(
        ModelEndpoint connection,
        string? apiKey,
        CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return ConnectionTestResult.Failed(ModelFailure.InvalidKey, "Paste an OpenAI API key to finish setup.");
        }

        try
        {
            await ReadModelIds(connection, apiKey, cancellation);

            return ConnectionTestResult.Ok("Connected to OpenAI.");
        }
        catch (ModelProviderException failure)
        {
            return ConnectionTestResult.Failed(failure.Failure, failure.Message);
        }
    }

    #endregion

    #region Model Listing

    /// <summary>Always the fixed address: the connection's own address is never used for this kind.</summary>
    private async Task<List<string>> ReadModelIds(ModelEndpoint connection, string? apiKey, CancellationToken cancellation) =>
        await ProtocolModelList.ReadIds(httpClients.CreateClient(HttpClientName), BaseAddress, apiKey, connection.Limits, cancellation)
            ?? throw ModelProviderException.FromStatus((int)HttpStatusCode.NotFound);

    #endregion
}
