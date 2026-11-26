namespace Drover.Presentation.Platform;

/// <summary>
/// The sound of a live call going out: pieces of the bot's voice are queued and played one after another with no
/// gap. Disposing it stops the sound and lets the device's output go.
/// </summary>
public interface ICallAudio : IAsyncDisposable
{
    /// <summary>How much has actually been played since the call's sound was opened, in milliseconds.</summary>
    int PlayedMilliseconds { get; }

    /// <summary>True while something queued has not been played yet.</summary>
    bool IsPlaying { get; }

    /// <summary>Raised when everything queued has been played. It may come on any thread.</summary>
    event Action? Drained;

    /// <summary>Adds a piece to play: 16-bit little-endian samples, one channel, 24,000 a second.</summary>
    void Queue(ReadOnlyMemory<byte> samples);

    /// <summary>Stops at once and drops everything queued. What was already played stays counted.</summary>
    void Flush();
}


/// <summary>
/// The device's loudspeaker. It plays one clip at a time, as it arrives, and plays the pieces of a live call. Nothing
/// it plays is written to storage or to a log.
/// </summary>
public interface IAudioPlayback
{
    /// <summary>False on a device whose build cannot play sound. Nothing that plays is shown there.</summary>
    bool IsAvailable { get; }

    /// <summary>The format this device plays a clip in, as the speech route names it: <c>mp3</c> or <c>pcm</c>.</summary>
    string ClipFormat { get; }

    /// <summary>
    /// Plays a clip from its stream, whose format <paramref name="mediaType"/> names, and returns when it has been
    /// played to its end. Cancelling stops it at once. A clip that cannot be played throws
    /// <see cref="DeviceCallFailedException"/>.
    /// </summary>
    Task PlayClip(Stream audio, string mediaType, CancellationToken cancellationToken);

    /// <summary>Opens the sound of a live call.</summary>
    Task<ICallAudio> OpenCall(CancellationToken cancellationToken = default);
}
