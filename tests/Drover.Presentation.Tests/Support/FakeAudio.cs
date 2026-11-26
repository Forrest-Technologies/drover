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

    /// <summary>When set, the device is still asking the person for the microphone until the test completes this.</summary>
    public TaskCompletionSource? Asking { get; set; }

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

    public async Task<IAsyncDisposable> Listen(Action<ReadOnlyMemory<byte>> heard, CancellationToken cancellationToken = default)
    {
        if (Refuses is { } failure)
        {
            throw new AudioCaptureException(failure);
        }

        if (Asking is { } asking)
        {
            await asking.Task;
        }

        this.heard = heard;
        return new Stopper(() => this.heard = null);
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
/// A loudspeaker stand-in. It hands out outputs a test steers: what was queued on each, how much it has played, and
/// when it was flushed. Every sound is made-up bytes.
/// </summary>
public sealed class FakePlayback : IAudioPlayback
{
    #region Properties

    public bool IsAvailable { get; set; } = true;

    /// <summary>When set, no output can be opened.</summary>
    public bool FailsToPlay { get; set; }

    /// <summary>Every output opened, in order, with the rate it was opened at.</summary>
    public List<FakeOutput> Outputs { get; } = [];

    /// <summary>The output opened last: the clip being read, or the sound of the call under way.</summary>
    public FakeOutput? Output => Outputs.LastOrDefault();

    /// <summary>True while the output opened last still has something to play.</summary>
    public bool Playing => Output is { IsPlaying: true, Disposed: false };

    #endregion

    #region Interface Implementations

    public Task<IAudioOutput> Open(int sampleRate, CancellationToken cancellationToken = default)
    {
        if (FailsToPlay)
        {
            throw new DeviceCallFailedException("The sample device could not open its sound output.");
        }

        var output = new FakeOutput(sampleRate);
        Outputs.Add(output);
        return Task.FromResult<IAudioOutput>(output);
    }

    #endregion
}


/// <summary>One output of the stand-in loudspeaker, as a test steers it.</summary>
public sealed class FakeOutput(int sampleRate) : IAudioOutput
{
    #region Properties

    public int SampleRate => sampleRate;

    public int PlayedMilliseconds { get; set; }

    public bool IsPlaying { get; private set; }

    public List<byte[]> Queued { get; } = [];

    /// <summary>Everything queued, end to end.</summary>
    public byte[] Sound => [.. Queued.SelectMany(piece => piece)];

    /// <summary>How often it was flushed while something was still to be played.</summary>
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
        if (IsPlaying)
        {
            Flushes++;
        }

        IsPlaying = false;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    #endregion
}
