namespace Drover.Domain.Voice;

/// <summary>Writes down what a recording says, through a model connection, without exposing provider types.</summary>
public interface ISpeechToText
{
    /// <summary>The connection kind this adapter serves.</summary>
    string Kind { get; }

    /// <summary>The address every connection of this kind uses, or null when the connection supplies its own.</summary>
    Uri? FixedAddress => null;

    /// <summary>
    /// Sends the audio to the provider and returns its words. The audio is read once from the stream and is never
    /// written to disk or a log. Provider failures surface as <see cref="ModelProviderException"/>.
    /// </summary>
    Task<Transcript> Transcribe(
        ModelEndpoint connection,
        string? apiKey,
        TranscriptionRequest request,
        CancellationToken cancellationToken);
}
