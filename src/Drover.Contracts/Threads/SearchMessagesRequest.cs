namespace Drover.Contracts.Threads;

/// <summary>
/// The words to search for. <paramref name="Before"/> continues a search past the messages an earlier answer looked
/// at: it is the <see cref="MessageSearchResult.Older"/> of that answer, and only messages sent before it are searched.
/// </summary>
public sealed record SearchMessagesRequest(string? Query, SearchCursor? Before = null);


/// <summary>A place in the caller's messages, newest first: the time a message was sent and which message it was.</summary>
public sealed record SearchCursor(DateTimeOffset SentAt, Guid MessageId);
