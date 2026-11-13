namespace Drover.Engine.Bots;

/// <summary>What a person sets on a schedule. A null thread mode is a word the request spelled wrongly.</summary>
public sealed record ScheduleEdit(string Cron, string TimeZone, string Prompt, ScheduleThreadMode? ThreadMode, bool Enabled);

/// <summary>The result of a change to a schedule. With neither a schedule nor a problem, there is none such.</summary>
public sealed record ScheduleChange(BotSchedule? Schedule, string? Problem)
{
    public static ScheduleChange NotFound { get; } = new(null, null);
}

/// <summary>
/// A bot's schedules. They are checked and stored here, with the next time each is due. Nothing here runs one.
/// </summary>
public sealed class ScheduleService(
    BotRepository bots,
    BotScheduleRepository schedules,
    AuditLog audit,
    TimeProvider time,
    ILogger<ScheduleService> logger)
{
    #region Private Fields

    private const int MaxSchedulesPerBot = 20;

    private const int MaxPromptLength = 8_000;

    private const int MaxCronLength = 100;

    #endregion

    #region Public Methods

    /// <summary>The bot's schedules, or null when there is no such bot.</summary>
    public async Task<List<BotSchedule>?> ListSchedules(Guid botId, CancellationToken cancellationToken) =>
        await FindBot(botId, cancellationToken) is null ? null : await schedules.ListSchedules(botId, cancellationToken);

    public async Task<ScheduleChange> Create(Guid botId, ScheduleEdit edit, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (await FindBot(botId, cancellationToken) is null)
        {
            return ScheduleChange.NotFound;
        }

        if (FindProblem(edit) is { } problem)
        {
            return new ScheduleChange(null, problem);
        }

        if ((await schedules.ListSchedules(botId, cancellationToken)).Count >= MaxSchedulesPerBot)
        {
            return new ScheduleChange(null, $"A bot has at most {MaxSchedulesPerBot} schedules. Remove one first.");
        }

        var schedule = new BotSchedule
        {
            BotId = botId,
            OwnerUserId = actorUserId,
            Cron = edit.Cron.Trim(),
            TimeZone = edit.TimeZone.Trim(),
            Prompt = edit.Prompt,
            ThreadMode = edit.ThreadMode!.Value,
            Enabled = edit.Enabled,
        };
        schedule.NextRunAt = NextRun(schedule);
        schedules.Add(schedule);
        audit.Record(actorUserId, "bot.schedule_added", AuditTargets.Bot, botId, null, Snapshot(schedule), About(schedule));

        logger.LogInformation("Schedule {ScheduleId} added to bot {BotId}", schedule.Id, botId);
        return new ScheduleChange(schedule, null);
    }

    public async Task<ScheduleChange> Update(Guid botId, Guid scheduleId, ScheduleEdit edit, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (await FindBot(botId, cancellationToken) is null
            || await schedules.GetScheduleForUpdate(botId, scheduleId, cancellationToken) is not { } schedule)
        {
            return ScheduleChange.NotFound;
        }

        if (FindProblem(edit) is { } problem)
        {
            return new ScheduleChange(null, problem);
        }

        var before = Snapshot(schedule);
        schedule.Cron = edit.Cron.Trim();
        schedule.TimeZone = edit.TimeZone.Trim();
        schedule.Prompt = edit.Prompt;
        schedule.ThreadMode = edit.ThreadMode!.Value;
        schedule.Enabled = edit.Enabled;
        schedule.NextRunAt = NextRun(schedule);

        if (audit.Record(actorUserId, "bot.schedule_changed", AuditTargets.Bot, botId, before, Snapshot(schedule), About(schedule)))
        {
            logger.LogInformation("Schedule {ScheduleId} of bot {BotId} changed", schedule.Id, botId);
        }

        return new ScheduleChange(schedule, null);
    }

    /// <summary>Removes a schedule. False when there is no such bot or schedule.</summary>
    public async Task<bool> Remove(Guid botId, Guid scheduleId, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (await FindBot(botId, cancellationToken) is null
            || await schedules.GetScheduleForUpdate(botId, scheduleId, cancellationToken) is not { } schedule)
        {
            return false;
        }

        schedules.Remove(schedule);
        audit.Record(actorUserId, "bot.schedule_removed", AuditTargets.Bot, botId, Snapshot(schedule), null, About(schedule));

        logger.LogInformation("Schedule {ScheduleId} removed from bot {BotId}", schedule.Id, botId);
        return true;
    }

    #endregion

    #region Private Methods

    private static JsonObject Snapshot(BotSchedule schedule) =>
        AuditLog.Snapshot(new { schedule.Cron, schedule.TimeZone, schedule.Prompt, schedule.ThreadMode, schedule.Enabled });

    private static JsonObject About(BotSchedule schedule) => new() { ["schedule_id"] = schedule.Id };

    private static string? FindProblem(ScheduleEdit edit)
    {
        if (edit.Cron.Trim().Length > MaxCronLength || !CronExpression.TryParse(edit.Cron.Trim(), CronFormat.Standard, out _))
        {
            return "Write the time as five cron fields: minute, hour, day of month, month, day of week. For example 0 9 * * 1-5.";
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(edit.TimeZone.Trim(), out _))
        {
            return "Choose a time zone by its IANA name, for example Europe/Berlin.";
        }

        if (string.IsNullOrWhiteSpace(edit.Prompt) || edit.Prompt.Length > MaxPromptLength)
        {
            return $"Write what to ask the bot, in up to {MaxPromptLength} characters.";
        }

        return edit.ThreadMode is null ? "A schedule posts to a new_thread each time or to the same_thread." : null;
    }

    private DateTimeOffset? NextRun(BotSchedule schedule) =>
        schedule.Enabled
            ? CronExpression.Parse(schedule.Cron, CronFormat.Standard)
                .GetNextOccurrence(time.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone))?.ToUniversalTime()
            : null;

    private async Task<Bot?> FindBot(Guid botId, CancellationToken cancellationToken) =>
        await bots.FindBot(botId, cancellationToken) is { Status: not BotStatus.Removed } bot ? bot : null;

    #endregion
}
