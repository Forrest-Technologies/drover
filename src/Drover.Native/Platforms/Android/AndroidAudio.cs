namespace Drover.Native.Droid;

/// <summary>
/// The microphone and the loudspeaker of an Android device. The microphone is opened as the one for a spoken
/// conversation, so the system takes its own loudspeaker and the room's noise out of what it hears where the device
/// can, and the permission is asked the first time something records. The system only does that for sound it plays
/// as a conversation, so a call's voice goes out that way (<see cref="CallRoute"/>); a reply read aloud goes out as
/// speech on the media stream. A message is recorded as plain samples and handed over as a WAV file
/// (<see cref="PcmRecording"/>). Sound is held in memory only.
/// </summary>
internal sealed class AndroidAudio(ILogger<AndroidAudio> logger) : IAudioCapture, IAudioPlayback
{
    #region Private Fields

    // The rate every device records at, used where one does not record at the rate of a call: twice it, so two
    // samples make one.
    private const int FallbackRate = PcmAudio.SampleRate * 2;

    // A fifth of a second of sound in the system's buffer, in either direction.
    private const int BufferedPerSecond = 5;

    #endregion

    #region Properties

    public bool IsAvailable => true;

    public bool CancelsEcho => Android.Media.Audiofx.AcousticEchoCanceler.IsAvailable;

    public AudioHead Head => AudioHead.Android;

    #endregion

    #region Interface Implementations

    public Task<IAudioRecording> Record(CancellationToken cancellationToken = default) => PcmRecording.Start(Listen, cancellationToken);

    public async Task<IAsyncDisposable> Listen(Action<ReadOnlyMemory<byte>> heard, CancellationToken cancellationToken = default)
    {
        if (!AndroidApplication.Context.PackageManager!.HasSystemFeature(PackageManager.FeatureMicrophone))
        {
            throw new AudioCaptureException(AudioCaptureFailure.NoMicrophone);
        }

        if (await Permissions.RequestMicrophone() is not PermissionStatus.Granted)
        {
            throw new AudioCaptureException(AudioCaptureFailure.PermissionRefused);
        }

        try
        {
            return new Microphone(heard, logger);
        }
        catch (Exception failure) when (failure is Java.Lang.Exception or InvalidOperationException)
        {
            logger.LogWarning(failure, "The microphone could not be opened");
            throw new AudioCaptureException(AudioCaptureFailure.Failed, failure);
        }
    }

    public Task<IAudioOutput> Open(int sampleRate, CancellationToken cancellationToken = default) =>
        Task.FromResult(Open(sampleRate, route: null));

    public Task<IAudioOutput> OpenCall(int sampleRate, CancellationToken cancellationToken = default) =>
        Task.FromResult(Open(sampleRate, new CallRoute(logger)));

    #endregion

    #region Private Methods

    // A route that was taken is given back when the output it was for could not be opened.
    private IAudioOutput Open(int sampleRate, CallRoute? route)
    {
        try
        {
            return new Output(sampleRate, logger, route);
        }
        catch (Exception failure) when (failure is Java.Lang.Exception or InvalidOperationException)
        {
            route?.Dispose();
            logger.LogWarning(failure, "The sound output could not be opened");
            throw new DeviceCallFailedException("Android could not open its sound output.", failure);
        }
    }

    #endregion

    #region Nested Types

    // How the system carries sound while a call lasts. It is told a conversation is going on, which is what makes it
    // take the voice it plays out of what the microphone hears: played as media, the voice comes back in through the
    // microphone and the bot answers itself. A conversation plays through the earpiece unless told otherwise, so the
    // voice is sent to a headset where one is connected and to the loudspeaker where none is. Before Android 12 a
    // wireless headset is not looked for. Disposing it gives the system back as it was.
    private sealed class CallRoute : IDisposable
    {
        #region Private Fields

        private readonly Android.Media.AudioManager manager;

        private readonly Android.Media.Mode before;

        private readonly ILogger logger;

        #endregion

        #region Constructors

        public CallRoute(ILogger logger)
        {
            this.logger = logger;
            manager = (Android.Media.AudioManager)AndroidApplication.Context.GetSystemService(Android.Content.Context.AudioService)!;
            before = manager.Mode;

            try
            {
                manager.Mode = Android.Media.Mode.InCommunication;
                Choose();
            }
            catch (Java.Lang.Exception refused)
            {
                // The call still plays, through whatever the system chose.
                logger.LogWarning(refused, "The sound of a call could not be routed");
            }
        }

