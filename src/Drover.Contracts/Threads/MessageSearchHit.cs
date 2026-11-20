namespace Drover.Contracts.Threads;

/// <summary>
/// A message of the caller's conversations that matched a search. <see cref="InMainConversation"/> says the thread
/// is the caller's main conversation with the bot; any other thread opens as a side chat. <see cref="FromBot"/> says
/// the bot said it; otherwise a person did. <see cref="Snippet"/> is plain text from the message around the match,
/// and each of <see cref="Matches"/> is a stretch of it that matched the words searched for.
/// </summary>
public sealed record MessageSearchHit(
    Guid MessageId,
    Guid ThreadId,
    Guid BotId,
    string BotName,
    string? ThreadTitle,
    bool InMainConversation,
    bool FromBot,
    DateTimeOffset SentAt,
    string Snippet,
    IReadOnlyList<SnippetMatch> Matches);

/// <summary>A stretch of a snippet that matched: where it starts, in characters, and how long it is.</summary>
public sealed record SnippetMatch(int Start, int Length);


/// <summary>
/// What one search found. A search looks at the newest <paramref name="Window"/> messages the caller may read, or
/// at the next <paramref name="Window"/> before the cursor it was given. <paramref name="Older"/> is null when
/// every readable message has been looked at, and otherwise the cursor that continues the search into older messages.
/// </summary>
public sealed record MessageSearchResult(IReadOnlyList<MessageSearchHit> Hits, int Window, SearchCursor? Older);
