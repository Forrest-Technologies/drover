namespace Drover.Presentation.ViewModels.Chat;

/// <summary>What the screen around a conversation tells it, each time the screen changes.</summary>
/// <param name="Bot">The bot this conversation is with.</param>
public sealed record ConversationParameters(BotSummary Bot)
{
    #region Properties

    /// <summary>True for the bot's main conversation, which the server keeps one of for each person.</summary>
    public bool Main { get; init; }

    /// <summary>
    /// The person chose this conversation, so having it open is seeing it. False where the conversation is there
    /// without being chosen: at the start page a phone shows the crew, not the lead's conversation.
    /// </summary>
    public bool Opened { get; init; } = true;

    /// <summary>The message a link points at, brought into view once the conversation is on the page.</summary>
    public Guid? Moment { get; init; }

    /// <summary>The side chat to open. Read once, when the conversation is first shown.</summary>
    public Guid? ThreadId { get; init; }

    /// <summary>Every bot in the crew, so work handed to one is shown under its name.</summary>
    public IReadOnlyList<BotSummary> Crew { get; init; } = [];

    /// <summary>The address of a bot's side of work handed to it, from that bot and its thread. Without it, no link.</summary>
    public Func<Guid, Guid, string>? WorkAddress { get; init; }

    /// <summary>
    /// True for a bot's side of work handed to it, opened to read: there is no field to write in, and opening it marks
    /// nothing seen. The conversation that handed the work over shows it live.
    /// </summary>
    public bool ReadOnly { get; init; }

    /// <summary>The request whose screen the person took over and has open beside the conversation, if any.</summary>
    public Guid? HeldCallId { get; init; }

    /// <summary>
    /// Increases each time the server reports that this thread changed. The open conversation uses it to reconcile
    /// changes made from a notification or another device without treating its own local updates as remote changes.
    /// </summary>
    public long ThreadRevision { get; init; }

    /// <summary>Whether this person can open the bot's screen, for a request to take it over.</summary>
    public ScreenAccess ScreenAccess { get; init; }

    /// <summary>Where the missing screen is set up: the server's status page without a provider, the bot's settings otherwise.</summary>
    public string? ScreenSetupHref { get; init; }

    #endregion
}
