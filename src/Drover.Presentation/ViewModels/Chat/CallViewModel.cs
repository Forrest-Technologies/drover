namespace Drover.Presentation.ViewModels.Chat;

/// <summary>Where a live call stands. Each state a call can be in has one word (<see cref="VoiceWords.State"/>).</summary>
public enum CallState
{
    /// <summary>There is no call and nothing to show.</summary>
    None,

    /// <summary>The call was asked for and did not start: the surface says why.</summary>
    NotStarted,

    Connecting,
    Listening,
    Thinking,
    Speaking,
    Muted,
    Ended,
}


/// <summary>
/// A live call with a bot, as on a phone: the person talks, the bot answers aloud, and either may talk over the
/// other. The call itself is the server's, between it and the speech model; this carries the microphone to it and
/// its voice to the loudspeaker, and shows where the call stands. What is said becomes ordinary messages of the
/// conversation, written by the server. No sound is kept.
/// </summary>
public sealed class CallViewModel : ObservableObject, IAsyncDisposable
{
    #region Private Fields

    /// <summary>How long the microphone is ignored after the bot starts speaking, where the device cancels no echo.</summary>
    public static readonly TimeSpan EchoGuard = TimeSpan.FromMilliseconds(600);

    private static readonly TimeSpan goodbyeWait = TimeSpan.FromSeconds(2);

    // About four seconds of microphone. A socket that falls further behind loses the oldest of it.
    private const int MostFramesWaiting = 200;

    private readonly SpaceVoice voice;

    private readonly DroverApi api;

    private readonly SessionState session;

    private readonly IAudioCapture capture;

    private readonly IAudioPlayback playback;

    private readonly CallConnection connection;

    private readonly IUiThread ui;

    private readonly TimeProvider time;

    private readonly ILogger<CallViewModel> logger;

    private VoiceCall? call;

    private IAsyncDisposable? microphone;

    private IAudioOutput? audio;

    private Channel<ReadOnlyMemory<byte>>? outgoing;

    private CancellationTokenSource? calling;

    private Task reading = Task.CompletedTask;

    private Task sending = Task.CompletedTask;

    private ITimer? clock;

    private long connectedAt;

    private int? maxMinutes;

    private CallState state;

    private volatile bool muted;

    private volatile bool live;

    // The time the loudspeaker last started, as a timestamp of the clock, or zero when no guard is running.
    private long guardedFrom;

    // An answer is being spoken: from its first sound or words until all of it has been played.
    private bool answering;

    private bool answerWhole;

    // After the person talked over the bot, what is left of that answer is not played.
    private bool droppingAnswer;

    private int answerStartedAt;

    private string elapsed = string.Empty;

    #endregion

    #region Constructors

    public CallViewModel(
        SpaceVoice voice,
        DroverApi api,
        SessionState session,
        IAudioCapture capture,
        IAudioPlayback playback,
        CallConnection connection,
        IUiThread ui,
        TimeProvider time,
        ILogger<CallViewModel> logger)
    {
        (this.voice, this.api, this.session, this.capture, this.playback) = (voice, api, session, capture, playback);
        (this.connection, this.ui, this.time, this.logger) = (connection, ui, time, logger);
        voice.Changed += OptionsChanged;
    }

    #endregion

    #region Properties

    /// <summary>
    /// The Call button shows wherever the microphone button of a message shows or a model can hold a live call, on
    /// a device that can record and play.
    /// </summary>
    public bool OffersCall => capture.IsAvailable && playback.IsAvailable && (voice.CanListen || voice.HasLive);

    /// <summary>Where the call stands. Muted takes the place of listening while the microphone is off.</summary>
    public CallState State => state is CallState.Listening && muted ? CallState.Muted : state;

    /// <summary>True while the surface has something to show.</summary>
    public bool Active => state is not CallState.None;

    /// <summary>True from the moment a call is asked for until it is over.</summary>
    public bool InCall => state is CallState.Connecting or CallState.Listening or CallState.Thinking or CallState.Speaking;

    /// <summary>The state in one word, read out once when it changes.</summary>
    public string StateWord => VoiceWords.State(State);

