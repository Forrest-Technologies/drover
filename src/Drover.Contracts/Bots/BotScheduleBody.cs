namespace Drover.Contracts.Bots;

/// <summary>
/// A time at which a bot is sent a prompt. The time is a five-field cron expression in an IANA time zone, and the
/// thread mode is "new_thread" (a new conversation each time) or "same_thread" (the owner's main conversation with the
/// bot).
/// </summary>
public sealed record BotScheduleBody(string? Cron, string? TimeZone, string? Prompt, string? ThreadMode, bool Enabled);

/// <summary>
/// A stored schedule. <see cref="LastRunAt"/> is when it was last due and handled. <see cref="LastOutcome"/> says what
/// happened then: the status of the run it started (queued, running, waiting, completed, failed, or cancelled), or
/// "started" when that run's conversation is private to someone other than the caller; "waiting_for_reply" when its
/// message waits for a reply under way, "bot_paused" or "bot_removed" when nothing was sent, or "owner_left" when its
/// owner had left the space and it was turned off. <see cref="LastRunId"/>, <see cref="LastThreadId"/> (the
/// conversation of that run) and <see cref="LastReason"/> (why it failed, in words) are given only to a caller who can
/// see that conversation.
/// </summary>
public sealed record BotScheduleResponse(
    Guid Id,
    Guid BotId,
    Guid OwnerUserId,
    string Cron,
    string TimeZone,
    string Prompt,
    string ThreadMode,
    bool Enabled,
    DateTimeOffset? NextRunAt,
    Guid? LastRunId = null,
    DateTimeOffset? LastRunAt = null,
    string? LastOutcome = null,
    Guid? LastThreadId = null,
    string? LastReason = null);

/// <summary>
/// A routine run once now, for the person who asked: the conversation it went to, and the run that answers it. The
/// run is null when that conversation already had a run under way, and the message waits for it to end.
/// </summary>
public sealed record ScheduleRunOnceResponse(Guid ThreadId, Guid? RunId);

/// <summary>A time to check before it is saved: a five-field cron expression in an IANA time zone.</summary>
public sealed record ScheduleCheckBody(string? Cron, string? TimeZone);

/// <summary>
/// What saving that time would come to: the sentence the server would refuse it with, or the next times it runs, in
/// UTC, three at most.
/// </summary>
public sealed record ScheduleCheckResponse(string? Problem, IReadOnlyList<DateTimeOffset> Next);
