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

    #endregion

    #region Properties

    /// <summary>The words in the search box. The view sets them as the person types.</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>The words the shown results are for.</summary>
    public string? Searched => searched;

    public ConversationSearchState State { get; private set; }

    /// <summary>True while a search is shown in place of the crew.</summary>
    public bool IsActive => State is not ConversationSearchState.Idle;

    public IReadOnlyList<SearchHitRow> Hits => hits;

    /// <summary>Why the search could not be done, as a sentence.</summary>
    public string? Problem { get; private set; }

    /// <summary>What the list says about itself: searching, or how much it found. Read out when it changes.</summary>
    public string? Status => State switch
    {
        ConversationSearchState.Searching => SearchingWords,
        ConversationSearchState.Found when hits.Count >= MaxShown => $"The {hits.Count} best matches. Add a word to narrow them.",
        ConversationSearchState.Found when hits.Count == 1 => "1 message found.",
        ConversationSearchState.Found => $"{hits.Count} messages found.",
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

        var ask = ++asked;
        searched = words;
        Problem = null;
        State = ConversationSearchState.Searching;
        OnPropertyChanged(string.Empty);

        try
        {
            var found = await api.SearchMessages(SpaceId, words);

            if (ask == asked)
            {
                hits = [.. found.Select(ToRow)];
                State = hits.Count == 0 ? ConversationSearchState.NothingFound : ConversationSearchState.Found;
            }
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed in the {Screen}", nameof(ConversationSearchViewModel));

            if (ask == asked)
            {
                hits = [];
                Problem = Failures.Sentence(exception);
                State = ConversationSearchState.Failed;
            }
        }

        if (ask == asked)
        {
            OnPropertyChanged(string.Empty);
        }
    }

    /// <summary>Empties the box and puts the crew back.</summary>
    public void Clear()
    {
        asked++;
        Query = string.Empty;
        searched = null;
        hits = [];
        Problem = null;
        State = ConversationSearchState.Idle;
        OnPropertyChanged(string.Empty);
    }

    #endregion

    #region Private Methods

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

    public const string FailedTitle = "Drover could not search your conversations";

    public const string TryAgain = "Try again";

    public const string MainConversation = "Main conversation";

    public const string SideChat = "Side chat";

    #endregion
}
