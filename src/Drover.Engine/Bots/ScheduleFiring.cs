namespace Drover.Engine.Bots;

/// <summary>What one due schedule came to: what happened, the conversation it went to, and the run it queued.</summary>
public sealed record ScheduleFire(ScheduleOutcome Outcome, Guid? ThreadId, QueuedRun? Queued);

/// <summary>
/// What running a schedule once now came to: the conversation it went to and the run that answers it, or why it was
/// not sent. With neither a conversation nor a problem, there is no such schedule.
/// </summary>
public sealed record ScheduleTrial(Guid? ThreadId, Guid? RunId, string? Problem)
{
    public static ScheduleTrial NotFound { get; } = new(null, null, null);
}

/// <summary>
/// Sends one due schedule, inside a tenant scope as the schedule's owner in the schedule's space. The schedule's
/// message is stored and its run queued exactly as a person's message is (<see cref="RunService.PostMessage"/>), in one
/// transaction that also writes what happened and the next time it is due. From there the run is an ordinary run.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The scheduler claimed the schedule by moving its <c>next_run_at</c> to the end of its claim. Nothing is sent
/// unless the locked row still holds that claim, so a schedule changed, turned off, removed, or claimed again since
/// is left alone, and a schedule is sent once for each time it is due.</item>
/// <item>A paused bot sends nothing; the schedule stays on and is due again at its next time. A removed bot, or an
/// owner who has left the space, sends nothing, and the schedule is turned off: it could never send again.</item>
/// <item>The message is the owner's, marked as sent by the schedule, so a run takes it as the owner's like any other.
/// When the conversation already has a run under way, it waits for that run to end and then starts the next one, which
/// is triggered by the schedule as well.</item>
/// </list>
/// </remarks>
public sealed class ScheduleFiring(
    BotScheduleRepository schedules,
    BotRepository bots,
    AccountRepository accounts,
    ThreadRepository threads,
    MessageRepository messages,
    RunRepository runs,
    ThreadLine line,
    TenantScope scope,
    RunSignal signal,
    RunEventHub hub,
    UserSignalHub signals,
    TimeProvider time,
    ILogger<ScheduleFiring> logger)
{
    #region Private Fields

    private const int TitleLength = 80;

    #endregion

    #region Public Methods

    /// <summary>
    /// Sends the schedule if it still holds the claim that ends at <paramref name="claimUntil"/>. Null when it does
    /// not, and nothing was done.
    /// </summary>
    public async Task<ScheduleFire?> Fire(Guid scheduleId, DateTimeOffset claimUntil, CancellationToken cancellationToken)
    {
        BotSchedule schedule;
        ScheduleFire fire;

        await using (var work = await scope.Begin(cancellationToken))
        {
            if (await schedules.LockSchedule(scheduleId, cancellationToken) is not { } locked || locked.NextRunAt != claimUntil)
            {
                return null;
            }

            schedule = locked;
            var now = time.GetUtcNow();
            fire = await Handle(schedule, now, cancellationToken);
            Record(schedule, fire, now);
            await work.Commit(cancellationToken);
        }

        Publish(schedule.SpaceId, schedule.OwnerUserId, fire);
        logger.LogInformation(
            "Schedule {ScheduleId} of bot {BotId} was due: {Outcome}; run {RunId}",
            schedule.Id,
            schedule.BotId,
            fire.Outcome,
            fire.Queued?.Run.Id);
        return fire;
    }

    /// <summary>
    /// Sends the schedule's message once now, for the person who asked, as an ordinary run of theirs: their message,
    /// marked as sent by the schedule, in a conversation of theirs chosen by the schedule's thread mode, answered by a
    /// run that acts for them. Never for the schedule's owner, whose notes and conversations stay theirs. The schedule
    /// itself is not touched: its next time, its last run, and its claim stay as they are. Only owners and admins reach
    /// this, and the prompt was written by one, so it is the space's own instruction, like the bot's, not text from
    /// outside the space.
    /// </summary>
    public async Task<ScheduleTrial> RunOnce(Guid botId, Guid scheduleId, Guid userId, CancellationToken cancellationToken)
    {
        BotSchedule schedule;
        ScheduleFire fire;

        await using (var work = await scope.Begin(cancellationToken))
        {
            if (await bots.FindBot(botId, cancellationToken) is not { Status: not BotStatus.Removed } bot
                || await schedules.FindSchedule(botId, scheduleId, cancellationToken) is not { } found)
            {
                return ScheduleTrial.NotFound;
            }

            if (bot.Status is BotStatus.Paused)
            {
                return new ScheduleTrial(null, null, $"{bot.Name} is paused, so its routines do not run. Resume it first.");
            }

            schedule = found;
            fire = await Send(schedule, userId, time.GetUtcNow(), cancellationToken);
            await work.Commit(cancellationToken);
        }

        Publish(schedule.SpaceId, userId, fire);
        logger.LogInformation("Schedule {ScheduleId} of bot {BotId} was run once by hand; run {RunId}", schedule.Id, schedule.BotId, fire.Queued?.Run.Id);
        return new ScheduleTrial(fire.ThreadId, fire.Queued?.Run.Id, null);
    }

    #endregion

    #region Private Methods

    private async Task<ScheduleFire> Handle(BotSchedule schedule, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await accounts.FindRole(schedule.SpaceId, schedule.OwnerUserId, cancellationToken) is null)
        {
            return new ScheduleFire(ScheduleOutcome.OwnerLeft, null, null);
        }

        return await bots.FindBot(schedule.BotId, cancellationToken) switch
        {
            null or { Status: BotStatus.Removed } => new ScheduleFire(ScheduleOutcome.BotRemoved, null, null),
            { Status: BotStatus.Paused } => new ScheduleFire(ScheduleOutcome.BotPaused, null, null),
            _ => await Send(schedule, schedule.OwnerUserId, now, cancellationToken),
        };
    }

    /// <summary>
    /// Stores the schedule's message for <paramref name="principalUserId"/>, and queues the run that acts for them
    /// unless a run is already under way in that conversation.
    /// </summary>
    private async Task<ScheduleFire> Send(BotSchedule schedule, Guid principalUserId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var thread = await OpenThread(schedule, principalUserId, now, cancellationToken);
        var message = new Message
        {
            Role = MessageRole.User,
            AuthorUserId = principalUserId,
            ScheduleId = schedule.Id,
            Parts = [new TextPart(schedule.Prompt)],
            CreatedAt = now,
        };
        thread.LastActivityAt = now;

        if (await runs.FindActiveRun(thread.Id, cancellationToken) is not null)
        {
            messages.AddPending(thread, message);
            return new ScheduleFire(ScheduleOutcome.WaitingForReply, thread.Id, null);
        }

        await messages.AppendMessages(thread, [message], cancellationToken);
        return new ScheduleFire(ScheduleOutcome.Started, thread.Id, await line.Queue(thread, message, principalUserId, cancellationToken));
    }

    /// <summary>
    /// The conversation the schedule's message goes to, tracked: a new one, private to the person it is sent for and
    /// named after the prompt, or that person's main conversation with the bot, made now if they have none, and locked
    /// as a post locks it.
    /// </summary>
    private async Task<ChatThread> OpenThread(BotSchedule schedule, Guid principalUserId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (schedule.ThreadMode is ScheduleThreadMode.NewThread)
        {
            var thread = new ChatThread
            {
                BotId = schedule.BotId,
                OwnerUserId = principalUserId,
                AudienceUserId = principalUserId,
                Origin = ThreadOrigin.Schedule,
                Title = ThreadService.Shorten(schedule.Prompt.Trim()[..Math.Min(schedule.Prompt.Trim().Length, TitleLength)]),
                LastActivityAt = now,
            };
            threads.Add(thread);
            return thread;
        }

        await threads.AddMainThread(schedule.BotId, principalUserId, now, cancellationToken);
        var main = await threads.FindMainThread(schedule.BotId, principalUserId, cancellationToken)
            ?? throw new InvalidOperationException($"The main thread of bot {schedule.BotId} cannot be read by the person it is for.");
        return await threads.GetThreadForUpdate(main.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Thread {main.Id} cannot be locked by the person it is for.");
    }

    /// <summary>
    /// Writes what happened and when the schedule is next due, counted from now: a schedule that missed several times
    /// is due once. An owner who left, or a bot that was removed, turns the schedule off: it could never send again.
    /// </summary>
    private static void Record(BotSchedule schedule, ScheduleFire fire, DateTimeOffset now)
    {
        schedule.LastRunAt = now;
        schedule.LastOutcome = fire.Outcome;
        schedule.LastRunId = fire.Queued?.Run.Id;

        if (fire.Outcome is ScheduleOutcome.OwnerLeft or ScheduleOutcome.BotRemoved)
        {
            schedule.Enabled = false;
        }

        schedule.NextRunAt = ScheduleService.NextOccurrence(schedule, now);
    }

    /// <summary>Tells the workers, and the screens of the person it was sent for, once the transaction has committed.</summary>
    private void Publish(Guid spaceId, Guid principalUserId, ScheduleFire fire)
    {
        if (fire.Queued is { } queued)
        {
            hub.Publish(queued.Run.RootRunId, queued.Events);
            signal.Wake();
        }

        if (fire.ThreadId is { } threadId)
        {
            signals.Publish(spaceId, principalUserId, new UserSignal(UserSignalKinds.ThreadChanged, threadId, fire.Queued?.Run.Id));
        }
    }

    #endregion
}
