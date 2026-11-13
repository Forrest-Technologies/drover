namespace Drover.Contracts.Bots;

/// <summary>
/// A time at which a bot is sent a prompt. Schedules are stored now and run once the scheduler ships. The time is a
/// five-field cron expression in an IANA time zone, and the thread mode is "new_thread" or "same_thread".
/// </summary>
public sealed record BotScheduleBody(string? Cron, string? TimeZone, string? Prompt, string? ThreadMode, bool Enabled);

public sealed record BotScheduleResponse(
    Guid Id,
    Guid BotId,
    Guid OwnerUserId,
    string Cron,
    string TimeZone,
    string Prompt,
    string ThreadMode,
    bool Enabled,
    DateTimeOffset? NextRunAt);
