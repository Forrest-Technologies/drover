namespace Drover.Presentation.ViewModels.Chat;

/// <summary>What a search of the person's conversations is showing.</summary>
public enum ConversationSearchState
{
    /// <summary>Nothing searched for: the crew shows instead.</summary>
    Idle,
    Searching,
    Found,
    NothingFound,
    Failed,
}


/// <summary>
/// One message a search found, as the list shows it: who said it to whom, where, when, the words around the match,
/// and the address that opens its conversation at it.
/// </summary>
/// <param name="Key">The message.</param>
/// <param name="Href">Its conversation, scrolled to it: the main conversation or the side chat it was said in.</param>
/// <param name="BotName">The bot of the conversation, whose head the row shows.</param>
/// <param name="BotHue">That bot's identity hue.</param>
/// <param name="Heading">"Message to" or "Reply from", and the bot.</param>
/// <param name="Snippet">The words of the message around the match, as plain text.</param>
/// <param name="Matches">The stretches of <paramref name="Snippet"/> that matched the words searched for.</param>
/// <param name="Facts">The conversation it was said in and when.</param>
/// <param name="AccessibleName">All of it in one sentence, for assistive technology.</param>
public sealed record SearchHitRow(
    Guid Key,
    string Href,
    string BotName,
    int BotHue,
    string Heading,
    string Snippet,
    IReadOnlyList<SnippetMatch> Matches,
    string Facts,
    string AccessibleName);


