namespace Drover.Presentation.Tests.Support;

/// <summary>
/// A microphone stand-in. A test says whether it can be used and what a recording holds, and plays the part of the
/// device by handing over what it "hears" during a call. Every sound is made-up bytes.
/// </summary>
public sealed class FakeCapture : IAudioCapture
{
    #region Private Fields

    private Action<ReadOnlyMemory<byte>>? heard;

    #endregion

    #region Properties

    public bool IsAvailable { get; set; } = true;

    public bool CancelsEcho { get; set; } = true;

    public AudioHead Head { get; set; } = AudioHead.Browser;

    /// <summary>When set, starting the microphone fails this way.</summary>
    public AudioCaptureFailure? Refuses { get; set; }

    /// <summary>What the next finished recording holds.</summary>
    public AudioClip Clip { get; set; } = new(new byte[] { 1, 2, 3, 4 }, "audio/webm");

    public int RecordingsStarted { get; private set; }

    public int RecordingsFinished { get; private set; }

    public int RecordingsDiscarded { get; private set; }

    public bool Listening => heard is not null;

    #endregion

    #region Public Methods

    /// <summary>The device hears a piece during a call.</summary>
    public void Hear(params byte[] samples) => heard?.Invoke(samples);

    #endregion

    #region Interface Implementations

    public Task<IAudioRecording> Record(CancellationToken cancellationToken = default)
    {
        if (Refuses is { } failure)
        {
            throw new AudioCaptureException(failure);
        }

        RecordingsStarted++;
        return Task.FromResult<IAudioRecording>(new Recording(this));
    }

    public Task<IAsyncDisposable> Listen(Action<ReadOnlyMemory<byte>> heard, CancellationToken cancellationToken = default)
    {
        if (Refuses is { } failure)
        {
            throw new AudioCaptureException(failure);
        }

        this.heard = heard;
        return Task.FromResult<IAsyncDisposable>(new Stopper(() => this.heard = null));
    }

    #endregion

    #region Nested Types

    private sealed class Recording(FakeCapture owner) : IAudioRecording
    {
        #region Private Fields

        private bool finished;

        #endregion

        #region Interface Implementations

        public Task<AudioClip> Finish(CancellationToken cancellationToken = default)
        {
            finished = true;
            owner.RecordingsFinished++;
            return Task.FromResult(owner.Clip);
        }

        public ValueTask DisposeAsync()
        {
            if (!finished)
            {
                owner.RecordingsDiscarded++;
            }

            return ValueTask.CompletedTask;
        }

        #endregion
    }

    private sealed class Stopper(Action stop) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            stop();
            return ValueTask.CompletedTask;
        }
    }

    #endregion
}


/// <summary>
/// A loudspeaker stand-in. It keeps what it was asked to play, holds a clip "playing" until the test says it ended,
/// and for a call lets the test say how much has been played.
/// </summary>
public sealed class FakePlayback : IAudioPlayback
{
    #region Private Fields

    private TaskCompletionSource? clipEnds;

    #endregion

    #region Properties

    public bool IsAvailable { get; set; } = true;

    public string ClipFormat { get; set; } = "mp3";

    /// <summary>The media type and the bytes of each clip played, in order.</summary>
    public List<(string MediaType, byte[] Audio)> Clips { get; } = [];

    public int ClipsStopped { get; private set; }

    public bool ClipPlaying => clipEnds is not null;

    /// <summary>When set, a clip cannot be played.</summary>
    public bool FailsToPlay { get; set; }

    /// <summary>The sound of the call under way, once one was opened.</summary>
    public FakeCallAudio? Call { get; private set; }

    #endregion

    #region Public Methods

    /// <summary>The clip being played reaches its end.</summary>
    public void EndClip() => clipEnds?.TrySetResult();

    #endregion

    #region Interface Implementations

    public async Task PlayClip(Stream audio, string mediaType, CancellationToken cancellationToken)
    {
        if (FailsToPlay)
        {
            throw new DeviceCallFailedException("The sample device could not play the clip.");
        }

        using var held = new MemoryStream();
        await audio.CopyToAsync(held, cancellationToken);
        Clips.Add((mediaType, held.ToArray()));
        var ends = clipEnds = new(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await ends.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            ClipsStopped++;
            throw;
        }
        finally
        {
            if (ReferenceEquals(clipEnds, ends))
            {
                clipEnds = null;
            }
        }
    }

    public Task<ICallAudio> OpenCall(CancellationToken cancellationToken = default) =>
        Task.FromResult<ICallAudio>(Call = new FakeCallAudio());

    #endregion
}


/// <summary>The sound of a call, as a test steers it: what was queued, how much was played, when it was flushed.</summary>
public sealed class FakeCallAudio : ICallAudio
{
    #region Properties

    public int PlayedMilliseconds { get; set; }

    public bool IsPlaying { get; private set; }

    public List<byte[]> Queued { get; } = [];

    public int Flushes { get; private set; }

    public bool Disposed { get; private set; }

    public event Action? Drained;

    #endregion

    #region Public Methods

    /// <summary>The device plays for a while: the count of what was played moves on.</summary>
    public void Play(int milliseconds) => PlayedMilliseconds += milliseconds;

    /// <summary>The device has played everything it was given.</summary>
    public void Drain()
    {
        IsPlaying = false;
        Drained?.Invoke();
    }

    #endregion

    #region Interface Implementations

    public void Queue(ReadOnlyMemory<byte> samples)
    {
        Queued.Add(samples.ToArray());
        IsPlaying = true;
    }

    public void Flush()
    {
        Flushes++;
        IsPlaying = false;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    #endregion
}
