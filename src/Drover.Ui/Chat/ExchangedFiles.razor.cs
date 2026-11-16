namespace Drover.Ui.Chat;

/// <summary>
/// Loads the files a person and a bot exchanged in the person's conversations with it, and reads them again when
/// <see cref="Version"/> changes, which is when a reply may have brought a new one.
/// </summary>
public sealed partial class ExchangedFiles(
    DroverApi api,
    SessionState session,
    ILogger<ExchangedFiles> logger)
{
    #region Private Fields

    private FilesPanelState state = FilesPanelState.Loading;

    private IReadOnlyList<SharedFile> files = [];

    private string? problem;

    private int? shownVersion;

    #endregion

    #region Properties

    [Parameter, EditorRequired]
    public BotSummary Bot { get; set; } = null!;

    /// <summary>Changes when a conversation with the bot has moved on.</summary>
    [Parameter]
    public int Version { get; set; }

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    #endregion

    #region Private Methods

    protected override async Task OnParametersSetAsync()
    {
        if (shownVersion != Version)
        {
            shownVersion = Version;
            await Load();
        }
    }

    // The files on screen stay while the list is read again, so they do not blink away after each reply.
    private async Task Load()
    {
        problem = null;

        if (state is FilesPanelState.Failed)
        {
            state = FilesPanelState.Loading;
        }

        try
        {
            var exchanged = await api.ListExchangedFiles(SpaceId, Bot.Id);
            files =
            [
                .. exchanged.Select(file => FileWords.Exchanged(
                    file,
                    api.FileAddress(SpaceId, file.File.Id),
                    Addresses.Moment(Bot.Id, file.ThreadId, file.MessageId, file.InMainConversation))),
            ];
            state = FilesPanelState.Ready;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ExchangedFiles));
            problem = Failures.Sentence(exception);
            state = FilesPanelState.Failed;
        }
    }

    private Task<FileText> ReadWhole(SharedFile file) => api.ReadFileText(SpaceId, file.Id, FileWords.MaxShownCharacters);

    #endregion
}
