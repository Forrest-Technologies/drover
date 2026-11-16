namespace Drover.Engine.Tests.Bots;

/// <summary>
/// Schedules coming due, over PostgreSQL and the real engine, on a clock the test moves: a due schedule starts one
/// run, triggered by the schedule, as its owner, in the conversation its thread mode names; and each edge case of
/// docs/architecture/03-run-lifecycle.md "Schedules". The hosts execute no runs, so each run stays as it was queued.
/// </summary>
public sealed class ScheduleFiringTests(PostgresServer postgres)
{
    #region Private Fields

    private const string Prompt = "Brief me on what changed overnight.";

    /// <summary>A Monday, an hour before a daily nine o'clock schedule is due.</summary>
    private static readonly DateTimeOffset monday = new(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);

    #endregion

    #region Firing

    [Fact]
    public async Task A_due_schedule_starts_exactly_one_run_triggered_by_the_schedule_as_its_owner_in_a_new_conversation()
    {
        var clock = new MovableClock(monday);
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.IdleHost(services => services.AddSingleton<TimeProvider>(clock));
        var schedule = await AddSchedule(world, host, world.UserId, "0 9 * * *", ScheduleThreadMode.NewThread);

        var early = await SendDue(host);
        clock.Now = monday.AddHours(1).AddSeconds(20);
        var due = await SendDue(host);
        var again = await SendDue(host);

        early.Should().Be(0, "the schedule is not due before nine");
        due.Should().Be(1);
        again.Should().Be(0, "a schedule is sent once for each time it is due");
        var runs = await world.Read(db => db.Runs.AsNoTracking().ToListAsync(RunWorld.Token));
        var run = runs.Should().ContainSingle().Which;
        run.Trigger.Should().Be(RunTrigger.Schedule);
        run.PrincipalUserId.Should().Be(world.UserId);
        run.Status.Should().Be(RunStatus.Queued);
        var thread = await world.ReadThread(run.ThreadId);
        thread.Should().Match<ChatThread>(found =>
            found.Origin == ThreadOrigin.Schedule && !found.IsMain && found.OwnerUserId == world.UserId && found.AudienceUserId == world.UserId);
        thread.Title.Should().Be(Prompt);
        var message = (await world.ReadMessages(thread.Id)).Should().ContainSingle().Which;
        message.Should().Match<Message>(sent =>
            sent.ScheduleId == schedule.Id && sent.AuthorUserId == world.UserId && sent.Role == MessageRole.User && sent.Position == 1);
        run.TriggerMessageId.Should().Be(message.Id);
        var stored = await ReadSchedule(world, schedule.Id);
        stored.Should().Match<BotSchedule>(after =>
            after.LastRunId == run.Id && after.LastOutcome == ScheduleOutcome.Started && after.LastRunAt == clock.Now && after.Enabled);
        stored.NextRunAt.Should().Be(monday.AddDays(1).AddHours(1), "the next time it is due is nine tomorrow");
        var listed = await ListSchedules(world, host);
        listed.Single().ToResponse().Should().Match<BotScheduleResponse>(response =>
            response.LastRunId == run.Id && response.LastOutcome == "queued" && response.LastRunAt == clock.Now);
    }

