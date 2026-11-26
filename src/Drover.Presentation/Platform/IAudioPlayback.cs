namespace Drover.Presentation.Platform;

/// <summary>
/// Sound going out of the device: pieces of plain samples are queued and played one after another with no gap, at
/// the rate the output was opened with. A reply read aloud and the bot's voice on a live call both play through one.
/// Disposing it stops the sound and lets the device's output go.
/// </summary>
public interface IAudioOutput : IAsyncDisposable
{
    /// <summary>How much has actually been played since the output was opened, in milliseconds.</summary>
    int PlayedMilliseconds { get; }

    /// <summary>True while something queued has not been played yet.</summary>
    bool IsPlaying { get; }

    /// <summary>Raised when everything queued has been played. It may come on any thread.</summary>
    event Action? Drained;

    /// <summary>Adds a piece to play: 16-bit little-endian samples, one channel.</summary>
    void Queue(ReadOnlyMemory<byte> samples);

    /// <summary>Stops at once and drops everything queued. What was already played stays counted.</summary>
    void Flush();
}


/// <summary>
/// The device's loudspeaker. Nothing it plays is written to storage or to a log. A clip is played through it by
/// <see cref="ClipPlayback"/>, the same way on every device.
/// </summary>
public interface IAudioPlayback
{
    /// <summary>False on a device whose build cannot play sound. Nothing that plays is shown there.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Opens an output for samples at <paramref name="sampleRate"/> a second. A device that cannot throws
    /// <see cref="DeviceCallFailedException"/>.
    /// </summary>
    Task<IAudioOutput> Open(int sampleRate, CancellationToken cancellationToken = default);
}
