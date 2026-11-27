namespace Drover.Presentation.Platform;

/// <summary>
/// A message recorded on a device that has no encoder of its own: the plain samples its microphone hands over are
/// kept in memory, up to a bound, and handed back as a WAV file, which the transcription route takes. Built on the
/// same listening a live call uses, so a device implements its microphone once.
/// </summary>
public sealed class PcmRecording : IAudioRecording
{
    #region Private Fields

    /// <summary>The most kept of one message: more than any server takes, and a bound on memory.</summary>
    public const int MostBytes = 64 * 1024 * 1024;

    private readonly Lock writing = new();

    private readonly MemoryStream samples = new();

    private IAsyncDisposable? listening;

    #endregion

    #region Public Methods

    /// <summary>
    /// Starts recording through <paramref name="listen"/>, the device's own way of handing over what it hears as
    /// 16-bit samples of one channel, 24,000 a second.
    /// </summary>
    public static async Task<IAudioRecording> Start(
        Func<Action<ReadOnlyMemory<byte>>, CancellationToken, Task<IAsyncDisposable>> listen,
        CancellationToken cancellationToken = default)
    {
        var recording = new PcmRecording();
        recording.listening = await listen(recording.Take, cancellationToken);
        return recording;
    }

    #endregion

    #region Interface Implementations

    public async Task<AudioClip> Finish(CancellationToken cancellationToken = default)
    {
        await Release();

        lock (writing)
        {
            return PcmAudio.ToWav(samples.GetBuffer().AsSpan(0, (int)samples.Length));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Release();

        lock (writing)
        {
            samples.SetLength(0);
        }
    }

    #endregion

    #region Private Methods

    // Called on the device's own thread for each piece the microphone hears.
    private void Take(ReadOnlyMemory<byte> heard)
    {
        lock (writing)
        {
            if (samples.Length + heard.Length <= MostBytes)
            {
                samples.Write(heard.Span);
            }
        }
    }

    private async Task Release()
    {
        if (Interlocked.Exchange(ref listening, null) is { } microphone)
        {
            await microphone.DisposeAsync();
        }
    }

    #endregion
}
