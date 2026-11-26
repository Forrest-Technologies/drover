namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// Copilot as a named provider. Each person signs in; there is no key and no address to paste. Chat and model
/// listing go to Copilot's OpenAI-compatible API with the required editor headers. When the model list cannot be
/// read, a short static catalog is offered so a bot can still be pointed at a known name.
/// </summary>
public sealed class CopilotPlanProvider(
    IHttpClientFactory httpClients,
    IOptions<CopilotPlanOptions> options,
    ILogger<CopilotPlanProvider> logger) : IModelClientProvider
{
    #region Private Fields

    public const string HttpClientName = "drover-copilot-signin";

    public const string AuthHttpClientName = "drover-copilot-signin-auth";

    #endregion

    #region Properties

    public string Kind => ModelConnectionKinds.CopilotSignIn;

    public Uri? FixedAddress => CopilotApiAddress.WithTrailingSlash(options.Value.ApiAddress);

    public ModelProviderDescriptor Descriptor { get; } = new(
        ModelConnectionKinds.CopilotSignIn,
        "GitHub Copilot",
        ModelKeyRule.None,
        ModelAddressRule.Fixed,
        AddressHint: null,
        KeyHint: null,
        ReportsCapabilities: true,
        SignIn: true);

    #endregion

    #region Interface Implementations

    public IModelChatClient CreateChatClient(ModelEndpoint connection, string? apiKey, string modelId, ModelSettings settings)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new ModelProviderException(
                ModelFailure.InvalidKey,
                "This connection needs a sign-in before it can be used.");
        }

        var address = CopilotApiAddress.FromToken(apiKey) ?? CopilotApiAddress.WithTrailingSlash(connection.BaseAddress);
        return new CopilotChatClient(
            new ProtocolChatClient(
                httpClients.CreateClient(HttpClientName),
                new ModelEndpoint(Kind, address),
                apiKey,
                modelId,
                settings,
                false));
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModels(
        ModelEndpoint connection,
        string? apiKey,
        CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new ModelProviderException(
                ModelFailure.InvalidKey,
                "This connection needs a sign-in before it can be used.");
        }

        try
        {
            var listed = await ReadModels(connection, apiKey, cancellation);
            return listed.Count == 0 ? CopilotCatalog.Fallback : listed;
        }
        catch (ModelProviderException failure) when (failure.Failure is not ModelFailure.InvalidKey)
        {
            logger.LogWarning(failure, "Copilot's model list could not be read; offering the known names.");
            return CopilotCatalog.Fallback;
        }
        catch (Exception failure) when (failure is HttpRequestException or IOException)
        {
            logger.LogWarning(failure, "Copilot's model list could not be reached; offering the known names.");
            return CopilotCatalog.Fallback;
        }
    }

    public async Task<ConnectionTestResult> TestConnection(
        ModelEndpoint connection,
        string? apiKey,
        CancellationToken cancellation)
    {
        try
        {
            var models = await ListModels(connection, apiKey, cancellation);
            return ConnectionTestResult.Ok($"Connected. Copilot lists {models.Count} models. Usage bills the signed-in Copilot plan.");
        }
        catch (ModelProviderException failure)
        {
            return ConnectionTestResult.Failed(failure.Failure, failure.Message);
        }
    }

    #endregion

    #region Model Listing

    private async Task<IReadOnlyList<ModelInfo>> ReadModels(
        ModelEndpoint connection,
        string apiKey,
        CancellationToken cancellation)
    {
        var address = CopilotApiAddress.FromToken(apiKey) ?? CopilotApiAddress.WithTrailingSlash(connection.BaseAddress);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(address, "models"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await httpClients.CreateClient(HttpClientName)
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw ModelProviderException.FromStatus((int)response.StatusCode);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw ModelProviderException.FromStatus((int)response.StatusCode);
        }

        using var document = await BoundedResponse.ReadJson(response.Content, connection.Limits.MaxBytes, "model list", cancellation);
        var individual = address.Host.Equals("api.individual.githubcopilot.com", StringComparison.OrdinalIgnoreCase);
        return CopilotCatalog.Parse(document.RootElement, individual);
    }

    #endregion
}
