namespace Drover.Presentation.Platform;

/// <summary>The kind of device the app runs on, as far as sound goes: it decides the words that say how to allow the microphone.</summary>
public enum AudioHead
{
    Browser,
    Android,
    Desktop,
    Apple,
}


/// <summary>Why the microphone could not be used.</summary>
public enum AudioCaptureFailure
{
    /// <summary>The person, or the device's settings, refused the microphone.</summary>
    PermissionRefused,

    /// <summary>The device has no microphone the app can open.</summary>
    NoMicrophone,

    /// <summary>The microphone was there and allowed, and still did not start or stopped working.</summary>
    Failed,
}


/// <summary>
/// The microphone could not be used. A platform's side throws it in place of its own exception types, so a view
/// model never has to know them, and says which of the three it was.
/// </summary>
public sealed class AudioCaptureException(AudioCaptureFailure failure, Exception? inner = null)
    : Exception($"The microphone could not be used: {failure}.", inner)
{
    #region Properties

    public AudioCaptureFailure Failure { get; } = failure;

    #endregion
}


/// <summary>A recording, encoded, with the media type that names its format. It lives in memory only.</summary>
public sealed record AudioClip(ReadOnlyMemory<byte> Audio, string MediaType)
{
    #region Public Methods

    /// <summary>The clip as a stream to send, read from where it is held.</summary>
    public Stream ToStream() =>
        System.Runtime.InteropServices.MemoryMarshal.TryGetArray(Audio, out var held)
            ? new MemoryStream(held.Array!, held.Offset, held.Count, writable: false)
            : new MemoryStream(Audio.ToArray(), writable: false);

    #endregion
}


/// <summary>
/// A message being recorded. <see cref="Finish"/> ends it and hands over what was said; disposing it without finishing
/// throws the recording away. Either way the microphone is let go.
/// </summary>
public interface IAudioRecording : IAsyncDisposable
{
    Task<AudioClip> Finish(CancellationToken cancellationToken = default);
}


/// <summary>
/// The device's microphone. It records one message to a clip in memory, or hands over what it hears as it hears it,
/// for a live call. The permission is asked the first time either is started, never before. Nothing it hears is
/// written to storage or to a log.
/// </summary>
public interface IAudioCapture
{
    /// <summary>False on a device whose build has no microphone support. Nothing that records is shown there.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// True when the device takes its own loudspeaker out of what the microphone hears. Where it does not, a call
    /// ignores the microphone for a moment after the bot starts speaking.
    /// </summary>
    bool CancelsEcho { get; }

    AudioHead Head { get; }

    /// <summary>Starts recording a message. Throws <see cref="AudioCaptureException"/> when the microphone cannot be used.</summary>
    Task<IAudioRecording> Record(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts listening for a call: <paramref name="heard"/> is given each piece as 16-bit little-endian samples, one
    /// channel, 24,000 a second, on whatever thread the device hears on. Disposing what is returned stops listening.
    /// Throws <see cref="AudioCaptureException"/> when the microphone cannot be used.
    /// </summary>
    Task<IAsyncDisposable> Listen(Action<ReadOnlyMemory<byte>> heard, CancellationToken cancellationToken = default);
}
