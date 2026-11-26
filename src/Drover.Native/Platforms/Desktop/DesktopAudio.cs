namespace Drover.Native.Platform;

/// <summary>
/// The microphone and the loudspeaker of a desktop, on Windows, macOS and Linux, through the system's own sound
/// service (SoundFlow over miniaudio). The device is opened at the rate asked for and the system converts to and from
/// its own, so what crosses here is always 16-bit samples of one channel. A message is recorded as those samples and
/// handed over as a WAV file (<see cref="PcmRecording"/>). A desktop has no echo cancellation here, so a call ignores the microphone for a moment
/// after the bot starts to speak; headphones keep the bot's voice out of the microphone for the rest. Sound is held in
/// memory only.
/// </summary>
internal sealed class DesktopAudio(ILogger<DesktopAudio> logger, IReadOnlyList<MiniAudioBackend>? backends = null)
    : IAudioCapture, IAudioPlayback, IDisposable
{
    #region Private Fields

    private readonly Lock opening = new();

    private MiniAudioEngine? engine;

    #endregion

    #region Constructors

    public DesktopAudio(ILogger<DesktopAudio> logger)
        : this(logger, null)
    {
    }

    #endregion

    #region Properties

    public bool IsAvailable => true;

    public bool CancelsEcho => false;

    public AudioHead Head => AudioHead.Desktop;

    #endregion

    #region Interface Implementations

    public Task<IAudioRecording> Record(CancellationToken cancellationToken = default) => PcmRecording.Start(Listen, cancellationToken);

    public Task<IAsyncDisposable> Listen(Action<ReadOnlyMemory<byte>> heard, CancellationToken cancellationToken = default) =>
        Task.FromResult<IAsyncDisposable>(new Listening(Capture(heard)));

    public Task<IAudioOutput> Open(int sampleRate, CancellationToken cancellationToken = default)
    {
        try
        {
            var sound = Engine();
            var format = Format(sampleRate);
            var device = sound.InitializePlaybackDevice(null, format);
            var output = new Output(sound, format, device);
            device.MasterMixer.AddComponent(output);
            device.Start();
            return Task.FromResult<IAudioOutput>(output);
        }
        catch (Exception failure) when (IsDeviceFailure(failure))
        {
            logger.LogWarning(failure, "The sound output could not be opened");
            throw new DeviceCallFailedException("The system could not open its sound output.", failure);
        }
    }

    public void Dispose() => engine?.Dispose();

    #endregion

    #region Private Methods

    private static AudioFormat Format(int sampleRate) =>
        new() { Format = SampleFormat.F32, Channels = PcmAudio.Channels, Layout = ChannelLayout.Mono, SampleRate = sampleRate };

    // The engine is made at the first use, so an app that never records or plays loads no sound library at all.
    private MiniAudioEngine Engine()
    {
        lock (opening)
        {
            return engine ??= new MiniAudioEngine(backends);
        }
    }

    // Opens the default microphone and hands over what it hears as 16-bit samples, on the system's sound thread.
    private AudioCaptureDevice Capture(Action<ReadOnlyMemory<byte>> heard)
    {
        try
        {
            var sound = Engine();
            sound.UpdateAudioDevicesInfo();

            if (sound.CaptureDevices.Length == 0)
            {
                throw new AudioCaptureException(AudioCaptureFailure.NoMicrophone);
            }

            var device = sound.InitializeCaptureDevice(null, Format(PcmAudio.SampleRate));
            device.OnAudioProcessed += (samples, _) => heard(ToBytes(samples));
            device.Start();
            return device;
        }
        catch (Exception failure) when (IsDeviceFailure(failure))
        {
            logger.LogWarning(failure, "The microphone could not be opened");
            throw new AudioCaptureException(Refused(failure) ? AudioCaptureFailure.PermissionRefused : AudioCaptureFailure.Failed, failure);
        }
    }

    private static bool IsDeviceFailure(Exception failure) =>
        failure is BackendException or DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or BadImageFormatException;

    // The system said no, which is what a privacy setting that keeps the microphone from the app answers.
    private static bool Refused(Exception failure) =>
        failure is BackendException && failure.Message.Contains("access", StringComparison.OrdinalIgnoreCase);

    private static byte[] ToBytes(ReadOnlySpan<float> samples)
    {
        var bytes = new byte[samples.Length * PcmAudio.BytesPerSample];

        for (var index = 0; index < samples.Length; index++)
        {
            var sample = Math.Clamp(samples[index], -1f, 1f);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(index * PcmAudio.BytesPerSample), (short)(sample * short.MaxValue));
        }

        return bytes;
    }

    #endregion

    #region Nested Types

    private sealed class Listening(AudioCaptureDevice device) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            device.Stop();
            device.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    // What is to be played, as a part of the system's sound graph: the system asks it for the next samples on its
    // own thread, and gets silence while nothing is queued.
    private sealed class Output(AudioEngine sound, AudioFormat format, AudioPlaybackDevice device) : SoundComponent(sound, format), IAudioOutput
    {
        #region Private Fields

        private readonly Lock queueing = new();

        private readonly Queue<float[]> waiting = [];

        private int offset;

        private long playedFrames;

        private bool playing;

        #endregion

        #region Properties

        public override string Name { get; set; } = "Drover voice";

        public int PlayedMilliseconds => (int)(Interlocked.Read(ref playedFrames) * 1000 / Format.SampleRate);

        public bool IsPlaying
        {
            get
            {
                lock (queueing)
                {
                    return playing;
                }
            }
        }

        public event Action? Drained;

        #endregion

        #region Interface Implementations

        public void Queue(ReadOnlyMemory<byte> samples)
        {
            var piece = new float[samples.Length / PcmAudio.BytesPerSample];

            for (var index = 0; index < piece.Length; index++)
            {
                piece[index] = BinaryPrimitives.ReadInt16LittleEndian(samples.Span[(index * PcmAudio.BytesPerSample)..]) / (float)short.MaxValue;
            }

            lock (queueing)
            {
                waiting.Enqueue(piece);
                playing = true;
            }
        }

        public void Flush()
        {
            lock (queueing)
            {
                waiting.Clear();
                offset = 0;
                playing = false;
            }
        }

        public ValueTask DisposeAsync()
        {
            Flush();
            device.Stop();
            device.MasterMixer.RemoveComponent(this);
            Dispose();
            device.Dispose();
            return ValueTask.CompletedTask;
        }

        #endregion

        #region Private Methods

        protected override void GenerateAudio(Span<float> buffer, int channels)
        {
            buffer.Clear();
            var drained = false;

            lock (queueing)
            {
                var frames = buffer.Length / channels;
                var written = 0;

                while (written < frames && waiting.TryPeek(out var piece))
                {
                    var taken = Math.Min(frames - written, piece.Length - offset);

                    for (var index = 0; index < taken; index++)
                    {
                        buffer.Slice((written + index) * channels, channels).Fill(piece[offset + index]);
                    }

                    written += taken;
                    offset += taken;

                    if (offset == piece.Length)
                    {
                        waiting.Dequeue();
                        offset = 0;
                    }
                }

                Interlocked.Add(ref playedFrames, written);

                if (playing && waiting.Count == 0)
                {
                    playing = false;
                    drained = true;
                }
            }

            if (drained)
            {
                Drained?.Invoke();
            }
        }

        #endregion
    }

    #endregion
}
