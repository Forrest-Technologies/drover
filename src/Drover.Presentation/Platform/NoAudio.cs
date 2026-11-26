namespace Drover.Presentation.Platform;

/// <summary>
/// The microphone and loudspeaker of a device whose build has neither yet. Both say they are not available, so no
/// control that records or plays is shown, and the Voice page says so in one sentence.
/// </summary>
public sealed class NoAudio(AudioHead head) : IAudioCapture, IAudioPlayback
{
    #region Properties

    public bool IsAvailable => false;

    public bool CancelsEcho => false;

    public AudioHead Head => head;

    public string ClipFormat => PcmAudio.Format;

    #endregion

    #region Interface Implementations

    public Task<IAudioRecording> Record(CancellationToken cancellationToken = default) =>
        throw new AudioCaptureException(AudioCaptureFailure.NoMicrophone);

    public Task<IAsyncDisposable> Listen(Action<ReadOnlyMemory<byte>> heard, CancellationToken cancellationToken = default) =>
        throw new AudioCaptureException(AudioCaptureFailure.NoMicrophone);

    public Task PlayClip(Stream audio, string mediaType, CancellationToken cancellationToken) =>
        throw new DeviceCallFailedException("This device's build cannot play sound.");

    public Task<ICallAudio> OpenCall(CancellationToken cancellationToken = default) =>
        throw new DeviceCallFailedException("This device's build cannot play sound.");

    #endregion
}
