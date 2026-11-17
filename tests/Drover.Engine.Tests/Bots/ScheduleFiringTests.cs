namespace Drover.Engine.Tests.Bots;

/// <summary>
/// Schedules coming due, over PostgreSQL and the real engine: a due schedule starts one run, triggered by the schedule,
/// as its owner, in the conversation its thread mode names; and each edge case of docs/architecture/03-run-lifecycle.md
/// "Schedules". The claim checks the caller's clock against the database's, so the hosts' clock stands still at the
/// real time and a schedule is made due by moving its next time into the past, as if the server had waited for it.
/// The hosts execute no runs, so each run stays as it was queued.
/// </summary>
public sealed class ScheduleFiringTests(PostgresServer postgres)
{
    #region Private Fields

    private const string Prompt = "Brief me on what changed overnight.";


    #endregion

    #region Firing

    [Fact]
    public async Task A_due_schedule_starts_exactly_one_run_triggered_by_the_schedule_as_its_owner_in_a_new_conversation()
    {
        var clock = new MovableClock(Now());
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.IdleHost(services => services.AddSingleton<TimeProvider>(clock));
        var schedule = await AddSchedule(world, host, world.UserId, "0 9 * * *", ScheduleThreadMode.NewThread);

        var early = await SendDue(host);
        await MakeDue(world, clock, TimeSpan.FromMinutes(1));
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
        stored.NextRunAt.Should().Be(NextAt(clock.Now, 9, 0), "the next time it is due is the next nine o'clock");
        var listed = await ListSchedules(world, host);
        listed.Single().ToResponse().Should().Match<BotScheduleResponse>(response =>
            response.LastRunId == run.Id && response.LastOutcome == "queued" && response.LastRunAt == clock.Now);
    }

    [Fact]
    public async Task The_same_thread_is_the_owners_main_conversation_and_a_message_sent_while_it_is_busy_starts_a_scheduled_run_after()
    {
        var clock = new MovableClock(Now());
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.IdleHost(services => services.AddSingleton<TimeProvider>(clock));
        var schedule = await AddSchedule(world, host, world.UserId, "0 9 * * *", ScheduleThreadMode.SameThread);

        await MakeDue(world, clock, TimeSpan.FromMinutes(1));
        await SendDue(host);
        var main = await world.NewMainThread();
        var first = (await world.ReadRuns(main)).Should().ContainSingle().Which;
        await MakeDue(world, clock, TimeSpan.FromMinutes(1));
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
        var clock = new MovableClock(Now());
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

        await MakeDue(world, clock, TimeSpan.FromMinutes(1));
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
        var clock = new MovableClock(Now());
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.IdleHost(services => services.AddSingleton<TimeProvider>(clock));
        var schedule = await AddSchedule(world, host, world.UserId, "0 * * * *", ScheduleThreadMode.NewThread);

        await MakeDue(world, clock, TimeSpan.FromHours(5.5));
        var afterOutage = await SendDue(host);
        var again = await SendDue(host);

        afterOutage.Should().Be(1, "five missed times are sent once, not five times");
        again.Should().Be(0);
        (await world.Read(db => db.Runs.CountAsync(RunWorld.Token))).Should().Be(1);
        (await ReadSchedule(world, schedule.Id)).NextRunAt.Should().Be(NextAt(clock.Now, null, 0), "the next top of the hour after now");
    }