    /// <summary>How long the call has been connected, as a clock shows it. Empty before it connects.</summary>
    public string Elapsed => elapsed;

    /// <summary>Why the call did not start, why it ended, or what failed while it goes on. Null when nothing did.</summary>
    public string? Problem { get; private set; }

    /// <summary>True when the call did not start for want of a model: the surface links to where one is connected.</summary>
    public bool NeedsModel { get; private set; }

    public BotSummary? Bot { get; private set; }

    public Guid? ThreadId { get; private set; }

    public bool Muted => muted;

    /// <summary>True while the words of the exchange under way are shown. Hidden until asked for.</summary>
    public bool WordsShown { get; private set; }

    /// <summary>True while the surface is a bar above the conversation.</summary>
    public bool Shrunk { get; private set; }

    /// <summary>What the person last said, as the speech model wrote it down.</summary>
    public string PersonWords { get; private set; } = string.Empty;

    /// <summary>What the bot is saying, or last said.</summary>
    public string BotWords { get; private set; } = string.Empty;

    public string MuteLabel => muted ? VoiceWords.Unmute : VoiceWords.Mute;

    public string WordsLabel => WordsShown ? VoiceWords.HideWords : VoiceWords.ShowWords;

    public string ShrinkLabel => Shrunk ? VoiceWords.ExpandCall : VoiceWords.ShrinkCall;

    #endregion

    #region Public Methods

    /// <summary>Reads what the space can call with, once.</summary>
    public Task Open() => voice.Load();

    /// <summary>
    /// Calls the bot of a conversation. The server is asked first, so a call it would not take is said in its own
    /// sentence and nothing is opened; the microphone is asked for only once the call is connected.
    /// </summary>
    public async Task Start(BotSummary bot, Guid? threadId)
    {
        if (InCall)
        {
            Shrunk = false;
            Changed();
            return;
        }

        await Release();
        (Bot, ThreadId, Problem, NeedsModel, maxMinutes) = (bot, threadId, null, false, null);
        (PersonWords, BotWords, elapsed, muted, WordsShown, Shrunk) = (string.Empty, string.Empty, string.Empty, false, false, false);
        (answering, answerWhole, droppingAnswer) = (false, false, false);
        Show(CallState.Connecting);

        if (session.Space?.Id is not { } space || threadId is not { } thread)
        {
            Refuse(VoiceWords.CallNotConnected);
            return;
        }

        var asked = calling = new();
        var leaving = asked.Token;

        try
        {
            // Each step's result is kept only if the person has not hung up while it was on its way.
            var connected = await api.OpenCall(space, thread, connection.Connect, leaving);

            if (leaving.IsCancellationRequested)
            {
                await connected.DisposeAsync();
                return;
            }

            call = connected;
            var speaker = await playback.Open(PcmAudio.SampleRate, leaving);

            if (leaving.IsCancellationRequested)
            {
                await speaker.DisposeAsync();
                return;
            }

            audio = speaker;
            speaker.Drained += Drained;
            var sent = outgoing = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(MostFramesWaiting)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            });
            var listening = await capture.Listen(Heard, leaving);

            if (leaving.IsCancellationRequested)
            {
                await listening.DisposeAsync();
                return;
            }

