namespace Drover.Contracts.Threads;

/// <summary>
/// A conversation with one bot. <see cref="ActiveRunId"/> is the run still working on it, if any: a client opens
/// that run's event stream to follow it, and sending another message waits until it ends. <see cref="IsMain"/> marks
/// the caller's one main conversation with the bot; every other thread with it is a side chat.
/// </summary>
public sealed record ThreadSummary(
    Guid Id,
    Guid BotId,
    string? Title,
    bool IsShared,
    DateTimeOffset LastActivityAt,
    Guid? ActiveRunId,
    bool IsMain = false);