    [Theory]
    [InlineData("paused", ScheduleOutcome.BotPaused)]
    [InlineData("removed", ScheduleOutcome.BotRemoved)]
    public async Task A_paused_bot_sends_nothing_and_its_schedule_moves_on_and_a_removed_bots_is_turned_off_each_saying_why(
        string status,
        ScheduleOutcome outcome)
    {
        var clock = new MovableClock(Now());
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.IdleHost(services => services.AddSingleton<TimeProvider>(clock));
        var schedule = await AddSchedule(world, host, world.UserId, "0 9 * * *", ScheduleThreadMode.NewThread);
        await world.Read(db => db.Database.ExecuteSqlAsync($"UPDATE bots SET status = {status} WHERE id = {world.BotId}", RunWorld.Token));

        await MakeDue(world, clock, TimeSpan.FromMinutes(1));
        var handled = await SendDue(host);
        var stored = await ReadSchedule(world, schedule.Id);
        var later = await SendDue(host);
        var response = (await world.Read(db => db.BotSchedules.AsNoTracking().SingleAsync(found => found.Id == schedule.Id, RunWorld.Token))).ToResponse();

        handled.Should().Be(1);
        (await world.Read(db => db.Runs.AnyAsync(RunWorld.Token))).Should().BeFalse();
        (await world.Read(db => db.Messages.AnyAsync(RunWorld.Token))).Should().BeFalse();
        later.Should().Be(0);
        stored.Should().Match<BotSchedule>(after => after.LastOutcome == outcome && after.LastRunId == null && after.LastRunAt == clock.Now);

        if (status is "paused")
        {
            stored.Enabled.Should().BeTrue("a paused bot may be resumed, and its schedule with it");
            stored.NextRunAt.Should().Be(NextAt(clock.Now, 9, 0), "next_run_at still moves on");
        }
        else
        {
            stored.Enabled.Should().BeFalse("a removed bot can never answer, so its schedule is not claimed again");
            stored.NextRunAt.Should().BeNull();
        }

        response.LastOutcome.Should().Be(status is "paused" ? "bot_paused" : "bot_removed");
    }

    [Fact]
    public async Task An_owner_who_is_no_longer_a_member_sends_nothing_and_the_schedule_is_turned_off_saying_why()
    {
        var clock = new MovableClock(Now());
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.IdleHost(services => services.AddSingleton<TimeProvider>(clock));
        var member = await world.AddMember(MembershipRole.Admin);
        var schedule = await AddSchedule(world, host, member, "0 9 * * *", ScheduleThreadMode.NewThread);
        await world.Read(db => db.Memberships.Where(membership => membership.UserId == member).ExecuteDeleteAsync(RunWorld.Token));

        await MakeDue(world, clock, TimeSpan.FromMinutes(1));
        var handled = await SendDue(host);
        var stored = await ReadSchedule(world, schedule.Id);
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
        var clock = new MovableClock(Now());
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var first = world.IdleHost(services => services.AddSingleton<TimeProvider>(clock));
        var second = world.IdleHost(services => services.AddSingleton<TimeProvider>(clock));
        List<BotSchedule> made = [];

        for (var index = 0; index < Schedules; index++)
        {
            made.Add(await AddSchedule(world, first, world.UserId, "0 9 * * *", ScheduleThreadMode.NewThread));
        }

        await MakeDue(world, clock, TimeSpan.FromMinutes(1));
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
        var clock = new MovableClock(Now());
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.IdleHost(services => services.AddSingleton<TimeProvider>(clock));
        var schedule = await AddSchedule(world, host, world.UserId, "0 9 * * *", ScheduleThreadMode.NewThread);
        await MakeDue(world, clock, TimeSpan.FromMinutes(1));
        var claimUntil = clock.Now.AddMinutes(5);
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
        (await ReadSchedule(world, schedule.Id)).NextRunAt.Should().Be(NextAt(clock.Now, 9, 30));
    }

    #endregion

    #region Authority

