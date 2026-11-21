namespace Drover.Contracts.Threads;

/// <summary>
/// The words to search for. <paramref name="Before"/> continues a search past the messages an earlier answer looked
/// at: it is the <see cref="MessageSearchResult.Older"/> of that answer, and only messages sent before it are searched.
/// The search covers the conversations the caller may read, their own and the ones shared with everyone in the space;
/// <paramref name="MineOnly"/> narrows it to the caller's own. It never widens what the caller may read.
/// </summary>
public sealed record SearchMessagesRequest(string? Query, SearchCursor? Before = null, bool MineOnly = false);


/// <summary>A place in the caller's messages, newest first: the time a message was sent and which message it was.</summary>
public sealed record SearchCursor(DateTimeOffset SentAt, Guid MessageId);
