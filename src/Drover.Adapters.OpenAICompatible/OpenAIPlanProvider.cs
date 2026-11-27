namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// A person's own ChatGPT plan, on the provider's Responses API with the access token the person signed in for
/// (<see cref="OpenAIPlanSignIn"/>). Each person signs in with their own plan; the credential a call carries is always
/// the person's own, chosen by the engine. Only <c>GET models</c> and <c>POST responses</c> are called, both on the
/// configured <see cref="OpenAIPlanOptions.ApiAddress"/>; every call streams and is shaped as the plan requires
/// (<see cref="PlanRequestShaping"/>).
/// Sources (read 2026-11-27): https://developers.openai.com/siwc/llms-full.txt, "Models and inference" (the model list
/// is <c>{"models":[{slug, display_name, visibility}]}</c>, shown where <c>visibility</c> is <c>list</c>, in the
/// provider's order) and "Preview limitations" (images and files where the model takes them; no image generation, file
/// search, code interpreter, computer use, hosted connectors or tool search; no audio input).
/// </summary>
public sealed class OpenAIPlanProvider(IHttpClientFactory httpClients, IOptionsMonitor<OpenAIPlanOptions> options) : IModelClientProvider
{
    #region Private Fields

    public const string ConnectionKind = "openai_signin";

    public const string DisplayName = "ChatGPT plan";

    public const string HttpClientName = "drover-openai-plan";

    /// <summary>
    /// Where a person reviews and limits what apps use of their plan. The provider asks apps to link it from their
    /// usage pages and from a usage-limit message ("Tracking usage", "UI/UX guidelines").
    /// </summary>
    public static readonly Uri UsagePage = new("https://chatgpt.com/settings/usage");

    private static readonly IReadOnlyDictionary<string, string> noHeaders = new Dictionary<string, string>();

    /// <summary>
    /// What a listed model starts with. The list names a model and nothing of what it does, so it starts with what the
    /// plan's Responses wire carries, as the key kind's models do, and nothing the plan refuses: no image generation,
    /// no hosted web search, no speech. Context size and prices stay unknown; a plan has no price per token.
    /// </summary>
    private static readonly ModelCapabilities planCapabilities = new()
    {
        ToolCalling = true,
        Streaming = true,
        Vision = true,
        StructuredOutput = true,
        Reasoning = true,
        ReasoningEfforts = ["low", "medium", "high"],
        WebSearch = false,
        ImageGeneration = false,
        SpeechToText = false,
        TextToSpeech = false,
        RealtimeVoice = false,
    };

    #endregion

    #region Properties

    public string Kind => ConnectionKind;

    public Uri? FixedAddress => options.CurrentValue.ApiAddress;

    public ModelProviderDescriptor Descriptor { get; } = new(
        ConnectionKind,
        DisplayName,
        ModelKeyRule.Required,
        ModelAddressRule.Fixed,
        AddressHint: null,
        KeyHint: null,
        ReportsCapabilities: false,
        SignInName: "ChatGPT",
        UsagePage: UsagePage);

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
            new ResponsesDestination(options.CurrentValue.ApiAddress, apiKey, noHeaders),
            modelId,
            settings,
            ResponsesRules.Plan);
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModels(ModelEndpoint connection, string? apiKey, CancellationToken cancellation) =>
        [.. (await ReadModels(connection, apiKey, cancellation)).Select(model => new ModelInfo(model.Slug, model.Name, planCapabilities, []))];

    public async Task<ConnectionTestResult> TestConnection(ModelEndpoint connection, string? apiKey, CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return ConnectionTestResult.Failed(ModelFailure.InvalidKey, "Sign in with your ChatGPT plan to finish setup.");
        }

        try
        {
            await ReadModels(connection, apiKey, cancellation);
            return ConnectionTestResult.Ok("Connected to your ChatGPT plan.");
        }
        catch (ModelProviderException failure)
        {
            return ConnectionTestResult.Failed(failure.Failure, failure.Message);
        }
    }

    #endregion

    #region Model Listing

    private async Task<List<PlanModel>> ReadModels(ModelEndpoint connection, string? apiKey, CancellationToken cancellation)
    {
        var address = options.CurrentValue.ApiAddress;
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(WithTrailingSlash(address), "models"));

        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        try
        {
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);

            if (!response.IsSuccessStatusCode)
            {
                var retryAfter = ProtocolFailureMapping.ParseRetryAfter(
                    response.Headers.TryGetValues("Retry-After", out var values) ? values.FirstOrDefault() : null,
                    DateTimeOffset.UtcNow);
                throw ModelProviderException.FromStatus((int)response.StatusCode, retryAfter);
            }

            using var document = await BoundedResponse.ReadJson(response.Content, connection.Limits.MaxBytes, "model list", cancellation);
            return ParseModels(document.RootElement);
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

    /// <summary>The entries meant for a picker, in the provider's order. One without a slug, or a slug seen before, is left out.</summary>
    private static List<PlanModel> ParseModels(JsonElement root)
    {
        if (root.ValueKind is not JsonValueKind.Object || !root.TryGetProperty("models", out var models) || models.ValueKind is not JsonValueKind.Array)
        {
            throw ModelProviderException.FromMalformedResponse();
        }

        HashSet<string> seen = [with(StringComparer.Ordinal)];
        List<PlanModel> listed = [];

        foreach (var model in models.EnumerateArray())
        {
            if (model.ValueKind is not JsonValueKind.Object || Text(model, "visibility") != "list" || Text(model, "slug") is not { Length: > 0 } slug || !seen.Add(slug))
            {
                continue;
            }

            listed.Add(new PlanModel(slug, Text(model, "display_name") is { Length: > 0 } name ? name : slug));
        }

        return listed;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;

    private static Uri WithTrailingSlash(Uri address) =>
        address.AbsoluteUri.EndsWith('/') ? address : new Uri($"{address.AbsoluteUri}/");

    #endregion

    #region Nested Types

    private sealed record PlanModel(string Slug, string Name);

    #endregion
}