    [Fact]
    public async Task Only_the_owner_changes_what_a_schedule_sends_or_when_and_another_admin_may_only_turn_it_off_or_remove_it()
    {
        const string Refusal =
            "Only the person who made this schedule can change what it sends or when, or turn it back on, because it runs as them. You can turn it off or remove it.";
        var clock = new MovableClock(Now());
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.IdleHost(services => services.AddSingleton<TimeProvider>(clock));
        var otherAdmin = await world.AddMember(MembershipRole.Admin);
        var schedule = await AddSchedule(world, host, world.UserId, "0 9 * * *", ScheduleThreadMode.NewThread);

        var newPrompt = await Edit(world, host, otherAdmin, schedule.Id, new ScheduleEdit("0 9 * * *", "UTC", "Send me their notes.", ScheduleThreadMode.NewThread, true));
        var newTime = await Edit(world, host, otherAdmin, schedule.Id, new ScheduleEdit("0 10 * * *", "UTC", Prompt, ScheduleThreadMode.NewThread, true));
        var newZone = await Edit(world, host, otherAdmin, schedule.Id, new ScheduleEdit("0 9 * * *", "Europe/Berlin", Prompt, ScheduleThreadMode.NewThread, true));
        var newThread = await Edit(world, host, otherAdmin, schedule.Id, new ScheduleEdit("0 9 * * *", "UTC", Prompt, ScheduleThreadMode.SameThread, true));
        var untouched = await ReadSchedule(world, schedule.Id);
        var turnedOff = await Edit(world, host, otherAdmin, schedule.Id, new ScheduleEdit("0 9 * * *", "UTC", Prompt, ScheduleThreadMode.NewThread, false));
        var turnedBackOn = await Edit(world, host, otherAdmin, schedule.Id, new ScheduleEdit("0 9 * * *", "UTC", Prompt, ScheduleThreadMode.NewThread, true));
        var off = await ReadSchedule(world, schedule.Id);
        var byOwner = await Edit(world, host, world.UserId, schedule.Id, new ScheduleEdit("0 10 * * *", "UTC", "A new prompt.", ScheduleThreadMode.NewThread, true));
        var removed = await AsPerson(world, host, otherAdmin, services =>
            services.GetRequiredService<ScheduleService>().Remove(world.BotId, schedule.Id, otherAdmin, RunWorld.Token));

        new[] { newPrompt, newTime, newZone, newThread }.Should().OnlyContain(change => change.Schedule == null && change.Problem == Refusal);
        untouched.Should().Match<BotSchedule>(found =>
            found.Prompt == Prompt && found.Cron == "0 9 * * *" && found.TimeZone == "UTC" && found.ThreadMode == ScheduleThreadMode.NewThread && found.Enabled);
        turnedOff.Problem.Should().BeNull("another admin may turn a schedule off");
        off.Should().Match<BotSchedule>(found => !found.Enabled && found.NextRunAt == null && found.Prompt == Prompt);
        turnedBackOn.Problem.Should().Be(Refusal, "turning it back on would run it as its owner again, which is the owner's to decide");
        byOwner.Problem.Should().BeNull();
        byOwner.Schedule!.Should().Match<BotSchedule>(found => found.Enabled && found.Prompt == "A new prompt." && found.NextRunAt == NextAt(clock.Now, 10, 0));
        removed.Should().BeTrue("another admin may remove a schedule");
        (await world.Read(db => db.BotSchedules.AnyAsync(RunWorld.Token))).Should().BeFalse();
    }

    #endregion

    #region Policy

    [Fact]
    public async Task Saving_a_schedule_that_repeats_more_often_than_the_deployment_allows_says_so_in_a_sentence()
    {
        var clock = new MovableClock(Now());
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.IdleHost(services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IOptions<DeploymentPolicyOptions>>(Options.Create(new DeploymentPolicyOptions { Rules = DeploymentPolicy.Hosted }));
        });

        var tooOften = await Create(world, host, "*/5 * * * *");
        var twiceAtNine = await Create(world, host, "0,10 9 * * *");
        var quarterly = await Create(world, host, "*/15 * * * *");
        var checkedFirst = await AsPerson(world, host, world.UserId, services =>
            Task.FromResult(services.GetRequiredService<ScheduleService>().Check("*/5 * * * *", "UTC")));

