namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// Copilot as a named provider. Each person signs in with the operator's GitHub app; there is no key and no address
/// to paste. Chat and model listing go to Copilot's OpenAI-compatible API with the GitHub user token as bearer.
/// When the model list cannot be read, the failure is reported in plain words, the same as every other kind.
/// </summary>
public sealed class CopilotPlanProvider(
    IHttpClientFactory httpClients,
    IOptionsMonitor<CopilotPlanOptions> options) : IModelClientProvider
{
    #region Private Fields

    public const string HttpClientName = "drover-copilot";

    #endregion

    #region Properties

    public string Kind => ModelConnectionKinds.CopilotSignIn;

    public Uri? FixedAddress => CopilotApiAddress.WithTrailingSlash(options.CurrentValue.ApiAddress);

    /// <summary>Read for each request, so a changed return address in the settings needs no restart.</summary>
    public ModelProviderDescriptor Descriptor => new(
        ModelConnectionKinds.CopilotSignIn,
        "GitHub Copilot",
        ModelKeyRule.Required,
        ModelAddressRule.Fixed,
        AddressHint: null,
        KeyHint: null,
        ReportsCapabilities: true,
        SignInName: "GitHub Copilot",
        UsagePage: options.CurrentValue.UsagePage,
        SignInReturn: options.CurrentValue.RedirectUri);

    #endregion

    #region Interface Implementations

    public IModelChatClient CreateChatClient(ModelEndpoint connection, string? apiKey, string modelId, ModelSettings settings)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw ModelProviderException.FromStatus((int)HttpStatusCode.Unauthorized);
        }

        return new CopilotChatClient(
            new ProtocolChatClient(
                httpClients.CreateClient(HttpClientName),
                new ModelEndpoint(Kind, CopilotApiAddress.WithTrailingSlash(connection.BaseAddress)),
                apiKey,
                modelId,
                settings,
                false));
    }

    public Task<IReadOnlyList<ModelInfo>> ListModels(
        ModelEndpoint connection,
        string? apiKey,
        CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw ModelProviderException.FromStatus((int)HttpStatusCode.Unauthorized);
        }

        return ReadModels(connection, apiKey, cancellation);
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
        var address = CopilotApiAddress.WithTrailingSlash(connection.BaseAddress);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(address, "models"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd(CopilotPlanOptions.UserAgent);

        try
        {
            using var response = await httpClients.CreateClient(HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);

            if (!response.IsSuccessStatusCode)
            {
                var retryAfter = ProtocolFailureMapping.ParseRetryAfter(
                    response.Headers.TryGetValues("Retry-After", out var values) ? values.FirstOrDefault() : null,
                    DateTimeOffset.UtcNow);
                throw ModelProviderException.FromStatus((int)response.StatusCode, retryAfter);
            }

            using var document = await BoundedResponse.ReadJson(response.Content, connection.Limits.MaxBytes, "model list", cancellation);
            var individual = address.Host.Equals("api.individual.githubcopilot.com", StringComparison.OrdinalIgnoreCase);
            return CopilotCatalog.Parse(document.RootElement, individual);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw ModelProviderException.FromMalformedResponse();
        }
        catch (Exception exception) when (ProtocolFailureMapping.TryMap(exception, out var mapped))
        {
            throw mapped;
        }
    }

    #endregion
}
