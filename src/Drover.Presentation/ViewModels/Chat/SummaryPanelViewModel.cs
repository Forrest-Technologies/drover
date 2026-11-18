namespace Drover.Presentation.ViewModels.Chat;

/// <summary>What the summary panel has to show.</summary>
public enum SummaryPanelState
{
    Loading,
    Failed,
    ServerWritesNone,
    NoneYet,
    Ready,
}


/// <summary>
/// The summary of the main conversation a person has with a bot. The server writes it and rewrites it as the
/// conversation goes on; this reads it, and reads it again when <see cref="Version"/> changes.
/// </summary>
public sealed class SummaryPanelViewModel(
    DroverApi api,
    SessionState session,
    ILogger<SummaryPanelViewModel> logger) : ObservableObject
{
    #region Private Fields

    private ConversationSummary? summary;

    private Guid? threadId;

    private int? shownVersion;

    #endregion

    #region Properties

    /// <summary>The bot whose main conversation is summarized.</summary>
    public BotSummary Bot { get; set; } = null!;

    /// <summary>Changes when the conversation has moved on, which is when the summary may have been rewritten.</summary>
    public int Version { get; set; }

    /// <summary>Why the summary could not be loaded, as a sentence.</summary>
    public string? Problem { get; private set; }

    public SummaryPanelState State => (Problem, summary) switch
    {
        (not null, _) => SummaryPanelState.Failed,
        (_, null) => SummaryPanelState.Loading,
        _ when string.IsNullOrWhiteSpace(summary.Text) && !summary.ServerWrites => SummaryPanelState.ServerWritesNone,
        _ when string.IsNullOrWhiteSpace(summary.Text) => SummaryPanelState.NoneYet,
        _ => SummaryPanelState.Ready,
    };

    /// <summary>What a state other than a written summary says.</summary>
    public string? Hint => State switch
    {
        SummaryPanelState.Loading => Loading,
        SummaryPanelState.ServerWritesNone => ServerWritesNone,
        SummaryPanelState.NoneYet => NoneYet,
        _ => null,
    };

    public string? Text => summary?.Text;

    /// <summary>Said under a summary the server has stopped rewriting.</summary>
    public string? StaysNote => State is SummaryPanelState.Ready && summary is { ServerWrites: false } ? Stays : null;

    public string Facts => summary switch
    {
        { UpdatedAt: { } written, MessagesCovered: 1 } => $"Covers 1 message · written {ScheduleWords.Moment(written)}",
        { UpdatedAt: { } written } => $"Covers {summary.MessagesCovered} messages · written {ScheduleWords.Moment(written)}",
        _ => $"Covers {summary!.MessagesCovered} messages",
    };

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

    // The summary on screen stays while a newer one is fetched, so the text does not blink away after each reply.
    public async Task Load()
    {
        Problem = null;
        try
        {
            threadId ??= (await api.OpenMainThread(SpaceId, Bot.Id)).Id;
            summary = await api.GetConversationSummary(SpaceId, threadId.Value);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(SummaryPanelViewModel));
            Problem = Failures.Sentence(exception);
        }

        OnPropertyChanged(string.Empty);
    }

    #endregion

    #region Fixed Words

    public const string FailedTitle = "Drover could not load the summary";

    public const string TryAgain = "Try again";

    public const string Loading = "Loading the summary.";

    public const string ServerWritesNone = "This server does not write summaries. Its operator has them turned off.";

    public const string NoneYet =
        "No summary yet. Drover writes one when this conversation is long enough to need it, and rewrites it as the conversation goes on.";

    public const string Stays = "This server no longer writes summaries, so this one stays as it is.";

    #endregion
}
