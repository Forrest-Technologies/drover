namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// Any endpoint that speaks the OpenAI chat protocol: a hosted provider or a local model server.
/// A key is optional. Capabilities are conservative until the server says more or an admin does.
/// </summary>
public sealed class OpenAICompatibleProvider(IHttpClientFactory httpClients)
    : IModelClientProvider, IModelImageGenerator, ISpeechToText, ITextToSpeech, IRealtimeVoice
{
    #region Private Fields

    public const string ConnectionKind = ModelConnectionKinds.Compatible;

    public const string HttpClientName = "drover-openai-compatible";

    /// <summary>
    /// The client a live call connects through. It has no retry or timeout pipeline: a call is one long connection,
    /// not a request to send again, and it lasts as long as the people on it want.
    /// </summary>
    public const string RealtimeHttpClientName = "drover-openai-compatible-realtime";

    // The vendor client insists on a credential. Local servers ignore whatever arrives.
    private const string PlaceholderKey = "none";

    private static readonly Uri placeholderAddress = new("https://compatible.invalid/");

    #endregion

    #region Properties

    public string Kind => ConnectionKind;

    public ModelProviderDescriptor Descriptor { get; } = new(
        ConnectionKind,
        "Compatible endpoint",
        ModelKeyRule.Optional,
        ModelAddressRule.Required,
        AddressHint: "The address of the server, starting with http:// or https://.",
        KeyHint: "Leave this empty if the server needs no key.",
        ReportsCapabilities: false,
        NamedProviders: KnownProviders.All);

    #endregion

    #region Interface Implementations

    public IModelChatClient CreateChatClient(ModelEndpoint connection, string? apiKey, string modelId, ModelSettings settings) =>
        new ProtocolChatClient(httpClients.CreateClient(HttpClientName), connection, KeyOrPlaceholder(apiKey), modelId, settings, false);

    public IModelChatClient CreateProtocolClient(ModelEndpoint connection, string? apiKey, string modelId) =>
        new ProtocolChatClient(httpClients.CreateClient($"drover-{connection.Kind.Replace('_', '-')}"), connection, KeyOrPlaceholder(apiKey), modelId, new ModelSettings(), true);

    public Task<ModelImageResult> Generate(
        ModelEndpoint connection,
        string? apiKey,
        ModelImageRequest request,
        CancellationToken cancellationToken) =>
        new ProtocolImageGenerator(httpClients.CreateClient(HttpClientName))
            .Generate(connection, apiKey, request, cancellationToken);

    public Task<ModelImageResult> GenerateCompatibleImage(
        ModelEndpoint connection,
        string? apiKey,
        ModelImageRequest request,
        CancellationToken cancellationToken) =>
        new ProtocolImageGenerator(httpClients.CreateClient($"drover-{connection.Kind.Replace('_', '-')}"))
            .Generate(connection, apiKey, request, cancellationToken);

    public Task<Transcript> Transcribe(
        ModelEndpoint connection,
        string? apiKey,
        TranscriptionRequest request,
        CancellationToken cancellationToken) =>
        new ProtocolSpeech(httpClients.CreateClient(HttpClientName)).Transcribe(connection, apiKey, request, cancellationToken);

    public IAsyncEnumerable<ReadOnlyMemory<byte>> Speak(
        ModelEndpoint connection,
        string? apiKey,
        SpeechRequest request,
        CancellationToken cancellationToken) =>
        new ProtocolSpeech(httpClients.CreateClient(HttpClientName)).Speak(connection, apiKey, request, cancellationToken);

    public Task<IReadOnlyList<VoiceOption>> ListVoices(
        ModelEndpoint connection,
        string? apiKey,
        string model,
        CancellationToken cancellationToken) =>
        new ProtocolSpeech(httpClients.CreateClient(HttpClientName)).ListVoices(connection, apiKey, model, cancellationToken);

    public IReadOnlyList<VoiceOption> ListVoices(string model) => KnownVoices.For(placeholderAddress, model);

    public Task<IRealtimeVoiceSession> Open(
        ModelEndpoint connection,
        string? apiKey,
        RealtimeVoiceOptions options,
        CancellationToken cancellationToken) =>
        ProtocolRealtimeVoice.Open(httpClients.CreateClient(RealtimeHttpClientName), connection, apiKey, options, cancellationToken);

    /// <summary>
    /// The server's models, with what the list says about each. A server that keeps abilities and context lengths
    /// apart from its list is then asked for them, one model at a time, with no switch: see <see cref="OllamaProbe"/>.
    /// </summary>
    public async Task<IReadOnlyList<ModelInfo>> ListModels(
        ModelEndpoint connection,
        string? apiKey,
        CancellationToken cancellation)
    {
        var models = await ReadModels(connection, apiKey, cancellation) ?? [];
        return await OllamaProbe.Fill(httpClients.CreateClient(HttpClientName), connection, apiKey, models, cancellation);
    }

    public async Task<ConnectionTestResult> TestConnection(
        ModelEndpoint connection,
        string? apiKey,
        CancellationToken cancellation)
    {
        try
        {
            var models = await ReadModels(connection, apiKey, cancellation);

            return models is null
                ? ConnectionTestResult.Ok("Connected. This endpoint does not list models, so type the model name.")
                : ConnectionTestResult.Ok($"Connected. The endpoint lists {models.Count} models.");
        }
        catch (ModelProviderException failure)
        {
            return ConnectionTestResult.Failed(failure.Failure, failure.Message);
        }
    }

    #endregion

    #region Model Listing

    /// <summary>Returns null when the endpoint has no model list, which is normal for some servers.</summary>
    private async Task<List<ModelInfo>?> ReadModels(
        ModelEndpoint connection,
        string? apiKey,
        CancellationToken cancellation) =>
        await ProtocolModelList.Read(
            httpClients.CreateClient(HttpClientName),
            connection.BaseAddress,
            apiKey,
            connection.Limits,
            PublishedModelFacts.Read,
            cancellation);

    #endregion

    #region Private Methods

    private static string KeyOrPlaceholder(string? apiKey) => string.IsNullOrEmpty(apiKey) ? PlaceholderKey : apiKey;

    #endregion
}
