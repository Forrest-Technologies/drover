namespace Drover.Domain.Voice;

/// <summary>
/// Opens a live spoken conversation with a speech-to-speech model through a model connection. The model hears
/// the person and answers in its own voice, so there is no transcription or read-aloud step between them.
/// </summary>
public interface IRealtimeVoice
{
    /// <summary>The connection kind this adapter serves.</summary>
    string Kind { get; }

    /// <summary>The address every connection of this kind uses, or null when the connection supplies its own.</summary>
    Uri? FixedAddress => null;

    /// <summary>The voices Drover knows <paramref name="model"/> to offer. May be empty.</summary>
    IReadOnlyList<VoiceOption> ListVoices(string model);

    /// <summary>
    /// Connects and configures the session. Failures to connect surface as <see cref="ModelProviderException"/>.
    /// The caller disposes the session to hang up.
    /// </summary>
    Task<IRealtimeVoiceSession> Open(
        ModelEndpoint connection,
        string? apiKey,
        RealtimeVoiceOptions options,
        CancellationToken cancellationToken);
}


/// <summary>
/// One open call with a speech-to-speech model. Audio in both directions is <see cref="RealtimeAudio"/>: raw
/// samples that are never stored. One reader and one writer may use the session at the same time.
/// </summary>
public interface IRealtimeVoiceSession : IAsyncDisposable
{
    /// <summary>Sends what the microphone heard. The provider decides when the person has finished speaking.</summary>
    ValueTask SendAudio(ReadOnlyMemory<byte> samples, CancellationToken cancellationToken);

    /// <summary>
    /// Stops the answer being spoken and tells the model how much of it the person heard, in milliseconds, so the
    /// conversation the model remembers matches what was said aloud.
    /// </summary>
    ValueTask Interrupt(int heardMilliseconds, CancellationToken cancellationToken);

    /// <summary>What the provider sends until the call ends. Ends without an error when either side hangs up.</summary>
    IAsyncEnumerable<RealtimeVoiceEvent> Read(CancellationToken cancellationToken);
}


/// <summary>The audio format of a live call, the same in both directions.</summary>
public static class RealtimeAudio
{
    #region Constants

    public const int SampleRate = 24_000;

    public const int BytesPerSample = 2;

    public const int Channels = 1;

    public const string MediaType = "audio/pcm";

    #endregion
}