        checkedFirst.Should().Match<ScheduleCheck>(check => check.Problem == tooOften.Problem && check.Next.Count == 0,
            "checking a time before saving it refuses it with the sentence a save would");
        tooOften.Problem.Should().Be($"Schedules on this server run at most once every {DeploymentPolicy.HostedMinScheduleIntervalMinutes} minutes. Choose a time that repeats less often.");
        twiceAtNine.Problem.Should().NotBeNull("two times ten minutes apart are closer than the hosted floor, however rarely they come");
        quarterly.Problem.Should().BeNull();
        quarterly.Schedule.Should().NotBeNull();
    }

    [Fact]
    public async Task Adding_a_schedule_beyond_the_spaces_cap_says_so_in_a_sentence_and_the_cap_counts_every_bot()
    {
        var clock = new MovableClock(Now());
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

    #region Time Zones

    [Theory]
    [InlineData("0 8 * * *", "Europe/Berlin", "2026-03-28T12:00:00Z", "2026-03-29T06:00:00Z")]
    [InlineData("0 8 * * *", "Europe/Berlin", "2026-10-24T12:00:00Z", "2026-10-25T07:00:00Z")]
    [InlineData("30 2 * * *", "America/New_York", "2026-03-07T12:00:00Z", "2026-03-08T07:00:00Z")]
    [InlineData("30 1 * * *", "America/New_York", "2026-10-31T12:00:00Z", "2026-11-01T05:30:00Z")]
    [InlineData("0 9 * * 1-5", "Asia/Tokyo", "2026-01-02T12:00:00Z", "2026-01-05T00:00:00Z")]
    public void A_schedule_runs_at_its_wall_clock_time_in_its_zone_across_daylight_saving_changes(string cron, string zone, string after, string expected)
    {
        var schedule = new BotSchedule { Cron = cron, TimeZone = zone, Prompt = Prompt };

        ScheduleService.NextOccurrence(schedule, DateTimeOffset.Parse(after, CultureInfo.InvariantCulture))
            .Should().Be(DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture));
    }

    #endregion

    #region Running Once

    [Fact]
    public async Task Running_once_now_starts_one_run_for_the_person_who_asked_in_their_own_conversation_and_leaves_the_schedule_as_it_was()
    {
        var clock = new MovableClock(Now());
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.IdleHost(services => services.AddSingleton<TimeProvider>(clock));
        var otherAdmin = await world.AddMember(MembershipRole.Admin);
        var schedule = await AddSchedule(world, host, world.UserId, "0 9 * * *", ScheduleThreadMode.NewThread);
        var sameThread = await AddSchedule(world, host, world.UserId, "0 18 * * *", ScheduleThreadMode.SameThread);
        var before = await ReadSchedule(world, schedule.Id);

        var byOtherAdmin = await RunOnce(world, host, otherAdmin, schedule.Id);
        var byOwner = await RunOnce(world, host, world.UserId, schedule.Id);
        var intoMain = await RunOnce(world, host, otherAdmin, sameThread.Id);
        var due = await SendDue(host);

        new[] { byOtherAdmin, byOwner, intoMain }.Should().OnlyContain(trial => trial.Problem == null && trial.ThreadId != null && trial.RunId != null);
        due.Should().Be(0, "running a schedule once does not make it due");
        var runs = await world.Read(db => db.Runs.AsNoTracking().ToListAsync(RunWorld.Token));
        runs.Should().HaveCount(3, "each press starts exactly one run");
        var asked = runs.Single(run => run.Id == byOtherAdmin.RunId);
        asked.Should().Match<Run>(run => run.PrincipalUserId == otherAdmin && run.Trigger == RunTrigger.Schedule && run.ThreadId == byOtherAdmin.ThreadId);
        (await ReadThreadAs(world, otherAdmin, byOtherAdmin.ThreadId!.Value)).Should().Match<ChatThread>(thread =>
            thread.OwnerUserId == otherAdmin && thread.AudienceUserId == otherAdmin && thread.Origin == ThreadOrigin.Schedule && !thread.IsMain);
        (await ReadMessagesAs(world, otherAdmin, byOtherAdmin.ThreadId.Value)).Should().ContainSingle().Which.Should().Match<Message>(message =>
            message.AuthorUserId == otherAdmin && message.ScheduleId == schedule.Id);
        runs.Single(run => run.Id == byOwner.RunId).PrincipalUserId.Should().Be(world.UserId);
        byOwner.ThreadId.Should().NotBe(byOtherAdmin.ThreadId!.Value);
        (await ReadThreadAs(world, otherAdmin, intoMain.ThreadId!.Value)).Should().Match<ChatThread>(thread =>
            thread.IsMain && thread.OwnerUserId == otherAdmin && thread.AudienceUserId == otherAdmin,
            "the same thread is the main conversation of the person who asked, never the owner's");
        var ownerSees = await AsPerson(world, host, world.UserId, services =>
            services.GetRequiredService<ThreadService>().FindThread(byOtherAdmin.ThreadId.Value, world.UserId, RunWorld.Token));
        ownerSees.Should().BeNull("the conversation is private to the admin who ran it");
        (await ReadSchedule(world, schedule.Id)).Should().Match<BotSchedule>(after =>
            after.NextRunAt == before.NextRunAt && after.LastRunId == null && after.LastRunAt == null && after.LastOutcome == null && after.Enabled);
    }

    [Fact]
    public async Task Running_once_on_a_paused_bot_says_why_and_an_unknown_schedule_is_not_found()
    {
        var clock = new MovableClock(Now());
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.IdleHost(services => services.AddSingleton<TimeProvider>(clock));
        var schedule = await AddSchedule(world, host, world.UserId, "0 9 * * *", ScheduleThreadMode.NewThread);
        await world.Read(db => db.Bots.Where(bot => bot.Id == world.BotId)
            .ExecuteUpdateAsync(set => set.SetProperty(bot => bot.Status, BotStatus.Paused), RunWorld.Token));

        var paused = await RunOnce(world, host, world.UserId, schedule.Id);
        var unknown = await RunOnce(world, host, world.UserId, Guid.CreateVersion7());

        paused.Problem.Should().EndWith("is paused, so its routines do not run. Resume it first.");
        paused.ThreadId.Should().BeNull();
        unknown.Should().Be(ScheduleTrial.NotFound);
        (await world.Read(db => db.Runs.AnyAsync(RunWorld.Token))).Should().BeFalse();
    }

    #endregion

    #region Last Outcome

    [Fact]
    public async Task The_last_run_is_told_in_full_only_to_someone_who_can_see_its_conversation()
    {
        const string Reason = "The model connection could not be reached.";
        var clock = new MovableClock(Now());
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.IdleHost(services => services.AddSingleton<TimeProvider>(clock));
        var otherAdmin = await world.AddMember(MembershipRole.Admin);
        var schedule = await AddSchedule(world, host, world.UserId, "0 9 * * *", ScheduleThreadMode.NewThread);
        await MakeDue(world, clock, TimeSpan.FromMinutes(1));
        await SendDue(host);
        var run = await world.Read(db => db.Runs.AsNoTracking().SingleAsync(RunWorld.Token));
        await world.Read(db => db.Runs.Where(found => found.Id == run.Id).ExecuteUpdateAsync(
            set => set.SetProperty(found => found.Status, RunStatus.Failed).SetProperty(found => found.Error, new RunError("model_unreachable", Reason)),
            RunWorld.Token));

        var toOwner = (await ListSchedules(world, host, world.UserId)).Single().ToResponse();
        var toOtherAdmin = (await ListSchedules(world, host, otherAdmin)).Single().ToResponse();
        var turnedOffByOtherAdmin = await Edit(world, host, otherAdmin, schedule.Id, new ScheduleEdit("0 9 * * *", "UTC", Prompt, ScheduleThreadMode.NewThread, false));
        var changedByOwner = await Edit(world, host, world.UserId, schedule.Id, new ScheduleEdit("0 9 * * *", "UTC", Prompt, ScheduleThreadMode.NewThread, true));

        toOwner.Should().Match<BotScheduleResponse>(response =>
            response.LastOutcome == "failed" && response.LastRunId == run.Id && response.LastThreadId == run.ThreadId && response.LastReason == Reason);
        toOtherAdmin.Should().Match<BotScheduleResponse>(response =>
            response.LastOutcome == "started" && response.LastRunId == null && response.LastThreadId == null && response.LastReason == null
            && response.LastRunAt == toOwner.LastRunAt && response.LastRunAt != null,
            "another admin learns that it ran, and nothing from the owner's private conversation");
        turnedOffByOtherAdmin.ToResponse().Should().Match<BotScheduleResponse>(response =>
            response.LastOutcome == "started" && response.LastRunId == null && response.LastThreadId == null && response.LastReason == null);
        changedByOwner.ToResponse().Should().Match<BotScheduleResponse>(response =>
            response.LastOutcome == "failed" && response.LastThreadId == run.ThreadId && response.LastReason == Reason);
    }

    #endregion

    #region Private Methods

    /// <summary>The real time to the second, which PostgreSQL stores exactly.</summary>
    private static DateTimeOffset Now() =>
        new(DateTimeOffset.UtcNow.UtcTicks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, TimeSpan.Zero);

    /// <summary>The first moment after <paramref name="after"/> at the minute given, in the hour given or in any hour, in UTC.</summary>
    private static DateTimeOffset NextAt(DateTimeOffset after, int? hour, int minute)
    {
        var candidate = new DateTimeOffset(after.Year, after.Month, after.Day, hour ?? after.Hour, minute, 0, TimeSpan.Zero);

        while (candidate <= after)
        {
            candidate = hour is null ? candidate.AddHours(1) : candidate.AddDays(1);
        }

        return candidate;
    }

    /// <summary>Every enabled schedule of the world was due a while ago: the server waited for it, or was down.</summary>
    private static Task<int> MakeDue(RunWorld world, MovableClock clock, TimeSpan ago) =>
        world.Read(db => db.BotSchedules
            .Where(schedule => schedule.Enabled)
            .ExecuteUpdateAsync(set => set.SetProperty(schedule => schedule.NextRunAt, clock.Now - ago), RunWorld.Token));

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

    private static Task<ScheduleChange> Edit(RunWorld world, EngineHost host, Guid actorUserId, Guid scheduleId, ScheduleEdit edit) =>
        AsPerson(world, host, actorUserId, services =>
            services.GetRequiredService<ScheduleService>().Update(world.BotId, scheduleId, edit, actorUserId, RunWorld.Token));

    private static Task<List<ScheduleView>> ListSchedules(RunWorld world, EngineHost host, Guid? userId = null) =>
        AsPerson(world, host, userId ?? world.UserId, async services =>
            (await services.GetRequiredService<ScheduleService>().ListSchedules(world.BotId, RunWorld.Token))!);

    private static Task<ChatThread> ReadThreadAs(RunWorld world, Guid userId, Guid threadId) =>
        world.TransactAs(world.SpaceId, userId, services => services.GetRequiredService<DroverDbContext>().Threads.AsNoTracking()
            .SingleAsync(thread => thread.Id == threadId, RunWorld.Token));

    private static Task<List<Message>> ReadMessagesAs(RunWorld world, Guid userId, Guid threadId) =>
        world.TransactAs(world.SpaceId, userId, services => services.GetRequiredService<DroverDbContext>().Messages.AsNoTracking()
            .Where(message => message.ThreadId == threadId).ToListAsync(RunWorld.Token));

    /// <summary>"Run once now" as a request makes it: the person set, no scope open, since the firing opens its own.</summary>
    private static async Task<ScheduleTrial> RunOnce(RunWorld world, EngineHost host, Guid userId, Guid scheduleId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, userId);
        return await scope.ServiceProvider.GetRequiredService<ScheduleFiring>().RunOnce(world.BotId, scheduleId, userId, RunWorld.Token);
    }

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
