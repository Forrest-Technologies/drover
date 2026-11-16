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
/// "waiting_for_reply" when its message waits for a reply under way, "bot_paused" or "bot_removed" when nothing was
/// sent, or "owner_left" when its owner had left the space and it was turned off. <see cref="LastRunId"/> is the run it
/// started, if it started one.
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
    string? LastOutcome = null);