            microphone = listening;
            live = true;
            reading = Read(connected, leaving);
            sending = Send(connected, sent.Reader, leaving);
        }
        catch (OperationCanceledException hungUp) when (leaving.IsCancellationRequested)
        {
            logger.LogDebug(hungUp, "The call was hung up while it was connecting");
        }
        catch (AudioCaptureException refused)
        {
            logger.LogWarning("The microphone could not be used for a call: {Failure}", refused.Failure);
            await Release();
            Refuse(VoiceWords.CaptureFailed(refused, capture.Head));
        }
        catch (Exception failure) when (Failures.IsReportable(failure) || failure is DeviceCallFailedException)
        {
            logger.LogWarning("The call could not be started ({Reason})", failure.GetType().Name);
            await Release();
            NeedsModel = !voice.HasLive;
            Refuse(VoiceWords.Sentence(failure));
        }
    }

    public void ToggleMute()
    {
        muted = !muted;
        Changed();
    }

    public void ToggleWords()
    {
        WordsShown = !WordsShown;
        Changed();
    }

    public void ToggleShrunk()
    {
        Shrunk = !Shrunk;
        Changed();
    }

    /// <summary>Ends the call. The server is told, and given a moment to say it is over.</summary>
    public async Task HangUp()
    {
        if (!InCall)
        {
            return;
        }

        // Still connecting: whatever step is on its way is told to stop, what is already held is let go, and the
        // step lets go of its own result when it arrives.
        if (state is CallState.Connecting || call is not { } connected)
        {
            await Release();
            Show(CallState.None);
            return;
        }

        live = false;

        try
        {
            await connected.HangUp();
            await reading.WaitAsync(goodbyeWait, time);
        }
        catch (Exception gone) when (gone is TimeoutException or WebSocketException or IOException or ObjectDisposedException or OperationCanceledException)
        {
            logger.LogInformation("The call was closed without the server's goodbye ({Reason})", gone.GetType().Name);
        }

        await End(null);
    }

    /// <summary>Takes away the surface of a call that is over, or of one that did not start.</summary>
    public void Close()
    {
        if (state is CallState.Ended or CallState.NotStarted)
        {
            Show(CallState.None);
        }
    }

    #endregion

    #region Interface Implementations

    public async ValueTask DisposeAsync()
    {
        voice.Changed -= OptionsChanged;
        await HangUp();
        await Release();
        state = CallState.None;
    }

    #endregion

    #region The Call's Events

    private async Task Read(VoiceCall connected, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var happened in connected.Read(cancellationToken))
            {
                if (!ReferenceEquals(call, connected))
                {
                    return;
                }

                await Handle(connected, happened);
            }
        }
        catch (OperationCanceledException letGo) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(letGo, "The call was let go from this side");
        }
    }

    private Task Handle(VoiceCall connected, VoiceCallEvent happened)
    {
        switch (happened)
        {
            case VoiceCallReady ready:
                maxMinutes = ready.MaxMinutes;
                connectedAt = time.GetTimestamp();
                clock = time.CreateTimer(_ => ui.Post(Tick), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                elapsed = VoiceWords.Clock(TimeSpan.Zero);
                Show(CallState.Listening);
                break;
            case VoiceCallPersonStarted:
                return PersonStarted(connected);
            case VoiceCallPersonSaid said:
                PersonWords = said.Text;
                droppingAnswer = false;
                Show(answering ? state : CallState.Thinking);
                break;
            case VoiceCallBotSaying saying when !droppingAnswer:
                BeginAnswer();
                BotWords = $"{BotWords}{saying.Text}";
                Show(CallState.Speaking);
                break;
            case VoiceCallAudio sound when !droppingAnswer && audio is { } speaker:
                BeginAnswer();
                GuardIfStarting(speaker);
                speaker.Queue(sound.Samples);
                Show(CallState.Speaking);
                break;
            case VoiceCallBotSaid said:
                BotSaid(said.Text);
                break;
            case VoiceCallFailed failed:
                Problem = failed.Message;
                Changed();
                break;
            case VoiceCallEnded ended:
                return End(ended.Reason);
        }

        return Task.CompletedTask;
    }

    // The person talked over the bot: its voice stops at once, and the server is told how much of the answer was
    // heard, so the model remembers only what was said aloud.
    private async Task PersonStarted(VoiceCall connected)
    {
        if (answering && audio is { } speaker)
        {
            var heard = Math.Max(0, speaker.PlayedMilliseconds - answerStartedAt);
            speaker.Flush();
            (answering, answerWhole, droppingAnswer) = (false, false, true);
            Show(CallState.Listening);
            await connected.Interrupt(heard);
            return;
        }

        Show(CallState.Listening);
    }

    private void BeginAnswer()
    {
        if (answering)
        {
            return;
        }

        (answering, answerWhole) = (true, false);
        answerStartedAt = audio?.PlayedMilliseconds ?? 0;
        BotWords = string.Empty;
    }

    private void BotSaid(string text)
    {
        droppingAnswer = false;

        if (text.Length > 0)
        {
            BotWords = text;
        }

        if (answering)
        {
            answerWhole = true;

            if (audio is not { IsPlaying: true })
            {
                answering = false;
                Show(CallState.Listening);
                return;
            }
        }

        Changed();
    }

    // The loudspeaker has played all it was given. The bot has finished only if its whole answer had arrived.
    private void Drained() => ui.Post(() =>
    {
        if (answering && answerWhole && state is CallState.Speaking)
        {
            answering = false;
            Show(CallState.Listening);
        }
    });

    #endregion

    #region The Microphone

    // Called on the device's own thread for each piece the microphone hears.
    private void Heard(ReadOnlyMemory<byte> samples)
    {
        if (!live || muted || Guarded())
        {
            return;
        }

        // The device may reuse its buffer once this returns, so the piece is copied before it waits to be sent.
        outgoing?.Writer.TryWrite(samples.ToArray());
    }

    private async Task Send(VoiceCall connected, ChannelReader<ReadOnlyMemory<byte>> waiting, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var samples in waiting.ReadAllAsync(cancellationToken))
            {
                await connected.SendAudio(samples, cancellationToken);
            }
        }
        catch (Exception gone) when (gone is OperationCanceledException or WebSocketException or IOException or ObjectDisposedException)
        {
            // The reader of the call's events says that it is over.
            logger.LogDebug("The microphone stopped being sent ({Reason})", gone.GetType().Name);
        }
    }

    // Where the device does not take its own loudspeaker out of the microphone, the first moment of the bot's
    // voice would be heard as the person speaking. The microphone is ignored for that moment.
    private void GuardIfStarting(IAudioOutput speaker)
    {
        if (!capture.CancelsEcho && !speaker.IsPlaying)
        {
            Interlocked.Exchange(ref guardedFrom, time.GetTimestamp());
        }
    }

    private bool Guarded()
    {
        var from = Interlocked.Read(ref guardedFrom);
        return from != 0 && time.GetElapsedTime(from) < EchoGuard;
    }

    #endregion

    #region Private Methods

    private async Task End(string? reason)
    {
        if (!InCall)
        {
            return;
        }

        var limit = maxMinutes;
        await Release();
        Problem = reason switch
        {
            null or VoiceCallEndReasons.HungUp => Problem,
            VoiceCallEndReasons.TimeLimit => VoiceWords.TimeLimitReached(limit),
            VoiceCallEndReasons.Provider => Problem ?? VoiceWords.ProviderEndedCall,
            _ => Problem ?? VoiceWords.CallDisconnected,
        };
        Show(CallState.Ended);
    }

    private void Refuse(string why)
    {
        Problem = why;
        Show(CallState.NotStarted);
    }

    // Lets go of the microphone, the loudspeaker and the socket, in that order, so nothing is heard or played after.
    private async Task Release()
    {
        live = false;
        clock?.Dispose();
        clock = null;
        Interlocked.Exchange(ref guardedFrom, 0);
        var (connected, listening, speaker, asked) = (call, microphone, audio, calling);
        (call, microphone, audio, calling) = (null, null, null, null);
        outgoing?.Writer.TryComplete();
        outgoing = null;

        if (listening is not null)
        {
            await listening.DisposeAsync();
        }

        if (speaker is not null)
        {
            speaker.Drained -= Drained;
            speaker.Flush();
            await speaker.DisposeAsync();
        }

        await (asked?.CancelAsync() ?? Task.CompletedTask);

        if (connected is not null)
        {
            await connected.DisposeAsync();
        }

        asked?.Dispose();
    }

    private void Tick()
    {
        if (clock is not null)
        {
            elapsed = VoiceWords.Clock(time.GetElapsedTime(connectedAt));
            OnPropertyChanged(nameof(Elapsed));
        }
    }

    private void Show(CallState next)
    {
        state = next;
        Changed();
    }

    private void Changed() => OnPropertyChanged(string.Empty);

    private void OptionsChanged() => ui.Post(Changed);

    #endregion
}
