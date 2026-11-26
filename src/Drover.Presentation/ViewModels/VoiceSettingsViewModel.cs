namespace Drover.Presentation.ViewModels;

/// <summary>One of the three things speech does in a space.</summary>
public enum VoiceSlot
{
    Listen,
    Speak,
    Live,
}


/// <summary>
/// One row of the Voice page: what the slot is called, the models it can use, the one chosen (or none, for
/// Automatic), what it uses now in words, and the voices of that model where it speaks.
/// </summary>
public sealed record VoiceRow(VoiceSlot Slot, string Title)
{
    #region Properties

    /// <summary>Automatic first, then every model of the space's connections that can fill the slot.</summary>
    public IReadOnlyList<ChoiceOption> Models { get; init; } = [];

    /// <summary>The value of the chosen model, or <see cref="VoiceSettingsViewModel.AutomaticValue"/>.</summary>
    public string Model { get; init; } = VoiceSettingsViewModel.AutomaticValue;

    /// <summary>What the slot uses now, or that nothing can fill it.</summary>
    public string Uses { get; init; } = VoiceWords.NothingForSlot;

    /// <summary>The voices the model in use offers. Empty for the slot that listens, and when the model lists none.</summary>
    public IReadOnlyList<ChoiceOption> Voices { get; init; } = [];

    public string Voice { get; init; } = string.Empty;

    /// <summary>True when the voice can be saved: the model in use is one of the space's own connections.</summary>
    public bool CanChooseVoice { get; init; }

    /// <summary>True where a sample of the voice can be played: the slot that reads aloud, with something to read with.</summary>
    public bool CanSample { get; init; }

    #endregion
}


