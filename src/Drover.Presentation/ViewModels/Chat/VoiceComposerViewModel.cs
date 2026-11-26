namespace Drover.Presentation.ViewModels.Chat;

/// <summary>Where recording a message stands.</summary>
public enum VoicePhase
{
    Idle,
    Recording,
    Writing,
}


/// <summary>
/// Saying a message instead of typing it: one press records, one press stops, and the words land in the message
/// field for the person to read and change. Nothing is sent. The recording stays in memory and is dropped once the
/// server has written it down, or when the person discards it.
/// </summary>
public sealed class VoiceComposerViewModel : ObservableObject, IAsyncDisposable
{
    #region Private Fields

    private readonly SpaceVoice voice;

    private readonly DroverApi api;

    private readonly SessionState session;

    private readonly IAudioCapture capture;

    private readonly IUiThread ui;

    private readonly TimeProvider time;

    private readonly ILogger<VoiceComposerViewModel> logger;

    private IAudioRecording? recording;

    private ITimer? clock;

    private long startedAt;

    private CancellationTokenSource? writing;

    private VoicePhase phase;

    private string elapsed = string.Empty;

    private string? problem;

    private bool gone;

    #endregion

    #region Constructors

    public VoiceComposerViewModel(
        SpaceVoice voice,
        DroverApi api,
        SessionState session,
        IAudioCapture capture,
        IUiThread ui,
        TimeProvider time,
        ILogger<VoiceComposerViewModel> logger)
    {
        (this.voice, this.api, this.session, this.capture, this.ui, this.time, this.logger) = (voice, api, session, capture, ui, time, logger);
    }

    #endregion

    #region Properties

    /// <summary>The microphone button exists only where the device can record and a model can write it down.</summary>
    public bool Shows => capture.IsAvailable && voice.CanListen;

    public VoicePhase Phase => phase;

    /// <summary>How long the recording has run, as a clock shows it. Empty when nothing is being recorded.</summary>
    public string Elapsed => elapsed;

    /// <summary>
    /// Why the last recording came to nothing, in one sentence with what to do. Null when it did not, and again once
    /// the next recording starts.
    /// </summary>
    public string? Problem => problem;

    /// <summary>The phase in words, read out once when it changes and never each second.</summary>
    public string Announcement => phase switch
    {
        VoicePhase.Recording => VoiceWords.Recording,
        VoicePhase.Writing => VoiceWords.WritingItDown,
        _ => string.Empty,
    };

    /// <summary>What the microphone button does now, which is its accessible name and tooltip.</summary>
    public string ButtonLabel => phase is VoicePhase.Recording ? VoiceWords.StopRecording : VoiceWords.Record;

    /// <summary>Given the words of a recording, to put in the message field. Nothing is sent.</summary>
    public Action<string>? OnWritten { get; set; }

    /// <summary>Given the sentence of a recording that came to nothing, to tell the person.</summary>
    public Action<string>? OnFailed { get; set; }

    #endregion

    #region Public Methods

    /// <summary>The message field is on screen: reads what the space can listen with, once, and follows it.</summary>
    public Task Open()
    {
        gone = false;
        voice.Changed -= OptionsChanged;
        voice.Changed += OptionsChanged;
        return voice.Load();
    }

    /// <summary>The message field left the screen: a recording under way is thrown away and the microphone let go.</summary>
    public async Task Leave()
    {
        gone = true;
        voice.Changed -= OptionsChanged;
        await StopWriting();
        await Release();
        Show(VoicePhase.Idle, null);
    }

    /// <summary>The one button: it starts a recording, and stops the one that is running.</summary>
    public Task Press() => phase switch
    {
        VoicePhase.Idle => Start(),
        VoicePhase.Recording => Stop(),
        _ => Task.CompletedTask,
    };

    /// <summary>Throws the recording away, or stops waiting for its words. The message field keeps what it had.</summary>
    public async Task Discard()
    {
        await StopWriting();
        await Release();
        Show(VoicePhase.Idle, null);
    }

