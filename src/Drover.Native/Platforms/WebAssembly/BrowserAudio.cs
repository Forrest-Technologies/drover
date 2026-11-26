namespace Drover.Native.Platform;

/// <summary>
/// The microphone and the loudspeaker of a browser, through the shared <c>voice.js</c>: the browser's own capture
/// with its echo cancellation, noise suppression and gain control on, its recorder for a message, an audio worklet
/// that hands a call the samples it carries, and scheduled buffers for what is played. This class only carries the
/// script's answers. Sound crosses to the script and back as bytes in memory and is kept nowhere.
/// </summary>
internal sealed partial class BrowserAudio(ILogger<BrowserAudio> logger) : IAudioCapture, IAudioPlayback
{
    #region Private Fields

    private const string Module = "drover-voice";

    // Relative to the runtime's own folder, _framework, beside which the page's files sit.
    private const string ModulePath = "../voice.js";

    private const string Started = "ok";

    private static readonly Dictionary<int, BrowserOutput> outputs = [];

    private static Action<ReadOnlyMemory<byte>>? listener;

    private Task<JSObject>? loaded;

    #endregion

    #region Properties

    public bool IsAvailable => true;

    public bool CancelsEcho => true;

    public AudioHead Head => AudioHead.Browser;

    #endregion

    #region Interface Implementations

    public async Task<IAudioRecording> Record(CancellationToken cancellationToken = default)
    {
        Check(await Call(StartRecordingWith, "startRecording"));
        return new BrowserRecording(this);
    }

    public async Task<IAsyncDisposable> Listen(Action<ReadOnlyMemory<byte>> heard, CancellationToken cancellationToken = default)
    {
        listener = heard;

        try
        {
            Check(await Call(() => StartListeningWith(Heard), "startListening"));
        }
        catch
        {
            listener = null;
            throw;
        }

        return new BrowserListening(this);
    }

    public async Task<IAudioOutput> Open(int sampleRate, CancellationToken cancellationToken = default)
    {
        var id = await Play(() => OpenOutputWith(sampleRate, Drained), "openOutput");

        if (id == 0)
        {
            throw new DeviceCallFailedException("The browser could not open its sound output.");
        }

        return outputs[id] = new(this, id);
    }

    #endregion

    #region Private Methods

    private static void Check(string status)
    {
        if (status != Started)
        {
            throw new AudioCaptureException(status switch
            {
                "refused" => AudioCaptureFailure.PermissionRefused,
                "none" => AudioCaptureFailure.NoMicrophone,
                _ => AudioCaptureFailure.Failed,
            });
        }
    }

    // A call into the script for the microphone. A script that fails is a microphone that failed.
    private async Task<T> Call<T>(Func<Task<T>> call, string function)
    {
        try
        {
            await (loaded ??= JSHost.ImportAsync(Module, ModulePath));
            return await call();
        }
        catch (JSException failure)
        {
            logger.LogWarning(failure, "The browser failed the microphone call {Function}", function);
            throw new AudioCaptureException(AudioCaptureFailure.Failed, failure);
        }
    }

    // A call into the script for the loudspeaker.
    private async Task<T> Play<T>(Func<Task<T>> call, string function)
    {
        try
        {
            await (loaded ??= JSHost.ImportAsync(Module, ModulePath));
            return await call();
        }
        catch (JSException failure)
        {
            logger.LogWarning(failure, "The browser failed the sound call {Function}", function);
            throw new DeviceCallFailedException("The browser could not play the sound.", failure);
        }
    }

    // The script says more was heard; what waits is taken at once and handed to whoever listens.
    private static void Heard(int waiting)
    {
        if (waiting > 0 && listener is { } heard)
        {
            heard(TakeHeardWith());
        }
    }

    private static void Drained(int id)
    {
        if (outputs.TryGetValue(id, out var output))
        {
            output.TellDrained();
        }
    }

    [JSImport("startRecording", Module)]
    private static partial Task<string> StartRecordingWith();

    [JSImport("finishRecording", Module)]
    private static partial Task<string> FinishRecordingWith();

    // The script keeps what it recorded until it is asked for, so bytes cross the boundary as a return value.
    [JSImport("takeRecording", Module)]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    private static partial byte[] TakeRecordingWith();

    [JSImport("discardRecording", Module)]
    private static partial void DiscardRecordingWith();

    [JSImport("startListening", Module)]
    private static partial Task<string> StartListeningWith([JSMarshalAs<JSType.Function<JSType.Number>>] Action<int> heard);

    [JSImport("takeHeard", Module)]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    private static partial byte[] TakeHeardWith();

    [JSImport("stopListening", Module)]
    private static partial Task StopListeningWith();

    [JSImport("openOutput", Module)]
    private static partial Task<int> OpenOutputWith(int rate, [JSMarshalAs<JSType.Function<JSType.Number>>] Action<int> drained);

    [JSImport("queue", Module)]
    private static partial void QueueWith(int id, [JSMarshalAs<JSType.Array<JSType.Number>>] byte[] samples);

    [JSImport("flush", Module)]
    private static partial void FlushWith(int id);

    [JSImport("played", Module)]
    private static partial int PlayedWith(int id);

    [JSImport("playing", Module)]
    private static partial bool PlayingWith(int id);

    [JSImport("closeOutput", Module)]
    private static partial Task CloseOutputWith(int id);

    #endregion

    #region Nested Types

    private sealed class BrowserRecording(BrowserAudio owner) : IAudioRecording
    {
        #region Private Fields

        private bool finished;

        #endregion

        #region Interface Implementations

        public async Task<AudioClip> Finish(CancellationToken cancellationToken = default)
        {
            finished = true;
            var mediaType = await owner.Call(FinishRecordingWith, "finishRecording");
            return new(TakeRecordingWith(), mediaType);
        }

        public ValueTask DisposeAsync()
        {
            if (!finished)
            {
                finished = true;
                DiscardRecordingWith();
            }

            return ValueTask.CompletedTask;
        }

        #endregion
    }

    private sealed class BrowserListening(BrowserAudio owner) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            listener = null;
            await owner.Call(async () =>
            {
                await StopListeningWith();
                return true;
            }, "stopListening");
        }
    }

    private sealed class BrowserOutput(BrowserAudio owner, int id) : IAudioOutput
    {
        #region Properties

        public int PlayedMilliseconds => PlayedWith(id);

        public bool IsPlaying => PlayingWith(id);

        public event Action? Drained;

        #endregion

        #region Public Methods

        public void TellDrained() => Drained?.Invoke();

        #endregion

        #region Interface Implementations

        public void Queue(ReadOnlyMemory<byte> samples) => QueueWith(id, samples.ToArray());

        public void Flush() => FlushWith(id);

        public async ValueTask DisposeAsync()
        {
            outputs.Remove(id);
            await owner.Play(async () =>
            {
                await CloseOutputWith(id);
                return true;
            }, "closeOutput");
        }

        #endregion
    }

    #endregion
}