/// <summary>
/// The Voice page. Its job: choose which model listens, which speaks and which holds live calls, and hear a voice
/// before choosing it. Each row says Automatic until someone picks. An owner or admin changes the rows; a member
/// reads them.
/// </summary>
public sealed class VoiceSettingsViewModel(
    DroverApi api,
    SessionState session,
    SpaceVoice voice,
    ReadAloudViewModel reader,
    IAudioCapture capture,
    IAudioPlayback playback,
    ILogger<VoiceSettingsViewModel> logger) : ObservableObject
{
    #region Private Fields

    private const string SampleKey = "voice-sample";

    private readonly Dictionary<VoiceSlot, IReadOnlyList<VoiceOptionResponse>> voices = [];

    private bool loaded;

    #endregion

    #region Properties

    /// <summary>The value of the choice that leaves a slot to the server.</summary>
    public const string AutomaticValue = "";

    /// <summary>The three rows, or none while the options are not known or nothing is connected.</summary>
    public IReadOnlyList<VoiceRow> Rows { get; private set; } = [];

    public bool Loading => !loaded && LoadProblem is null;

    public string? LoadProblem { get; private set; }

    /// <summary>Why the last change was not saved, in the server's sentence.</summary>
    public string? SaveProblem { get; private set; }

    /// <summary>True when no model of the space, and no speech service of the server, can listen, speak or call.</summary>
    public bool NothingConnected => loaded && voice.Options is { Listen.Selected: null, Speak.Selected: null, Live.Selected: null };

    public bool CanEdit => session.Space?.Role is "owner" or "admin";

    /// <summary>Said on a device whose build has no sound yet. The settings are the space's, so they still show.</summary>
    public string? DeviceNote => capture.IsAvailable && playback.IsAvailable ? null : VoiceWords.NotOnThisDevice;

    /// <summary>The limits in effect, one to a line, for the collapsed Advanced part.</summary>
    public IReadOnlyList<string> Limits => voice.Options?.Limits is { } limits
        ? [VoiceWords.RecordingLimit(limits.MaxRecordingBytes), VoiceWords.ReadAloudLimit(limits.MaxReadAloudCharacters), VoiceWords.CallLimit(limits.MaxCallMinutes)]
        : [];

    public bool SamplePlaying => reader.Playing == SampleKey;

    public string SampleLabel => SamplePlaying ? VoiceWords.StopSample : VoiceWords.PlaySample;

    #endregion

    #region Public Methods

    public async Task Open()
    {
        reader.PropertyChanged += ReaderChanged;

        if (await session.Load() is not null)
        {
            await Load();
        }
    }

    /// <summary>The page is left: a sample still playing stops.</summary>
    public void Leave()
    {
        reader.PropertyChanged -= ReaderChanged;

        if (SamplePlaying)
        {
            reader.Stop();
        }
    }

    public async Task Load()
    {
        (loaded, LoadProblem) = (false, null);
        OnPropertyChanged(string.Empty);

        if (session.Space?.Id is not { } space)
        {
            return;
        }

        try
        {
            voice.Take(await api.GetVoiceOptions(space));
        }
        catch (Exception failure) when (Failures.IsReportable(failure))
        {
            logger.LogWarning(failure, "The voice settings could not be loaded");
            LoadProblem = Failures.Sentence(failure, "Try again.");
            OnPropertyChanged(string.Empty);
            return;
        }

        await ListVoices(space);
        loaded = true;
        Build();
    }

    /// <summary>Picks the model of a slot, or Automatic. The server may refuse; the row then goes back.</summary>
    public Task ChooseModel(VoiceSlot slot, string value) =>
        Rows.FirstOrDefault(row => row.Slot == slot)?.Model == value
            ? Task.CompletedTask
            : Save(slot, value == AutomaticValue ? null : ToChoice(value, null));

    /// <summary>Picks the voice of a slot. A slot left to the server becomes a choice of the model it uses now.</summary>
    public Task ChooseVoice(VoiceSlot slot, string voiceId)
    {
        if (Rows.FirstOrDefault(row => row.Slot == slot) is not { CanChooseVoice: true } row || row.Voice == voiceId
            || Selected(slot) is not { ConnectionId: { } connection } selection)
        {
            return Task.CompletedTask;
        }

        return Save(slot, new VoiceChoiceBody(connection, selection.Model, voiceId));
    }

    /// <summary>Plays a sample of the voice that reads aloud, or stops it.</summary>
    public Task PlaySample()
    {
        var row = Rows.FirstOrDefault(found => found.Slot is VoiceSlot.Speak);
        return row is { CanSample: true }
            ? reader.Press(SampleKey, new SpeakRequest(Text: VoiceWords.SampleText, Voice: row.Voice.Length > 0 ? row.Voice : null))
            : Task.CompletedTask;
    }

    #endregion

    #region Private Methods

    private async Task Save(VoiceSlot slot, VoiceChoiceBody? choice)
    {
        if (!CanEdit || session.Space?.Id is not { } space)
        {
            return;
        }

        var request = new VoiceSettingsRequest(
            slot is VoiceSlot.Listen ? choice : Kept(VoiceSlot.Listen),
            slot is VoiceSlot.Speak ? choice : Kept(VoiceSlot.Speak),
            slot is VoiceSlot.Live ? choice : Kept(VoiceSlot.Live));

        try
        {
            voice.Take(await api.SaveVoiceSettings(space, request));
            SaveProblem = null;
            await ListVoices(space);
        }
        catch (Exception failure) when (Failures.IsReportable(failure))
        {
            logger.LogWarning("The voice settings could not be saved ({Reason})", failure.GetType().Name);
            SaveProblem = VoiceWords.Sentence(failure);
        }

        Build();
    }

    // A slot someone chose keeps its choice when another slot is saved; a slot left to the server stays left to it.
    private VoiceChoiceBody? Kept(VoiceSlot slot) =>
        Selected(slot) is { Automatic: false, ConnectionId: { } connection } selection
            ? new(connection, selection.Model, selection.Voice)
            : null;

    private VoiceSelection? Selected(VoiceSlot slot) => SlotOf(slot)?.Selected;

    private VoiceSlotResponse? SlotOf(VoiceSlot slot) => slot switch
    {
        VoiceSlot.Listen => voice.Options?.Listen,
        VoiceSlot.Speak => voice.Options?.Speak,
        _ => voice.Options?.Live,
    };

    // The voices of the model each speaking slot uses now. A model that lists none, or a provider that fails, leaves
    // the row without a voice picker.
    private async Task ListVoices(Guid space)
    {
        voices.Clear();

        foreach (var slot in (VoiceSlot[])[VoiceSlot.Speak, VoiceSlot.Live])
        {
            if (Selected(slot) is not { } selection)
            {
                continue;
            }

            try
            {
                voices[slot] = await api.ListVoices(space, selection.ConnectionId, selection.Model);
            }
            catch (Exception failure) when (Failures.IsReportable(failure))
            {
                logger.LogWarning("The voices of a speech model could not be listed ({Reason})", failure.GetType().Name);
            }
        }
    }

    private void Build()
    {
        Rows = NothingConnected || voice.Options is null
            ? []
            :
            [
                Row(VoiceSlot.Listen, VoiceWords.ListensWith),
                Row(VoiceSlot.Speak, VoiceWords.SpeaksWith),
                Row(VoiceSlot.Live, VoiceWords.LiveCallsWith),
            ];
        OnPropertyChanged(string.Empty);
    }

    private VoiceRow Row(VoiceSlot slot, string title)
    {
        var filled = SlotOf(slot)!;
        var selection = filled.Selected;
        var listed = voices.GetValueOrDefault(slot) ?? [];

        return new(slot, title)
        {
            Models =
            [
                new(AutomaticValue, VoiceWords.Automatic),
                .. filled.Sources
                    .Where(source => source.ConnectionId is not null)
                    .OrderBy(source => source.ConnectionName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(source => source.Model, StringComparer.OrdinalIgnoreCase)
                    .Select(source => new ChoiceOption(Value(source.ConnectionId!.Value, source.Model), VoiceWords.Source(source), source.ConnectionName)),
            ],
            Model = selection is { Automatic: false, ConnectionId: { } connection } ? Value(connection, selection.Model) : AutomaticValue,
            Uses = selection is null ? VoiceWords.NothingForSlot : VoiceWords.Uses(selection),
            Voices = [.. listed.Select(option => new ChoiceOption(option.Id, option.Name))],
            Voice = selection?.Voice is { } chosen && listed.Any(option => option.Id == chosen) ? chosen : listed is [var first, ..] ? first.Id : string.Empty,
            CanChooseVoice = CanEdit && selection?.ConnectionId is not null && listed.Count > 0,
            CanSample = slot is VoiceSlot.Speak && selection is not null && playback.IsAvailable,
        };
    }

    private static string Value(Guid connection, string model) => $"{connection}|{model}";

    private static VoiceChoiceBody ToChoice(string value, string? voiceId)
    {
        var cut = value.IndexOf('|', StringComparison.Ordinal);
        return new(Guid.Parse(value[..cut]), value[(cut + 1)..], voiceId);
    }

    private void ReaderChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs change) =>
        OnPropertyChanged(string.Empty);

    #endregion
}
