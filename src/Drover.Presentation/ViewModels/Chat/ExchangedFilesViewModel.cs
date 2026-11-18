namespace Drover.Presentation.ViewModels.Chat;

/// <summary>
/// Loads the files a person and a bot exchanged in the person's conversations with it, and reads them again when
/// <see cref="Version"/> changes, which is when a reply may have brought a new one.
/// </summary>
public sealed class ExchangedFilesViewModel(
    DroverApi api,
    SessionState session,
    ILogger<ExchangedFilesViewModel> logger) : ObservableObject
{
    #region Private Fields

    private int? shownVersion;

    #endregion

    #region Properties

    public BotSummary Bot { get; set; } = null!;

    /// <summary>Changes when a conversation with the bot has moved on.</summary>
    public int Version { get; set; }

    public FilesPanelState State { get; private set; } = FilesPanelState.Loading;

    public IReadOnlyList<SharedFile> Files { get; private set; } = [];

    public string? Problem { get; private set; }

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    #endregion

    #region Public Methods

    /// <summary>Runs once the bot and the version are set, and again each time one changes.</summary>
    public async Task ParametersSet()
    {
        if (shownVersion != Version)
        {
            shownVersion = Version;
            await Load();
        }
    }

    // The files on screen stay while the list is read again, so they do not blink away after each reply.
    public async Task Load()
    {
        Problem = null;

        if (State is FilesPanelState.Failed)
        {
            State = FilesPanelState.Loading;
            Changed();
        }

        try
        {
            var exchanged = await api.ListExchangedFiles(SpaceId, Bot.Id);
            Files =
            [
                .. exchanged.Select(file => FileWords.Exchanged(
                    file,
                    api.FileAddress(SpaceId, file.File.Id),
                    Addresses.Moment(Bot.Id, file.ThreadId, file.MessageId, file.InMainConversation))),
            ];
            State = FilesPanelState.Ready;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ExchangedFilesViewModel));
            Problem = Failures.Sentence(exception);
            State = FilesPanelState.Failed;
        }

        Changed();
    }

    public Task<FileText> ReadWhole(SharedFile file) => api.ReadFileText(SpaceId, file.Id, FileWords.MaxShownCharacters);

    #endregion

    #region Private Methods

    private void Changed() => OnPropertyChanged(string.Empty);

    #endregion
}