/// <summary>
/// Searches the words of the person's conversations, on the server, and lists the messages that match. Choosing one
/// opens its conversation scrolled to it. It never decides who may read what: the server only searches the threads
/// the person may read.
/// </summary>
public sealed class ConversationSearchViewModel(
    DroverApi api,
    SessionState session,
    ILogger<ConversationSearchViewModel> logger) : ObservableObject
{
    #region Private Fields

    private IReadOnlyList<SearchHitRow> hits = [];

    private string? searched;

    private int asked;

    // Where the search stopped, when older messages were not looked at, and how many messages it has looked at.
    private SearchCursor? older;

    private int looked;

    private bool firstPage = true;

    // The page that failed, so trying again asks for that page and keeps what was already found.
    private SearchCursor? failedBefore;

    private bool failedOlder;

    #endregion

    #region Properties

    /// <summary>The words in the search box. The view sets them as the person types.</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>The words the shown results are for.</summary>
    public string? Searched => searched;

    public ConversationSearchState State { get; private set; }

    /// <summary>True when older messages were not searched and the person can ask for them.</summary>
    public bool CanSearchOlder => older is not null && State is ConversationSearchState.Found or ConversationSearchState.NothingFound;

    /// <summary>How far back the search looked, when it did not look at everything. Null once it has.</summary>
    public string? Coverage => older is null ? null : $"Searched your newest {looked:N0} messages.";

    /// <summary>The cause the nothing-found state gives: it never claims more than the search looked at.</summary>
    public string NothingExplanation => older is null
        ? NothingCause
        : $"No message in your newest {looked:N0} messages has these words. Older messages are not searched until you ask.";

    /// <summary>True while a search is shown in place of the crew.</summary>
    public bool IsActive => State is not ConversationSearchState.Idle;

    public IReadOnlyList<SearchHitRow> Hits => hits;

    /// <summary>Why the search could not be done, as a sentence.</summary>
    public string? Problem { get; private set; }

    /// <summary>What the list says about itself: searching, or how much it found. Read out when it changes.</summary>
    public string? Status => State switch
    {
        ConversationSearchState.Searching => SearchingWords,
        ConversationSearchState.Found => Joined(FoundWords, Coverage),
        ConversationSearchState.NothingFound => Coverage,
        _ => null,
    };

    /// <summary>The title of the state that found nothing.</summary>
    public string NothingTitle => $"Nothing matches “{searched}”";

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    #endregion

    #region Public Methods

    /// <summary>
    /// Searches for the words in <see cref="Query"/>. Blank words clear the search. An answer to an earlier search
    /// that arrives after a later one began is dropped.
    /// </summary>
    public async Task Search()
    {
        var words = Query.Trim();

        if (words.Length == 0)
        {
            Clear();
            return;
        }

        await Run(words, null);
    }

    /// <summary>
    /// Asks again for what failed. When searching older messages failed, it asks for those messages again and keeps
    /// the results already found; otherwise it searches from the newest messages.
    /// </summary>
    public async Task Retry()
    {
        if (failedOlder && searched is { } words)
        {
            await Run(words, failedBefore);
            return;
        }

        await Search();
    }

    /// <summary>
    /// Searches the messages before the ones already searched and adds what it finds to the list. It does nothing
    /// when every message has been searched.
    /// </summary>
    public async Task SearchOlder()
    {
        if (older is not null && searched is { } words)
        {
            await Run(words, older);
        }
    }

    /// <summary>Empties the box and puts the crew back.</summary>
    public void Clear()
    {
        asked++;
        Query = string.Empty;
        searched = null;
        hits = [];
        older = null;
        looked = 0;
        failedOlder = false;
        failedBefore = null;
        Problem = null;
        State = ConversationSearchState.Idle;
        OnPropertyChanged(string.Empty);
    }

    #endregion

    #region Private Methods

    private async Task Run(string words, SearchCursor? before)
    {
        var ask = ++asked;
        searched = words;
        failedOlder = false;
        Problem = null;
        State = ConversationSearchState.Searching;
        OnPropertyChanged(string.Empty);

        try
        {
            var found = await api.SearchMessages(SpaceId, words, before);

            if (ask == asked)
            {
                var rows = found.Hits.Select(ToRow);
                hits = before is null ? [.. rows] : [.. hits, .. rows.Where(row => hits.All(shown => shown.Key != row.Key))];
                firstPage = before is null;
                looked = before is null ? found.Window : looked + found.Window;
                older = found.Older;
                State = hits.Count == 0 ? ConversationSearchState.NothingFound : ConversationSearchState.Found;
            }
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationSearchViewModel));

            if (ask == asked)
            {
                // A failed page of older messages keeps what was found and where it stopped.
                failedOlder = before is not null;
                failedBefore = before;
                hits = failedOlder ? hits : [];
                older = failedOlder ? older : null;
                looked = failedOlder ? looked : 0;
                Problem = Failures.Sentence(exception);
                State = ConversationSearchState.Failed;
            }
        }

        if (ask == asked)
        {
            OnPropertyChanged(string.Empty);
        }
    }

    private string FoundWords => (firstPage && hits.Count >= MaxShown, hits.Count) switch
    {
        (true, _) => $"The {hits.Count} best matches. Add a word to narrow them.",
        (_, 1) => "1 message found.",
        _ => $"{hits.Count} messages found.",
    };

    private static string Joined(string first, string? second) => second is null ? first : $"{first} {second}";

    private static SearchHitRow ToRow(MessageSearchHit hit)
    {
        var heading = hit.FromBot ? $"Reply from {hit.BotName}" : $"Message to {hit.BotName}";
        var place = hit.InMainConversation ? MainConversation : hit.ThreadTitle ?? SideChat;
        var when = ScheduleWords.Moment(hit.SentAt);
        var spokenPlace = (hit.InMainConversation, hit.ThreadTitle) switch
        {
            (true, _) => "the main conversation",
            (_, { } title) => $"the side chat {title}",
            _ => "a side chat",
        };
        return new(
            hit.MessageId,
            Addresses.Moment(hit.BotId, hit.ThreadId, hit.MessageId, hit.InMainConversation),
            hit.BotName,
            BotIdentity.Hue(hit.BotId),
            heading,
            hit.Snippet,
            hit.Matches,
            $"{place} · {when}",
            $"{heading} in {spokenPlace}, {when}: {hit.Snippet}");
    }

    #endregion

    #region Fixed Words

    /// <summary>The most results the server sends for one search.</summary>
    public const int MaxShown = 30;

    public const string Label = "Search conversations";

    public const string Placeholder = "Words someone said";

    public const string SearchingWords = "Searching your conversations.";

    public const string NothingCause = "No message in your conversations has these words. Try other words, or fewer of them.";

    public const string ClearAction = "Clear the search";

    public const string OlderAction = "Search older messages";

    public const string FailedTitle = "Drover could not search your conversations";

    public const string TryAgain = "Try again";

    public const string MainConversation = "Main conversation";

    public const string SideChat = "Side chat";

    #endregion
}
