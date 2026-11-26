namespace Drover.Presentation.ViewModels.Chat;

/// <summary>
/// Reading aloud: a reply, or a sample of a voice. One thing plays at a time for the whole app, so starting another
/// stops the first. The sound comes from the server as it is made and is played from memory.
/// </summary>
public sealed class ReadAloudViewModel(
    SpaceVoice voice,
    DroverApi api,
    SessionState session,
    IAudioPlayback playback,
    ToastService toasts,
    ILogger<ReadAloudViewModel> logger) : ObservableObject, IDisposable
{
    #region Private Fields

    private CancellationTokenSource? playing;

    private string? playingKey;

    #endregion

    #region Properties

    /// <summary>The speaker button exists only where the device can play and a model can speak.</summary>
    public bool Shows => playback.IsAvailable && voice.CanSpeak;

    /// <summary>What is being read now, by the key it was started with. Null when nothing is.</summary>
    public string? Playing => playingKey;

    #endregion

    #region Public Methods

    public bool IsReading(Guid messageId) => playingKey == Key(messageId);

    /// <summary>What a reply's speaker button does now, which is its accessible name and tooltip.</summary>
    public string Label(Guid messageId) => IsReading(messageId) ? VoiceWords.StopReading : VoiceWords.ReadAloud;

    /// <summary>The speaker button of a reply: it reads the reply aloud, or stops the reading of it.</summary>
    public Task Press(Guid messageId) => Press(Key(messageId), new SpeakRequest(MessageId: messageId, Format: playback.ClipFormat));

    /// <summary>
    /// Plays what <paramref name="speak"/> asks for under a key of the caller's, or stops it when that is what is
    /// playing. Whatever else was playing stops first.
    /// </summary>
    public async Task Press(string key, SpeakRequest speak)
    {
        var wasThis = playingKey == key;
        Stop();

        if (wasThis || session.Space?.Id is not { } space)
        {
            return;
        }

        using var asked = playing = new();
        playingKey = key;
        OnPropertyChanged(nameof(Playing));

        try
        {
            using var answer = await api.Speak(space, speak with { Format = speak.Format ?? playback.ClipFormat }, asked.Token);
            await using var audio = await answer.Content.ReadAsStreamAsync(asked.Token);
            await playback.PlayClip(audio, answer.Content.Headers.ContentType?.MediaType ?? PcmAudio.MediaType, asked.Token);
        }
        catch (OperationCanceledException stopped) when (asked.IsCancellationRequested)
        {
            logger.LogDebug(stopped, "Reading aloud was stopped by the person, or by the next thing to play");
        }
        catch (Exception failure) when (Failures.IsReportable(failure) || failure is DeviceCallFailedException or IOException)
        {
            logger.LogWarning("Reading aloud failed ({Reason})", failure.GetType().Name);
            toasts.Show(VoiceWords.Sentence(failure), ToastKind.Error);
        }
        finally
        {
            if (ReferenceEquals(playing, asked))
            {
                playing = null;
                playingKey = null;
                OnPropertyChanged(nameof(Playing));
            }
        }
    }

    /// <summary>Stops whatever is being read.</summary>
    public void Stop()
    {
        if (playing is { } current)
        {
            playing = null;
            playingKey = null;
            current.Cancel();
            OnPropertyChanged(nameof(Playing));
        }
    }

    #endregion

    #region Interface Implementations

    public void Dispose() => Stop();

    #endregion

    #region Private Methods

    private static string Key(Guid messageId) => $"message:{messageId}";

    #endregion
}