        #endregion

        #region Interface Implementations

        public void Dispose()
        {
            try
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(31))
                {
                    manager.ClearCommunicationDevice();
                }
                else
                {
                    manager.SpeakerphoneOn = false;
                }

                manager.Mode = before;
            }
            catch (Java.Lang.Exception refused)
            {
                logger.LogWarning(refused, "The sound of a call could not be given back");
            }
        }

        #endregion

        #region Private Methods

        private void Choose()
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                manager.SpeakerphoneOn = !WiredHeadsetConnected();
                return;
            }

            Android.Media.AudioDeviceInfo? headset = null;
            Android.Media.AudioDeviceInfo? loudspeaker = null;

            foreach (var device in manager.AvailableCommunicationDevices)
            {
                if (device.Type is Android.Media.AudioDeviceType.WiredHeadset
                    or Android.Media.AudioDeviceType.WiredHeadphones
                    or Android.Media.AudioDeviceType.UsbHeadset
                    or Android.Media.AudioDeviceType.BluetoothSco
                    or Android.Media.AudioDeviceType.BleHeadset)
                {
                    headset ??= device;
                }
                else if (device.Type is Android.Media.AudioDeviceType.BuiltinSpeaker)
                {
                    loudspeaker ??= device;
                }
            }

            if ((headset ?? loudspeaker) is { } chosen && !manager.SetCommunicationDevice(chosen))
            {
                logger.LogWarning("The system would not play a call through {Device}", chosen.Type);
            }
        }

        private bool WiredHeadsetConnected()
        {
            foreach (var device in manager.GetDevices(Android.Media.GetDevicesTargets.Outputs) ?? [])
            {
                if (device.Type is Android.Media.AudioDeviceType.WiredHeadset or Android.Media.AudioDeviceType.WiredHeadphones)
                {
                    return true;
                }
            }

            return false;
        }

        #endregion
    }

    // The microphone, read on a thread of its own for as long as it is listened to.
    private sealed class Microphone : IAsyncDisposable
    {
        #region Private Fields

        private readonly Android.Media.AudioRecord record;

        private readonly Android.Media.Audiofx.AcousticEchoCanceler? echo;

        private readonly Action<ReadOnlyMemory<byte>> heard;

        private readonly ILogger logger;

        private readonly int rate;

        private readonly Thread reading;

        private volatile bool listening = true;

        #endregion

        #region Constructors

        public Microphone(Action<ReadOnlyMemory<byte>> heard, ILogger logger)
        {
            (this.heard, this.logger) = (heard, logger);
            (record, rate) = Open();

            if (Android.Media.Audiofx.AcousticEchoCanceler.IsAvailable)
            {
                echo = Android.Media.Audiofx.AcousticEchoCanceler.Create(record.AudioSessionId);
                echo?.SetEnabled(true);
            }

            record.StartRecording();
            reading = new(Read) { IsBackground = true, Name = "Drover microphone" };
            reading.Start();
        }

        #endregion

        #region Interface Implementations

        public ValueTask DisposeAsync()
        {
            listening = false;
            reading.Join(TimeSpan.FromSeconds(2));

            if (record.RecordingState is Android.Media.RecordState.Recording)
            {
                record.Stop();
            }

            echo?.Release();
            echo?.Dispose();
            record.Release();
            record.Dispose();
            return ValueTask.CompletedTask;
        }

        #endregion

        #region Private Methods

        // At the rate of a call where the device records at it, and at twice that otherwise.
        private static (Android.Media.AudioRecord Record, int Rate) Open()
        {
            foreach (var rate in (int[])[PcmAudio.SampleRate, FallbackRate])
            {
                var least = Android.Media.AudioRecord.GetMinBufferSize(rate, Android.Media.ChannelIn.Mono, Android.Media.Encoding.Pcm16bit);

                if (least <= 0)
                {
                    continue;
                }

                var record = new Android.Media.AudioRecord(
                    Android.Media.AudioSource.VoiceCommunication,
                    rate,
                    Android.Media.ChannelIn.Mono,
                    Android.Media.Encoding.Pcm16bit,
                    Math.Max(least, rate * PcmAudio.BytesPerSample / BufferedPerSecond));

                if (record.State is Android.Media.State.Initialized)
                {
                    return (record, rate);
                }

                record.Release();
                record.Dispose();
            }

            throw new InvalidOperationException("The device opened no microphone at a rate the app records at.");
        }

        private void Read()
        {
            // About forty milliseconds at a time.
            var buffer = new byte[rate * PcmAudio.BytesPerSample / 25];

            while (listening)
            {
                var read = record.Read(buffer, 0, buffer.Length);

                if (read < 0)
                {
                    logger.LogWarning("The microphone stopped handing over sound ({Code})", read);
                    return;
                }

                if (read > 0 && listening)
                {
                    heard(rate == PcmAudio.SampleRate ? buffer.AsMemory(0, read) : Halved(buffer.AsSpan(0, read - (read % (2 * PcmAudio.BytesPerSample)))));
                }
            }
        }

        // Two samples make one: their average, which also keeps out what the lower rate cannot hold.
        private static byte[] Halved(ReadOnlySpan<byte> samples)
        {
            var half = new byte[samples.Length / 2];

            for (var at = 0; at < half.Length; at += PcmAudio.BytesPerSample)
            {
                var pair = samples[(at * 2)..];
                var sum = BinaryPrimitives.ReadInt16LittleEndian(pair) + BinaryPrimitives.ReadInt16LittleEndian(pair[PcmAudio.BytesPerSample..]);
                BinaryPrimitives.WriteInt16LittleEndian(half.AsSpan(at), (short)(sum / 2));
            }

            return half;
        }

        #endregion
    }

    // What is to be played, written to the system on a thread of its own so a full buffer never holds the interface.
    // The system says when it has played up to a mark, which is set at the end of what it was given: that is how
    // the end of the sound is known. The writer also looks once the sound that was left should have been played, for
    // a device that does not say.
    private sealed class Output : IAudioOutput
    {
        #region Private Fields

        private readonly Android.Media.AudioTrack track;

        private readonly ILogger logger;

        // Held for a call's output and given back with it.
        private readonly CallRoute? route;

        private readonly int rate;

        // How long the system's own buffer plays for: the least it holds back before a sound is heard.
        private readonly int bufferMilliseconds;

        private readonly Lock queueing = new();

        private readonly Queue<byte[]> waiting = [];

        private readonly SemaphoreSlim queued = new(0);

        private readonly Thread writing;

        private volatile bool open = true;

        // Raised by one each time what waits is dropped, so the writer leaves the piece it was in the middle of.
        private volatile int flushes;

        // The frames given to the system since it was last flushed, which is where its own count starts from.
        private long givenFrames;

        // The frames played before the last flush.
        private long framesBeforeFlush;

        // True while the writer is handing a piece to the system.
        private bool handing;

        private bool playing;

        #endregion

        #region Constructors

        public Output(int sampleRate, ILogger logger, CallRoute? route)
        {
            (rate, this.logger, this.route) = (sampleRate, logger, route);
            var least = Android.Media.AudioTrack.GetMinBufferSize(sampleRate, Android.Media.ChannelOut.Mono, Android.Media.Encoding.Pcm16bit);

            if (least <= 0)
            {
                throw new InvalidOperationException("The device plays no sound at this rate.");
            }

            bufferMilliseconds = least / PcmAudio.BytesPerSample * 1000 / sampleRate;
            using var attributes = new Android.Media.AudioAttributes.Builder()
                .SetUsage(route is null ? Android.Media.AudioUsageKind.Media : Android.Media.AudioUsageKind.VoiceCommunication)!
                .SetContentType(Android.Media.AudioContentType.Speech)!
                .Build()!;
            using var format = new Android.Media.AudioFormat.Builder()
                .SetEncoding(Android.Media.Encoding.Pcm16bit)!
                .SetSampleRate(sampleRate)!
                .SetChannelMask(Android.Media.ChannelOut.Mono)!
                .Build()!;
            track = new Android.Media.AudioTrack.Builder()
                .SetAudioAttributes(attributes)!
                .SetAudioFormat(format)!
                .SetBufferSizeInBytes(least)!
                .SetTransferMode(Android.Media.AudioTrackMode.Stream)!
                .Build()!;

            // The system waits for a full buffer before it starts to play, which a short sound never fills. Where
            // it can be told to, it starts with the first sample.
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                track.SetStartThresholdInFrames(1);
            }

            track.MarkerReached += (_, _) => PlayedToTheMark();
            track.Play();
            writing = new(Write) { IsBackground = true, Name = "Drover loudspeaker" };
            writing.Start();
        }

        #endregion

        #region Properties

        public int PlayedMilliseconds
        {
            get
            {
                lock (queueing)
                {
                    return (int)((framesBeforeFlush + PlayedSinceFlush()) * 1000 / rate);
                }
            }
        }

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
            lock (queueing)
            {
                if (!open)
                {
                    return;
                }

                waiting.Enqueue(samples.ToArray());
                playing = true;
            }

            queued.Release();
        }

        public void Flush()
        {
            lock (queueing)
            {
                waiting.Clear();
                flushes++;
                playing = false;

                if (!open)
                {
                    return;
                }

                // Paused, flushed and started again, the system drops what it held and its count starts from zero.
                framesBeforeFlush += PlayedSinceFlush();
                givenFrames = 0;
                track.Pause();
                track.Flush();
                track.Play();
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (queueing)
            {
                if (!open)
                {
                    return ValueTask.CompletedTask;
                }

                framesBeforeFlush += PlayedSinceFlush();
                givenFrames = 0;
                waiting.Clear();
                flushes++;
                playing = false;
                open = false;
                track.Pause();
                track.Flush();
            }

            queued.Release();
            writing.Join(TimeSpan.FromSeconds(2));
            track.Release();
            track.Dispose();
            queued.Dispose();
            route?.Dispose();
            return ValueTask.CompletedTask;
        }

        #endregion

        #region Private Methods

        // The system counts the frames it has played since it was last flushed, and never more than it was given.
        // Called with the queue held.
        private long PlayedSinceFlush() => open ? Math.Min((uint)track.PlaybackHeadPosition, givenFrames) : 0;

        private void Write()
        {
            while (open)
            {
                // Until more is queued, and with sound still to be played, no longer than that sound lasts.
                queued.Wait(LeftToPlay());

                while (open && Next() is { } next)
                {
                    Hand(next.Piece, next.Flushes);
                }

                MarkTheEnd();
            }
        }

        private TimeSpan LeftToPlay()
        {
            lock (queueing)
            {
                return playing
                    ? TimeSpan.FromMilliseconds(((givenFrames - PlayedSinceFlush()) * 1000 / rate) + bufferMilliseconds)
                    : Timeout.InfiniteTimeSpan;
            }
        }

        private (byte[] Piece, int Flushes)? Next()
        {
            lock (queueing)
            {
                handing = waiting.TryDequeue(out var piece);
                return handing ? (piece!, flushes) : null;
            }
        }

        // A piece goes to the system in as many parts as the system takes it in, so a flush is seen between them. A
        // piece that was flushed away while it was being handed over is left where it is.
        private void Hand(byte[] piece, int flushesAtStart)
        {
            var at = 0;

            while (open && at < piece.Length && flushes == flushesAtStart)
            {
                var taken = track.Write(piece, at, piece.Length - at);

                if (taken < 0)
                {
                    logger.LogWarning("The loudspeaker stopped taking sound ({Code})", taken);
                    return;
                }

                lock (queueing)
                {
                    if (flushes == flushesAtStart)
                    {
                        givenFrames += taken / PcmAudio.BytesPerSample;
                    }
                }

                at += taken;
            }
        }

        // Everything queued has been given to the system: it is asked to say when it has played all of it.
        private void MarkTheEnd()
        {
            lock (queueing)
            {
                handing = false;

                if (open && playing && waiting.Count == 0 && givenFrames > 0)
                {
                    track.SetNotificationMarkerPosition((int)givenFrames);
                }
            }

            // A mark at or behind what was already played is never reported, so that case is looked at here.
            PlayedToTheMark();
        }

        // Told by the system that it reached the mark, or asked after the mark was set. The sound has ended only if
        // nothing more was queued meanwhile and the system has played all it was given.
        private void PlayedToTheMark()
        {
            lock (queueing)
            {
                if (!open || !playing || handing || waiting.Count > 0 || (uint)track.PlaybackHeadPosition < givenFrames)
                {
                    return;
                }

                playing = false;
            }

            Drained?.Invoke();
        }

        #endregion
    }

    #endregion
}
