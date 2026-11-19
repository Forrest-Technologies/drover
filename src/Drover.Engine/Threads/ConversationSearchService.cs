namespace Drover.Engine.Threads;

/// <summary>
/// Finds the messages of a person's conversations that contain the words they typed. It runs in a tenant scope opened
/// after validation and concurrency checks, so row-level security keeps every search to the caller's space and to the
/// threads they may read: their own, and the ones shared with everyone.
/// </summary>
public sealed class ConversationSearchService(MessageRepository messages)
{
    #region Public Methods

    /// <summary>Why the words cannot be searched for, as a sentence, or null when they can.</summary>
    public static string? Refusal(string? words) => words?.Trim() switch
    {
        null or "" => "Say what to look for.",
        { Length: > MaxLength } => $"Search for at most {MaxLength} characters.",
        _ => null,
    };

    /// <summary>The messages that match, best first. Words that match nothing, such as only very common words, find nothing.</summary>
    public async Task<List<MessageSearchHit>> Search(string words, CancellationToken cancellationToken) =>
        [.. (await messages.SearchMessages(words.Trim(), MaxHits, cancellationToken)).Select(ToHit)];

    #endregion

    #region Private Methods

    private static MessageSearchHit ToHit(MessageMatch match)
    {
        var (snippet, matches) = Unmark(match.Snippet);
        return new(
            match.MessageId,
            match.ThreadId,
            match.BotId,
            match.BotName,
            match.ThreadTitle,
            match.InMainConversation,
            match.FromBot,
            match.CreatedAt,
            snippet,
            matches);
    }

    // The database marks each matched word; the marks become stretches of the plain text they are taken out of.
    private static (string Snippet, List<SnippetMatch> Matches) Unmark(string marked)
    {
        var text = new StringBuilder(marked.Length);
        List<SnippetMatch> matches = [];
        int? start = null;

        foreach (var character in marked)
        {
            switch (character)
            {
                case MessageRepository.MatchStart:
                    start = text.Length;
                    break;
                case MessageRepository.MatchEnd when start is { } from:
                    matches.Add(new(from, text.Length - from));
                    start = null;
                    break;
                case MessageRepository.MatchEnd:
                    break;
                default:
                    text.Append(character);
                    break;
            }
        }

        return (text.ToString(), matches);
    }

    #endregion

    #region Limits

    /// <summary>The most messages one search returns, best match first.</summary>
    public const int MaxHits = 30;

    /// <summary>The longest search, in characters.</summary>
    public const int MaxLength = 200;

    #endregion
}
