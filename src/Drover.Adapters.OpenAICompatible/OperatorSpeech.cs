namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// The server's own speech service: a compatible audio endpoint the operator names in the server's configuration,
/// such as a speech container beside the server. It speaks the same protocol as a space's compatible connection and
/// differs only in its client, which the server marks as going to an address the operator configured. No space,
/// person or bot can make a connection of this kind or change where it points.
/// </summary>
public sealed class OperatorSpeech(IHttpClientFactory httpClients) : ISpeechToText, ITextToSpeech
{
    #region Constants

    public const string ConnectionKind = "server_speech";

    public const string HttpClientName = "drover-server-speech";

    #endregion

    #region Properties

    public string Kind => ConnectionKind;

    #endregion

    #region Interface Implementations

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

    #endregion
}
