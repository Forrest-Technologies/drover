namespace Drover.Presentation.ViewModels.Chat;

/// <summary>
/// What a conversation shows above its composer: the messages, the bot thinking, how the last reply ended, and
/// what is read out as it changes. The conversation around it sets what there is to show and calls
/// <see cref="Update"/>; the list then says what to announce and which message a link landed on.
/// </summary>
public sealed class MessageListViewModel : ObservableObject
{
    #region Private Fields

    private readonly SentenceAnnouncer announcer = new();

    private readonly ToolAnnouncer tools = new();

    private Guid? announcedFailure;

    private Guid? shownMoment;

    #endregion

    #region Properties

    public IReadOnlyList<ChatMessage> Messages { get; set; } = [];

    public string BotName { get; set; } = string.Empty;

    public int BotHue { get; set; }

    /// <summary>The bot that wrote the messages a person would have, on a bot's side of work handed to it.</summary>
    public string? Asker { get; set; }

    /// <summary>True while the bot is working and has not started to answer.</summary>
    public bool Thinking { get; set; }

    /// <summary>Why the last reply failed and what to do, when it did.</summary>
    public string? Problem { get; set; }

    /// <summary>The run the conversation ends with, when it failed and nothing came after it.</summary>
    public RunFailure? Failure { get; set; }

    /// <summary>True while trying the failed run again is being asked for.</summary>
    public bool Retrying { get; set; }

    /// <summary>Starts a new run from the message the failed run answered.</summary>
    public Func<Task>? OnRetry { get; set; }

    /// <summary>Something to know about the last reply that is not a failure, such as that it was stopped.</summary>
    public string? Notice { get; set; }

    /// <summary>Reads what a finished reply's run used. Without it, replies show no usage.</summary>
    public Func<Guid, Task<RunUsageDetail>>? LoadUsage { get; set; }

    /// <summary>Sends a person's answer to a tool call that waits for one. Returns why it failed, or nothing.</summary>
    public Func<Guid, bool, Task<string?>>? Decide { get; set; }

    /// <summary>Where a reply leads to the run that wrote it, as a timeline. Without it, replies lead nowhere.</summary>
    public Func<Guid, string>? RunAddress { get; set; }

    /// <summary>Removes a note the bot saved, from its card, while it still says the given text.</summary>
    public Func<Guid, string, Task<ForgetOutcome>>? Forget { get; set; }

    /// <summary>The notes forgotten from their cards, which then say so.</summary>
    public IReadOnlySet<Guid> ForgottenNotes { get; set; } = new HashSet<Guid>();

    /// <summary>The bot's memory beside the conversation, where a note that changed since its card can be seen.</summary>
    public string? MemoryHref { get; set; }

    /// <summary>Reads a text file's whole content for "Show all".</summary>
    public Func<SharedFile, Task<FileText>>? ReadWhole { get; set; }

    /// <summary>What a request for a person at the bot's screen needs here. Without it such a request is a plain tool card.</summary>
    public ScreenTakeovers? Takeovers { get; set; }

    /// <summary>The message the address points at, brought into view and focused once it is on the page.</summary>
    public Guid? Moment { get; set; }

    /// <summary>The group's accessible name.</summary>
    public string Name => $"Conversation with {BotName}";

    /// <summary>Nothing was said and nothing is on its way: the bot stands there, with one line on what to do.</summary>
    public bool IsEmpty => Messages.Count == 0 && !Thinking;

    public string StartHint => $"Nothing said yet. Write to {BotName} below.";

    /// <summary>What is read out, politely: the newest reply sentence by sentence, each tool call's change, a failure.</summary>
    public string? Spoken { get; private set; }

    /// <summary>The cause of the failure, as the line that ends the conversation says it.</summary>
    public FailureCause? FailureCause => Failure is { } failure ? AttentionWords.CauseOf(failure.Cause) : null;

    /// <summary>
    /// Raised to bring the newest message into view. For when the person sends: they want to see what they wrote,
    /// wherever they had scrolled to. Replies do not raise this, so reading further up is never interrupted.
    /// </summary>
    public event Action? NewestRequested;

    #endregion

    #region Public Methods

    public void ShowNewest() => NewestRequested?.Invoke();

    /// <summary>The id a link to a message's moment names: <c>message-</c> and the message's id.</summary>
    public static string Anchor(Guid messageId) => MessageAnchors.Anchor(messageId);

    /// <summary>The message a moment link names can take focus, so the person lands on it; no other message is a stop.</summary>
    public bool IsMoment(ChatMessage message) => Moment is { } moment && message.Key == moment.ToString();

    /// <summary>
    /// Only a bot's newest message is read out, sentence by sentence. Older ones are already on the page. A tool call
    /// is read out once each time its state changes, and a failure once, when it appears.
    /// </summary>
    public void Update()
    {
        var sentences = Messages is [.., { FromPerson: false } newest] ? announcer.Next(newest.Key, newest.Text, newest.Streaming) : null;
        var changes = tools.Next(Messages.AllTools(BotName));
        Spoken = (sentences, changes) switch
        {
            (null, null) => Spoken,
            ({ } said, { } done) => $"{said} {done}",
            _ => sentences ?? changes,
        };

        if (Failure is { } failure && failure.RunId != announcedFailure)
        {
            announcedFailure = failure.RunId;
            Spoken = AttentionWords.Failure(AttentionWords.CauseOf(failure.Cause), BotName);
        }

        OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// The moment to bring into view now, asked for once the list has been drawn. A moment is shown once, when its
    /// message has arrived on the page; a link followed again, after the person went elsewhere, lands again.
    /// </summary>
    public Guid? TakeMoment()
    {
        if (Moment is null)
        {
            shownMoment = null;
        }

        if (Moment is not { } moment || moment == shownMoment || !Messages.Any(message => message.Key == moment.ToString()))
        {
            return null;
        }

        shownMoment = moment;
        return moment;
    }

    #endregion
}