    #endregion

    #region Interface Implementations

    public async ValueTask DisposeAsync() => await Leave();

    #endregion

    #region Private Methods

    private async Task Start()
    {
        Show(VoicePhase.Recording, null);

        try
        {
            var started = await capture.Record();

            // The person discarded, or left, while the device was still asking for the microphone.
            if (gone || phase is not VoicePhase.Recording)
            {
                await started.DisposeAsync();
                return;
            }

            recording = started;
        }
        catch (AudioCaptureException refused)
        {
            logger.LogWarning("The microphone could not be used for a recording: {Failure}", refused.Failure);
            Show(VoicePhase.Idle, VoiceWords.CaptureFailed(refused, capture.Head));
            return;
        }

        startedAt = time.GetTimestamp();
        clock = time.CreateTimer(_ => ui.Post(Tick), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        Tick();
    }

    private async Task Stop()
    {
        if (recording is not { } running)
        {
            return;
        }

        recording = null;
        StopClock();
        Show(VoicePhase.Writing, null);
        using var asked = writing = new();

        try
        {
            AudioClip clip;

            await using (running)
            {
                clip = await running.Finish(asked.Token);
            }

            var words = await WriteDown(clip, asked.Token);

            if (gone || asked.IsCancellationRequested)
            {
                return;
            }

            if (words.Length == 0)
            {
                Show(VoicePhase.Idle, VoiceWords.NothingHeard);
                return;
            }

            Show(VoicePhase.Idle, null);
            OnWritten?.Invoke(words);
        }
        catch (OperationCanceledException discarded) when (asked.IsCancellationRequested)
        {
            logger.LogDebug(discarded, "A recording was discarded while its words were on their way");
        }
        catch (AudioCaptureException failed)
        {
            logger.LogWarning("The recording could not be finished: {Failure}", failed.Failure);
            Show(VoicePhase.Idle, VoiceWords.CaptureFailed(failed, capture.Head));
        }
        catch (Exception failure) when (Failures.IsReportable(failure))
        {
            logger.LogWarning("The recording could not be written down ({Reason})", failure.GetType().Name);
            Show(VoicePhase.Idle, VoiceWords.Sentence(failure));
        }
        finally
        {
            writing = null;
        }
    }

    private async Task<string> WriteDown(AudioClip clip, CancellationToken cancellationToken)
    {
        if (voice.Options?.Limits.MaxRecordingBytes is { } limit && clip.Audio.Length > limit)
        {
            throw new DroverApiException(HttpStatusCode.RequestEntityTooLarge, VoiceWords.RecordingTooLong(limit));
        }

        if (clip.Audio.Length == 0 || session.Space?.Id is not { } space)
        {
            return string.Empty;
        }

        using var audio = clip.ToStream();
        return (await api.Transcribe(space, audio, clip.MediaType, cancellationToken)).Trim();
    }

    private Task StopWriting() => writing?.CancelAsync() ?? Task.CompletedTask;

    private async Task Release()
    {
        StopClock();

        if (recording is { } running)
        {
            recording = null;
            await running.DisposeAsync();
        }
    }

    private void StopClock()
    {
        clock?.Dispose();
        clock = null;
    }

    private void Tick()
    {
        if (phase is VoicePhase.Recording && clock is not null)
        {
            elapsed = VoiceWords.Clock(time.GetElapsedTime(startedAt));
            OnPropertyChanged(nameof(Elapsed));
        }
    }

    private void Show(VoicePhase next, string? said)
    {
        (phase, problem) = (next, said);

        if (next is not VoicePhase.Recording)
        {
            elapsed = string.Empty;
        }

        OnPropertyChanged(string.Empty);

        if (said is not null)
        {
            OnFailed?.Invoke(said);
        }
    }

    private void OptionsChanged() => ui.Post(() => OnPropertyChanged(string.Empty));

    #endregion
}
