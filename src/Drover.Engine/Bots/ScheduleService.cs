namespace Drover.Engine.Bots;

/// <summary>What a person sets on a schedule. A null thread mode is a word the request spelled wrongly.</summary>
public sealed record ScheduleEdit(string Cron, string TimeZone, string Prompt, ScheduleThreadMode? ThreadMode, bool Enabled);

/// <summary>
/// The result of a change to a schedule, with how its last run stands for the person who made the change. With
/// neither a schedule nor a problem, there is none such.
/// </summary>
public sealed record ScheduleChange(BotSchedule? Schedule, string? Problem, RunGlance? LastRun = null)
{
    public static ScheduleChange NotFound { get; } = new(null, null);
}

/// <summary>A time checked before it is saved: the server's sentence when it would refuse it, or the next times it runs.</summary>
public sealed record ScheduleCheck(string? Problem, IReadOnlyList<DateTimeOffset> Next);

/// <summary>
/// A schedule and, when the last time it was due started a run whose conversation the caller can see, how that run
/// stands now. A run in another person's private conversation is left out: the caller learns only that it started.
/// </summary>
public sealed record ScheduleView(BotSchedule Schedule, RunGlance? LastRun);

/// <summary>
/// A bot's schedules. They are checked and stored here, with the next time each is due; <see cref="ScheduleFiring"/>
/// sends one when that time comes. A schedule is checked against the deployment's rules on the shortest time between
/// two runs and the most schedules a space may hold.
/// </summary>
public sealed class ScheduleService(
    BotRepository bots,
    BotScheduleRepository schedules,
    RunRepository runs,
    AuditLog audit,
    TimeProvider time,
    IOptions<DeploymentPolicyOptions> deployment,
    ILogger<ScheduleService> logger)
{
    #region Private Fields

    private const int MaxSchedulesPerBot = 20;

    private const int MaxPromptLength = 8_000;

    private const int MaxCronLength = 100;

    private const int CheckedTimes = 3;

    private const string NotTheOwners =
        "Only the person who made this schedule can change what it sends or when, or turn it back on, because it runs as them. You can turn it off or remove it.";

    /// <summary>How far ahead a schedule's times are compared to find the shortest gap between two of them.</summary>
    private static readonly TimeSpan gapSearch = TimeSpan.FromDays(366);

    #endregion

    #region Public Methods

    /// <summary>The bot's schedules, each with how its last run is going, or null when there is no such bot.</summary>
    public async Task<List<ScheduleView>?> ListSchedules(Guid botId, CancellationToken cancellationToken)
    {
        if (await FindBot(botId, cancellationToken) is null)
        {
            return null;
        }

        var found = await schedules.ListSchedules(botId, cancellationToken);
        var glances = await runs.ListVisibleGlances([.. found.Select(schedule => schedule.LastRunId).OfType<Guid>()], cancellationToken);
        return [.. found.Select(schedule => new ScheduleView(schedule, Glance(schedule, glances)))];
    }

    /// <summary>
    /// The first time the schedule is due after <paramref name="after"/>, in UTC, or null when it is off. Counted from
    /// a moment, not from the last time it was due, so a schedule that missed several times while the server was down
    /// is due once, not once for each.
    /// </summary>
    public static DateTimeOffset? NextOccurrence(BotSchedule schedule, DateTimeOffset after) =>
        schedule.Enabled
            ? CronExpression.Parse(schedule.Cron, CronFormat.Standard)
                .GetNextOccurrence(after, TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone))?.ToUniversalTime()
            : null;

    /// <summary>
    /// Checks a time as saving it would: the sentence a save would refuse it with, or its next three times in UTC.
    /// Lets the editor explain an expression it cannot put in words before anything is saved.
    /// </summary>
    public ScheduleCheck Check(string cron, string timeZone)
    {
        var edit = new ScheduleEdit(cron, timeZone, string.Empty, ScheduleThreadMode.NewThread, true);

        if ((FindTimeProblem(edit) ?? FindIntervalProblem(edit)) is { } problem)
        {
            return new ScheduleCheck(problem, []);
        }

        var from = time.GetUtcNow();
        var next = CronExpression.Parse(edit.Cron.Trim(), CronFormat.Standard)
            .GetOccurrences(from, from + gapSearch, TimeZoneInfo.FindSystemTimeZoneById(edit.TimeZone.Trim()), fromInclusive: false)
            .Take(CheckedTimes)
            .Select(occurrence => occurrence.ToUniversalTime());
        return new ScheduleCheck(null, [.. next]);
    }

    public Task<ScheduleChange> Create(Guid botId, ScheduleEdit edit, Guid actorUserId, CancellationToken cancellationToken) =>
        Create(botId, edit, AuditActor.Person(actorUserId), cancellationToken);

    internal async Task<ScheduleChange> Create(Guid botId, ScheduleEdit edit, AuditActor actor, CancellationToken cancellationToken)
    {
        if (await FindBot(botId, cancellationToken) is null)
        {
            return ScheduleChange.NotFound;
        }

        if (FindProblem(edit) is { } problem)
        {
            return new ScheduleChange(null, problem);
        }

        var maxPerSpace = deployment.Value.Rules.MaxSchedulesPerSpace;
        await schedules.LockSpaceSchedules(cancellationToken);

        if (await schedules.CountSchedules(cancellationToken) >= maxPerSpace)
        {
            return new ScheduleChange(null, $"A space on this server holds at most {maxPerSpace} schedules. Remove one first.");
        }

        if ((await schedules.ListSchedules(botId, cancellationToken)).Count >= MaxSchedulesPerBot)
        {
            return new ScheduleChange(null, $"A bot has at most {MaxSchedulesPerBot} schedules. Remove one first.");
        }

        var schedule = new BotSchedule
        {
            BotId = botId,
            OwnerUserId = actor.UserId,
            Cron = edit.Cron.Trim(),
            TimeZone = edit.TimeZone.Trim(),
            Prompt = edit.Prompt,
            ThreadMode = edit.ThreadMode!.Value,
            Enabled = edit.Enabled,
        };
        schedule.NextRunAt = NextRun(schedule);
        schedules.Add(schedule);
        audit.Record(actor, "bot.schedule_added", AuditTargets.Bot, botId, null, Snapshot(schedule), About(schedule));

        logger.LogInformation("Schedule {ScheduleId} added to bot {BotId}", schedule.Id, botId);
        return new ScheduleChange(schedule, null);
    }

    public Task<ScheduleChange> Update(Guid botId, Guid scheduleId, ScheduleEdit edit, Guid actorUserId, CancellationToken cancellationToken) =>
        Update(botId, scheduleId, edit, AuditActor.Person(actorUserId), cancellationToken);

    internal async Task<ScheduleChange> Update(Guid botId, Guid scheduleId, ScheduleEdit edit, AuditActor actor, CancellationToken cancellationToken)
    {
        if (await FindBot(botId, cancellationToken) is null
            || await schedules.GetScheduleForUpdate(botId, scheduleId, cancellationToken) is not { } schedule)
        {
            return ScheduleChange.NotFound;
        }

        if (schedule.OwnerUserId != actor.UserId)
        {
            if (actor.BotId is not null)
            {
                return ScheduleChange.NotFound;
            }

            var turnedOff = TurnOffForOwner(schedule, edit, actor.UserId);
            return turnedOff.Schedule is { } kept ? await WithLastRun(kept, cancellationToken) : turnedOff;
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

        if (audit.Record(actor, "bot.schedule_changed", AuditTargets.Bot, botId, before, Snapshot(schedule), About(schedule)))
        {
            logger.LogInformation("Schedule {ScheduleId} of bot {BotId} changed", schedule.Id, botId);
        }

        return await WithLastRun(schedule, cancellationToken);
    }

    /// <summary>Removes a schedule. False when there is no such bot or schedule.</summary>
    public Task<bool> Remove(Guid botId, Guid scheduleId, Guid actorUserId, CancellationToken cancellationToken) =>
        Remove(botId, scheduleId, AuditActor.Person(actorUserId), cancellationToken);

    internal async Task<bool> Remove(Guid botId, Guid scheduleId, AuditActor actor, CancellationToken cancellationToken)
    {
        if (await FindBot(botId, cancellationToken) is null
            || await schedules.GetScheduleForUpdate(botId, scheduleId, cancellationToken) is not { } schedule)
        {
            return false;
        }

        if (actor.BotId is not null && schedule.OwnerUserId != actor.UserId)
        {
            return false;
        }

        schedules.Remove(schedule);
        audit.Record(actor, "bot.schedule_removed", AuditTargets.Bot, botId, Snapshot(schedule), null, About(schedule));

        logger.LogInformation("Schedule {ScheduleId} removed from bot {BotId}", schedule.Id, botId);
        return true;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// A schedule runs as the person who made it, with their notes, their sign-ins, and their private conversation,
    /// so nobody else may change what it sends or when. Another admin may turn it off (or remove it), and nothing more.
    /// </summary>
    private ScheduleChange TurnOffForOwner(BotSchedule schedule, ScheduleEdit edit, Guid actorUserId)
    {
        var rewrites = edit.Cron.Trim() != schedule.Cron
            || edit.TimeZone.Trim() != schedule.TimeZone
            || edit.Prompt != schedule.Prompt
            || edit.ThreadMode != schedule.ThreadMode;

        if (rewrites || (edit.Enabled && !schedule.Enabled))
        {
            return new ScheduleChange(null, NotTheOwners);
        }

        if (!edit.Enabled && schedule.Enabled)
        {
            var before = Snapshot(schedule);
            schedule.Enabled = false;
            schedule.NextRunAt = null;
            audit.Record(actorUserId, "bot.schedule_changed", AuditTargets.Bot, schedule.BotId, before, Snapshot(schedule), About(schedule));
            logger.LogInformation("Schedule {ScheduleId} of bot {BotId} turned off by someone other than its owner", schedule.Id, schedule.BotId);
        }

        return new ScheduleChange(schedule, null);
    }

    private static RunGlance? Glance(BotSchedule schedule, Dictionary<Guid, RunGlance> glances) =>
        schedule.LastRunId is { } runId && glances.TryGetValue(runId, out var glance) ? glance : null;

    private async Task<ScheduleChange> WithLastRun(BotSchedule schedule, CancellationToken cancellationToken) =>
        new(schedule, null, Glance(schedule, await runs.ListVisibleGlances(schedule.LastRunId is { } runId ? [runId] : [], cancellationToken)));

    private static JsonObject Snapshot(BotSchedule schedule) =>
        AuditLog.Snapshot(new { schedule.Cron, schedule.TimeZone, schedule.Prompt, schedule.ThreadMode, schedule.Enabled });

    private static JsonObject About(BotSchedule schedule) => new() { ["schedule_id"] = schedule.Id };

    private string? FindProblem(ScheduleEdit edit) => FindWritingProblem(edit) ?? FindIntervalProblem(edit);

    private static string? FindTimeProblem(ScheduleEdit edit)
    {
        if (edit.Cron.Trim().Length > MaxCronLength || !CronExpression.TryParse(edit.Cron.Trim(), CronFormat.Standard, out _))
        {
            return "Write the time as five cron fields: minute, hour, day of month, month, day of week. For example 0 9 * * 1-5.";
        }

        return TimeZoneInfo.TryFindSystemTimeZoneById(edit.TimeZone.Trim(), out _)
            ? null
            : "Choose a time zone by its IANA name, for example Europe/Berlin.";
    }

    private static string? FindWritingProblem(ScheduleEdit edit)
    {
        if (FindTimeProblem(edit) is { } problem)
        {
            return problem;
        }

        if (string.IsNullOrWhiteSpace(edit.Prompt) || edit.Prompt.Length > MaxPromptLength)
        {
            return $"Write what to ask the bot, in up to {MaxPromptLength} characters.";
        }

        return edit.ThreadMode is null ? "A schedule posts to a new_thread each time or to the same_thread." : null;
    }

    /// <summary>
    /// Says so when two of the schedule's times in the coming year are closer together than the deployment allows. A
    /// floor of one minute or less allows every schedule, since cron has nothing finer.
    /// </summary>
    private string? FindIntervalProblem(ScheduleEdit edit)
    {
        var minutes = deployment.Value.Rules.MinScheduleIntervalMinutes;

        if (minutes <= 1)
        {
            return null;
        }

        var from = time.GetUtcNow();
        var times = CronExpression.Parse(edit.Cron.Trim(), CronFormat.Standard)
            .GetOccurrences(from, from + gapSearch, TimeZoneInfo.FindSystemTimeZoneById(edit.TimeZone.Trim()));
        DateTimeOffset? previous = null;

        foreach (var next in times)
        {
            if (previous is { } last && next - last < TimeSpan.FromMinutes(minutes))
            {
                return $"Schedules on this server run at most once every {minutes} minutes. Choose a time that repeats less often.";
            }

            previous = next;
        }

        return null;
    }

    private DateTimeOffset? NextRun(BotSchedule schedule) => NextOccurrence(schedule, time.GetUtcNow());

    private async Task<Bot?> FindBot(Guid botId, CancellationToken cancellationToken) =>
        await bots.FindBot(botId, cancellationToken) is { Status: not BotStatus.Removed } bot ? bot : null;

    #endregion
}
