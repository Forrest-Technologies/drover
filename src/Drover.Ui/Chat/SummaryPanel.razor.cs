namespace Drover.Ui.Chat;

/// <summary>
/// The summary of the main conversation a person has with a bot. The server writes it and rewrites it as the
/// conversation goes on; this reads it, and reads it again when <see cref="Version"/> changes.
/// </summary>
public sealed partial class SummaryPanel(
    DroverApi api,
    SessionState session,
    ILogger<SummaryPanel> logger)
{
    #region Private Fields

    private ConversationSummary? summary;

    private Guid? threadId;

    private string? problem;

    private int? shownVersion;

    #endregion

    #region Properties

    /// <summary>The bot whose main conversation is summarized.</summary>
    [Parameter, EditorRequired]
    public BotSummary Bot { get; set; } = null!;

    /// <summary>Changes when the conversation has moved on, which is when the summary may have been rewritten.</summary>
    [Parameter]
    public int Version { get; set; }

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    private string Facts => summary switch
    {
        { UpdatedAt: { } written, MessagesCovered: 1 } => $"Covers 1 message · written {ScheduleWords.Moment(written)}",
        { UpdatedAt: { } written } => $"Covers {summary.MessagesCovered} messages · written {ScheduleWords.Moment(written)}",
        _ => $"Covers {summary!.MessagesCovered} messages",
    };

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

    // The summary on screen stays while a newer one is fetched, so the text does not blink away after each reply.
    private async Task Load()
    {
        problem = null;
        try
        {
            threadId ??= (await api.OpenMainThread(SpaceId, Bot.Id)).Id;
            summary = await api.GetConversationSummary(SpaceId, threadId.Value);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(SummaryPanel));
            problem = Failures.Sentence(exception);
        }
    }

    #endregion
}