    [Fact]
    public async Task The_same_thread_is_the_owners_main_conversation_and_a_message_sent_while_it_is_busy_starts_a_scheduled_run_after()
    {
        var clock = new MovableClock(monday);
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.IdleHost(services => services.AddSingleton<TimeProvider>(clock));
        var schedule = await AddSchedule(world, host, world.UserId, "0 9 * * *", ScheduleThreadMode.SameThread);

        clock.Now = monday.AddHours(1);
        await SendDue(host);
        var main = await world.NewMainThread();
        var first = (await world.ReadRuns(main)).Should().ContainSingle().Which;
        clock.Now = monday.AddDays(1).AddHours(1);
        await SendDue(host);
        var waiting = await ReadSchedule(world, schedule.Id);
        var pending = await world.Read(db => db.Messages.AsNoTracking().Where(message => message.ThreadId == main).ToListAsync(RunWorld.Token));
        await world.Cancel(first.Id, host);
        var runs = await world.ReadRuns(main);

        first.Trigger.Should().Be(RunTrigger.Schedule);
        waiting.LastOutcome.Should().Be(ScheduleOutcome.WaitingForReply, "the first run was still under way");
        waiting.LastRunId.Should().BeNull();
        pending.Should().HaveCount(2);
        pending.Should().ContainSingle(message => message.Position == null && message.ScheduleId == schedule.Id && !message.Steers);
        runs.Should().HaveCount(2, "the waiting message starts the next run once the first has ended");
        runs[1].Should().Match<Run>(next => next.Trigger == RunTrigger.Schedule && next.PrincipalUserId == world.UserId && next.ThreadId == main);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_scheduled_run_acts_for_its_owner_recalls_their_notes_starts_clean_unless_a_note_taints_it_and_its_reply_is_new_to_them(
        bool taintedNote)
    {
        const string Note = "Reads the brief before standup.";
        var clock = new MovableClock(monday);
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("Nothing changed overnight.")]));
        await world.Read(async db =>
        {
            var bot = await db.Bots.SingleAsync(found => found.Id == world.BotId, RunWorld.Token);
            bot.MemorySettings = bot.MemorySettings with { Enabled = true };
            db.Memories.Add(new MemoryNote { BotId = world.BotId, AudienceUserId = world.UserId, Text = Note, WrittenByUserId = world.UserId, Tainted = taintedNote });
            return true;
        });
        await world.Grant("memory.list", GrantEffect.Allow);
        var scheduler = world.IdleHost(services => services.AddSingleton<TimeProvider>(clock));
        await AddSchedule(world, scheduler, world.UserId, "0 9 * * *", ScheduleThreadMode.NewThread);

        clock.Now = monday.AddHours(1);
        await SendDue(scheduler);
        var queued = await world.Read(db => db.Runs.AsNoTracking().SingleAsync(RunWorld.Token));
        world.StartHost();
        var run = await world.WaitForStatus(queued.Id, RunStatus.Completed, RunStatus.Failed);
        var thread = await world.ReadThread(run.ThreadId);
        var attention = (await world.Transact(services =>
            services.GetRequiredService<ThreadService>().FindThread(run.ThreadId, world.UserId, RunWorld.Token)))!.Attention!;

        queued.Tainted.Should().BeFalse("a scheduled run starts clean");
        run.Status.Should().Be(RunStatus.Completed);
        run.PrincipalUserId.Should().Be(world.UserId);
        run.Tainted.Should().Be(taintedNote, "a note a tainted run wrote taints the run given it, as it would any run");
        thread.AudienceUserId.Should().Be(world.UserId, "the conversation a schedule makes is private to its owner");
        world.Model.Requests.Should().ContainSingle().Which.Messages
            .SelectMany(message => message.Contents.OfType<ModelText>())
            .Should().Contain(text => text.Text.Contains(Note, StringComparison.Ordinal), "the run recalls its owner's notes");
        attention.HasNew.Should().BeTrue("the owner has not seen the reply the schedule's run wrote");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task A_server_down_across_several_due_times_sends_the_schedule_once_and_counts_the_next_time_from_now()
    {
        var clock = new MovableClock(monday);
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.IdleHost(services => services.AddSingleton<TimeProvider>(clock));
        var schedule = await AddSchedule(world, host, world.UserId, "0 * * * *", ScheduleThreadMode.NewThread);

        clock.Now = monday.AddHours(5).AddMinutes(30);
        var afterOutage = await SendDue(host);
        var again = await SendDue(host);

        afterOutage.Should().Be(1, "five missed times are sent once, not five times");
        again.Should().Be(0);
        (await world.Read(db => db.Runs.CountAsync(RunWorld.Token))).Should().Be(1);
        (await ReadSchedule(world, schedule.Id)).NextRunAt.Should().Be(monday.AddHours(6));
    }

    [Theory]
    [InlineData("paused", ScheduleOutcome.BotPaused)]
    [InlineData("removed", ScheduleOutcome.BotRemoved)]
    public async Task A_paused_or_removed_bot_sends_nothing_its_schedule_stays_on_and_moves_to_its_next_time_and_says_why(
        string status,
        ScheduleOutcome outcome)
    {
        var clock = new MovableClock(monday);
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.IdleHost(services => services.AddSingleton<TimeProvider>(clock));
        var schedule = await AddSchedule(world, host, world.UserId, "0 9 * * *", ScheduleThreadMode.NewThread);
        await world.Read(db => db.Database.ExecuteSqlAsync($"UPDATE bots SET status = {status} WHERE id = {world.BotId}", RunWorld.Token));

        clock.Now = monday.AddHours(1);
        var handled = await SendDue(host);
        var stored = await ReadSchedule(world, schedule.Id);
        var response = (await world.Read(db => db.BotSchedules.AsNoTracking().SingleAsync(found => found.Id == schedule.Id, RunWorld.Token))).ToResponse();

        handled.Should().Be(1);
        (await world.Read(db => db.Runs.AnyAsync(RunWorld.Token))).Should().BeFalse();
        (await world.Read(db => db.Messages.AnyAsync(RunWorld.Token))).Should().BeFalse();
        stored.Should().Match<BotSchedule>(after =>
            after.Enabled && after.LastOutcome == outcome && after.LastRunId == null && after.LastRunAt == clock.Now);
        stored.NextRunAt.Should().Be(monday.AddDays(1).AddHours(1), "next_run_at still moves on");
        response.LastOutcome.Should().Be(status is "paused" ? "bot_paused" : "bot_removed");
    }

    [Fact]
    public async Task An_owner_who_is_no_longer_a_member_sends_nothing_and_the_schedule_is_turned_off_saying_why()
    {
        var clock = new MovableClock(monday);
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.IdleHost(services => services.AddSingleton<TimeProvider>(clock));
        var member = await world.AddMember(MembershipRole.Admin);
        var schedule = await AddSchedule(world, host, member, "0 9 * * *", ScheduleThreadMode.NewThread);
        await world.Read(db => db.Memberships.Where(membership => membership.UserId == member).ExecuteDeleteAsync(RunWorld.Token));

        clock.Now = monday.AddHours(1);
        var handled = await SendDue(host);
        var stored = await ReadSchedule(world, schedule.Id);
        clock.Now = monday.AddDays(3);
        var later = await SendDue(host);

        handled.Should().Be(1);
        later.Should().Be(0, "a schedule that is off is never due");
        (await world.Read(db => db.Runs.AnyAsync(RunWorld.Token))).Should().BeFalse();
        (await world.Read(db => db.Threads.AnyAsync(RunWorld.Token))).Should().BeFalse();
        stored.Should().Match<BotSchedule>(after =>
            !after.Enabled && after.NextRunAt == null && after.LastOutcome == ScheduleOutcome.OwnerLeft && after.LastRunId == null);
        stored.ToResponse().LastOutcome.Should().Be("owner_left");
    }

    [Fact]
    public async Task Two_servers_looking_at_the_same_moment_send_each_due_schedule_once()
    {
        const int Schedules = 15;
        var clock = new MovableClock(monday);
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var first = world.IdleHost(services => services.AddSingleton<TimeProvider>(clock));
        var second = world.IdleHost(services => services.AddSingleton<TimeProvider>(clock));
        List<BotSchedule> made = [];

        for (var index = 0; index < Schedules; index++)
        {
            made.Add(await AddSchedule(world, first, world.UserId, "0 9 * * *", ScheduleThreadMode.NewThread));
        }

        clock.Now = monday.AddHours(1);
        var handled = await Task.WhenAll(Task.Run(() => SendDue(first)), Task.Run(() => SendDue(second)));

        handled.Sum().Should().Be(Schedules);
        var runs = await world.Read(db => db.Runs.AsNoTracking().ToListAsync(RunWorld.Token));
        runs.Should().HaveCount(Schedules);
        var sentBy = await world.Read(db => db.Messages.AsNoTracking().Select(message => message.ScheduleId).ToListAsync(RunWorld.Token));
        sentBy.Should().BeEquivalentTo(made.Select(schedule => (Guid?)schedule.Id), "each schedule sent one message, once");
    }

    [Fact]
    public async Task A_schedule_changed_after_it_was_claimed_is_left_alone()
    {
        var clock = new MovableClock(monday);
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.IdleHost(services => services.AddSingleton<TimeProvider>(clock));
        var schedule = await AddSchedule(world, host, world.UserId, "0 9 * * *", ScheduleThreadMode.NewThread);
        clock.Now = monday.AddHours(1);
        var claimUntil = monday.AddHours(1).AddMinutes(5);
        var scopes = host.Services.GetRequiredService<RunScopes>();
        await scopes.WithoutTenant(
            services => services.GetRequiredService<BotScheduleRepository>().ClaimDue(clock.Now, claimUntil, 10, RunWorld.Token),
            RunWorld.Token);

        await AsPerson(world, host, world.UserId, services => services.GetRequiredService<ScheduleService>()
            .Update(world.BotId, schedule.Id, new ScheduleEdit("30 9 * * *", "UTC", Prompt, ScheduleThreadMode.NewThread, true), world.UserId, RunWorld.Token));
        ScheduleFire? fire = null;
        await scopes.For(world.SpaceId, world.UserId).InScope(async services =>
            fire = await services.GetRequiredService<ScheduleFiring>().Fire(schedule.Id, claimUntil, RunWorld.Token));

        fire.Should().BeNull("the person changed the schedule after it was claimed, so the claim no longer holds");
        (await world.Read(db => db.Runs.AnyAsync(RunWorld.Token))).Should().BeFalse();
        (await ReadSchedule(world, schedule.Id)).NextRunAt.Should().Be(monday.AddHours(1).AddMinutes(30));
    }

    #endregion

    #region Policy

    [Fact]
    public async Task Saving_a_schedule_that_repeats_more_often_than_the_deployment_allows_says_so_in_a_sentence()
    {
        var clock = new MovableClock(monday);
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.IdleHost(services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IOptions<DeploymentPolicyOptions>>(Options.Create(new DeploymentPolicyOptions { Rules = DeploymentPolicy.Hosted }));
        });

        var tooOften = await Create(world, host, "*/5 * * * *");
        var twiceAtNine = await Create(world, host, "0,10 9 * * *");
        var quarterly = await Create(world, host, "*/15 * * * *");

        tooOften.Problem.Should().Be($"Schedules on this server run at most once every {DeploymentPolicy.HostedMinScheduleIntervalMinutes} minutes. Choose a time that repeats less often.");
        twiceAtNine.Problem.Should().NotBeNull("two times ten minutes apart are closer than the hosted floor, however rarely they come");
        quarterly.Problem.Should().BeNull();
        quarterly.Schedule.Should().NotBeNull();
    }

    [Fact]
    public async Task Adding_a_schedule_beyond_the_spaces_cap_says_so_in_a_sentence_and_the_cap_counts_every_bot()
    {
        var clock = new MovableClock(monday);
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.IdleHost(services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IOptions<DeploymentPolicyOptions>>(Options.Create(new DeploymentPolicyOptions
            {
                Rules = DeploymentPolicy.SelfHosted with { MaxSchedulesPerSpace = 2 },
            }));
        });
        await AddSchedule(world, host, world.UserId, "0 9 * * *", ScheduleThreadMode.NewThread);
        await AddSchedule(world, host, world.UserId, "0 10 * * *", ScheduleThreadMode.NewThread);

        var third = await Create(world, host, "0 11 * * *");

        third.Schedule.Should().BeNull();
        third.Problem.Should().Be("A space on this server holds at most 2 schedules. Remove one first.");
    }

    #endregion

    #region Private Methods

    private static Task<int> SendDue(EngineHost host) =>
        host.Services.GetRequiredService<ScheduleRunner>().SendDue(RunWorld.Token);

    private static async Task<BotSchedule> AddSchedule(RunWorld world, EngineHost host, Guid ownerUserId, string cron, ScheduleThreadMode mode)
    {
        var change = await Create(world, host, cron, mode, ownerUserId);
        change.Problem.Should().BeNull();
        return change.Schedule!;
    }

    private static Task<ScheduleChange> Create(
        RunWorld world,
        EngineHost host,
        string cron,
        ScheduleThreadMode mode = ScheduleThreadMode.NewThread,
        Guid? ownerUserId = null) =>
        AsPerson(world, host, ownerUserId ?? world.UserId, services => services.GetRequiredService<ScheduleService>()
            .Create(world.BotId, new ScheduleEdit(cron, "UTC", Prompt, mode, true), ownerUserId ?? world.UserId, RunWorld.Token));

    private static Task<List<ScheduleView>> ListSchedules(RunWorld world, EngineHost host) =>
        AsPerson(world, host, world.UserId, async services =>
            (await services.GetRequiredService<ScheduleService>().ListSchedules(world.BotId, RunWorld.Token))!);

    private static Task<BotSchedule> ReadSchedule(RunWorld world, Guid scheduleId) =>
        world.Read(db => db.BotSchedules.AsNoTracking().SingleAsync(schedule => schedule.Id == scheduleId, RunWorld.Token));

    /// <summary>A request's tenant scope on the given host, as the person, around one call.</summary>
    private static async Task<T> AsPerson<T>(RunWorld world, EngineHost host, Guid userId, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, userId);
        await using var transaction = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(RunWorld.Token);
        var result = await work(scope.ServiceProvider);
        await transaction.Commit(RunWorld.Token);
        return result;
    }

    #endregion

    #region Nested Types

    /// <summary>A clock that stands still until the test moves it.</summary>
    private sealed class MovableClock(DateTimeOffset start) : TimeProvider
    {
        #region Properties

        public DateTimeOffset Now { get; set; } = start;

        #endregion

        #region Public Methods

        public override DateTimeOffset GetUtcNow() => Now;

        #endregion
    }

    #endregion
}
