namespace Drover.Domain.Voice;

/// <summary>Reads text aloud through a model connection without exposing provider types.</summary>
public interface ITextToSpeech
{
    /// <summary>The connection kind this adapter serves.</summary>
    string Kind { get; }

    /// <summary>The address every connection of this kind uses, or null when the connection supplies its own.</summary>
    Uri? FixedAddress => null;

    /// <summary>
    /// Streams the audio of <paramref name="request"/> as the provider produces it. Nothing is buffered to disk.
    /// Provider failures surface as <see cref="ModelProviderException"/> before the first chunk or between chunks.
    /// </summary>
    IAsyncEnumerable<ReadOnlyMemory<byte>> Speak(
        ModelEndpoint connection,
        string? apiKey,
        SpeechRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// The voices <paramref name="model"/> offers. A provider with no voice list answers with the voices Drover
    /// knows for that model, which may be none: the person then types a voice name.
    /// </summary>
    Task<IReadOnlyList<VoiceOption>> ListVoices(
        ModelEndpoint connection,
        string? apiKey,
        string model,
        CancellationToken cancellationToken);
}
