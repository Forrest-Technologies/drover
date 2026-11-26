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

    private string? announcedUnusable;

    private Guid? shownMoment;

    private IReadOnlyList<ChatMessage>? newestOf;

    private IReadOnlySet<string>? newest;

    // A turn is shown as one, so the page never starts or ends inside one.
    private readonly ListWindow<ChatMessage> window = new(message => message.Key, message => !message.ContinuesTurn);

    private IReadOnlyList<ChatMessage>? windowOf;

    #endregion

    #region Properties

    /// <summary>Everything said in the conversation. What is read out, searched, and linked to is all of it.</summary>
    public IReadOnlyList<ChatMessage> Messages { get; set; } = [];

    /// <summary>
    /// The messages on the page: the newest ones, and from there wherever the reader went. A long conversation costs
    /// what its page holds; the rest of <see cref="Messages"/> is shown as the reader comes to it.
    /// </summary>
    public IReadOnlyList<ChatMessage> Shown => Window().Shown;

    /// <summary>How many messages come before the ones on the page.</summary>
    public int Earlier => Window().Earlier;

    /// <summary>How many messages come after the ones on the page. None while the page ends with the newest.</summary>
    public int Later => Window().Later;

    /// <summary>The action that stands above the page while messages come before it.</summary>
    public static string EarlierLabel => "Show earlier messages";

    /// <summary>The action that stands below the page while it does not end with the newest message.</summary>
    public static string NewestLabel => "Go to newest messages";

    public string BotName { get; set; } = string.Empty;

    public int BotHue { get; set; }

    /// <summary>The bot that wrote the messages a person would have, on a bot's side of work handed to it.</summary>
    public string? Asker { get; set; }

    /// <summary>True while the bot is working and has not started to answer.</summary>
    public bool Thinking { get; set; }

    /// <summary>
    /// The bot is thinking and no reply of its says so: a reply whose reasoning is arriving shows the bot thinking
    /// itself, so the list does not show it twice.
    /// </summary>
    public bool ShowsThinking => Thinking && Later == 0 && !Messages.Any(message => message.Thinking);

    /// <summary>Why the last reply failed and what to do, when it did.</summary>
    public string? Problem { get; set; }

    /// <summary>The run the conversation ends with, when it failed and nothing came after it.</summary>
    public RunFailure? Failure { get; set; }

    /// <summary>True while trying the failed run again is being asked for.</summary>
    public bool Retrying { get; set; }

    /// <summary>Starts a new run from the message the failed run answered.</summary>
    public Func<Task>? OnRetry { get; set; }

    /// <summary>True while a new version, or another version to show, is being asked for.</summary>
    public bool Changing { get; set; }

    /// <summary>Shows another version of a place in the conversation, by the id of that version.</summary>
    public Func<Guid, Task>? OnShowVersion { get; set; }

    /// <summary>Asks for another reply to the message a reply answers, by the id of the reply.</summary>
    public Func<Guid, Task>? OnRegenerate { get; set; }

    /// <summary>Sends an edit of a person's message, by its id, and has it answered.</summary>
    public Func<Guid, string, Task>? OnEdit { get; set; }

    /// <summary>Something to know about the last reply that is not a failure, such as that it was stopped.</summary>
    public string? Notice { get; set; }

    /// <summary>Reads what a finished reply's run used. Without it, replies show no usage.</summary>
    public Func<Guid, Task<RunUsageDetail>>? LoadUsage { get; set; }

    /// <summary>Where a failure to read what a reply used is written down.</summary>
    public ILogger Logger { get; set; } = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    /// <summary>True while a run of the conversation is under way.</summary>
    public bool Working { get; set; }

    /// <summary>True while the run waits for the person's answer.</summary>
    public bool Waiting { get; set; }

    /// <summary>The model the conversation answers with, when it is known.</summary>
    public string? Model { get; set; }

    /// <summary>True when that model was chosen for this conversation only.</summary>
    public bool ModelIsChosen { get; set; }

    /// <summary>The one line under the composer: what the bot is doing now, and what the newest reply used.</summary>
    public ConversationStatusViewModel Status { get; } = new();

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

    /// <summary>The bot's Limits settings, for a person who can edit the bot; null for everyone else.</summary>
    public string? LimitsHref { get; set; }

    /// <summary>Reads a text file's whole content for "Show all".</summary>
    public Func<SharedFile, Task<FileText>>? ReadWhole { get; set; }

    /// <summary>What a request for a person at the bot's screen needs here. Without it such a request is a plain tool card.</summary>
    public ScreenTakeovers? Takeovers { get; set; }

    /// <summary>The message the address points at, brought into view and focused once it is on the page.</summary>
    public Guid? Moment { get; set; }

    /// <summary>The group's accessible name.</summary>
    public string Name => $"Conversation with {BotName}";

    /// <summary>True until the conversation has been read: until then nothing is known to be said or not.</summary>
    public bool Loading { get; set; }

    /// <summary>
    /// Nothing was said and nothing is on its way: the bot stands there, with one line on what to do. Never while the
    /// conversation is still being read, so a conversation with messages never says it has none.
    /// </summary>
    public bool IsEmpty => !Loading && Messages.Count == 0 && !Thinking;

    public string StartHint => $"Nothing said yet. Write to {BotName} below.";

    /// <summary>What is read out, politely: the newest reply sentence by sentence, each tool call's change, a failure.</summary>
    public string? Spoken { get; private set; }

    /// <summary>The cause of the failure, as the line that ends the conversation says it.</summary>
    public FailureCause? FailureCause => Failure is { } failure ? AttentionWords.CauseOf(failure.Cause) : null;

    /// <summary>The first line of the failure card, naming the limit and its numbers when the server reported one.</summary>
    public string? FailureSentence => Failure is { } failure ? AttentionWords.Failure(failure, BotName) : null;

    /// <summary>Where the failure card's limits action goes: set for a limit stop when the person can edit the bot.</summary>
    public string? FailureLimitsHref => Failure?.Cause is FailureCauses.BudgetReached ? LimitsHref : null;

    /// <summary>The label of the failure card's limits action, or null when no limits action is offered.</summary>
    public string? FailureLimitsLabel => FailureLimitsHref is not null ? AttentionWords.OpenLimitsLabel : null;

    /// <summary>
    /// Raised to bring the newest message into view. For when the person sends: they want to see what they wrote,
    /// wherever they had scrolled to. Replies do not raise this, so reading further up is never interrupted.
    /// </summary>
    public event Action? NewestRequested;

    #endregion

    #region Public Methods

    public void ShowNewest()
    {
        if (Window().ShowNewest())
        {
            OnPropertyChanged(string.Empty);
        }

        NewestRequested?.Invoke();
    }

    /// <summary>Puts a step more of what was said before on the page, for a reader going back.</summary>
    public void ShowEarlier()
    {
        if (Window().ShowEarlier())
        {
            OnPropertyChanged(string.Empty);
        }
    }

    /// <summary>Puts a step more of what was said after on the page, for a reader coming forward.</summary>
    public void ShowLater()
    {
        if (Window().ShowLater())
        {
            OnPropertyChanged(string.Empty);
        }
    }

    /// <summary>Tries the failed run again. While that is being asked for, a second press does nothing.</summary>
    public Task Retry() => Retrying ? Task.CompletedTask : OnRetry?.Invoke() ?? Task.CompletedTask;

    /// <summary>Shows another version. While one change is being asked for, another does nothing.</summary>
    public Task ShowVersion(Guid messageId) => Changing ? Task.CompletedTask : OnShowVersion?.Invoke(messageId) ?? Task.CompletedTask;

    /// <summary>Asks for another reply. While one change is being asked for, another does nothing.</summary>
    public Task Regenerate(Guid replyId) => Changing ? Task.CompletedTask : OnRegenerate?.Invoke(replyId) ?? Task.CompletedTask;

    /// <summary>Sends an edit with words in it. While one change is being asked for, another does nothing.</summary>
    public Task Edit(Guid messageId, string text) =>
        Changing || string.IsNullOrWhiteSpace(text) ? Task.CompletedTask : OnEdit?.Invoke(messageId, text.Trim()) ?? Task.CompletedTask;

    /// <summary>The id a link to a message's moment names: <c>message-</c> and the message's id.</summary>
    public static string Anchor(Guid messageId) => MessageAnchors.Anchor(messageId);

    /// <summary>
    /// The message a moment link names can take focus, so the person lands on it. The only other message that is a
    /// stop is an older reply that keeps actions out of sight (<see cref="ChatRows.RevealsActions"/>).
    /// </summary>
    public bool IsMoment(ChatMessage message) => Moment is { } moment && message.Key == moment.ToString();

    /// <summary>
    /// True for a message of the newest reply (<see cref="ChatRows.NewestReply"/>), whose actions stay in view. An
    /// older reply shows its own only while asked.
    /// </summary>
    public bool IsNewestReply(ChatMessage message)
    {
        if (newest is null || !ReferenceEquals(newestOf, Messages))
        {
            (newestOf, newest) = (Messages, ChatRows.NewestReply(Messages));
        }

        return newest.Contains(message.Key);
    }

    /// <summary>
    /// Only a bot's newest message is read out, sentence by sentence. Older ones are already on the page. A tool call
    /// is read out once each time its state changes, and a failure once, when it appears.
    /// </summary>
    public void Update()
    {
        var sentences = Messages is [.., { FromPerson: false, UnusableToolRequest: false } newest]
            ? announcer.Next(newest.Key, newest.Text, newest.Streaming)
            : null;
        var changes = tools.Next(Messages.AllTools(BotName));
        Spoken = (sentences, changes) switch
        {
            (null, null) => Spoken,
            ({ } said, { } done) => $"{said} {done}",
            _ => sentences ?? changes,
        };

        if (Messages is [.., { FromPerson: false, UnusableToolRequest: true } unusable] && unusable.Key != announcedUnusable)
        {
            announcedUnusable = unusable.Key;
            Spoken = UnusableToolRequests.Announcement(BotName, ChatRows.OffersUnusableRetry(unusable, OnRegenerate is not null));
        }

        if (Failure is { } failure && failure.RunId != announcedFailure)
        {
            announcedFailure = failure.RunId;
            Spoken = AttentionWords.Failure(failure, BotName);
        }

        Status.Show(new(Messages, Working || Thinking, Thinking, Waiting, Model, ModelIsChosen), LoadUsage, Logger);
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
        Window().Reach(moment.ToString());
        return moment;
    }

    #endregion

    #region Private Methods

    // The page over the messages as they are now. A conversation is worked out anew each time it changes, so the
    // page is taken again whenever the list is another one.
    private ListWindow<ChatMessage> Window()
    {
        if (!ReferenceEquals(windowOf, Messages))
        {
            windowOf = Messages;
            window.Show(Messages);
        }

        return window;
    }

    #endregion
}
