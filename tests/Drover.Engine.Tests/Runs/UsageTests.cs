namespace Drover.Engine.Tests.Runs;

/// <summary>
/// What bots and runs used, read through <see cref="UsageService"/> on PostgreSQL. Runs and their model calls are
/// seeded as rows, so each figure can be checked against a direct sum of what is stored. Issue 163.
/// </summary>
public sealed class UsageTests(PostgresServer postgres)
{
    #region Private Fields

    private static readonly TimeZoneInfo utc = TimeZoneInfo.Utc;

    private static readonly TimeZoneInfo berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    #endregion

    #region Sums

    [Fact]
    public async Task Each_bots_own_figure_and_its_figure_with_the_bots_beneath_it_equal_the_sums_of_the_stored_records()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var helperId = await world.NewBotBeneath(world.BotId, "Planner");
        var finderId = await world.NewBotBeneath(helperId, "Finder");
        var siblingId = await world.NewBotBeneath(world.BotId, "Writer");
        var idleId = await world.NewBotBeneath(world.BotId, "Idle");
        var threadId = await world.NewThread();

        var root = await SeedRun(world, threadId, world.BotId, world.UserId);
        var child = await SeedRun(world, threadId, helperId, world.UserId, root);
        var grandchild = await SeedRun(world, threadId, finderId, world.UserId, child);
        var other = await SeedRun(world, threadId, siblingId, world.UserId, root);
        await SeedCall(world, root, 0.40m, 1_000, 200);
        await SeedCall(world, root, 0.10m, 300, 30);
        await SeedCall(world, child, 1.25m, 2_000, 400);
        await SeedCall(world, grandchild, 0.05m, 50, 5);
        await SeedCall(world, grandchild, 0.70m, 700, 70);
        await SeedCall(world, other, 0.33m, 330, 33);

        var crew = await world.Transact(services => services.GetRequiredService<UsageService>()
            .GetCrewUsage(UsagePeriods.Week, utc, world.UserId, isSpaceAdmin: true, RunWorld.Token));

        crew.Currency.Should().Be("USD");
        var lead = crew.Crew.Should().ContainSingle().Subject;
        var planner = lead.Children.Should().ContainSingle(node => node.BotId == helperId).Subject;
        var finder = planner.Children.Should().ContainSingle().Subject;
        var writer = lead.Children.Should().ContainSingle(node => node.BotId == siblingId).Subject;
        lead.Children.Should().NotContain(node => node.BotId == idleId, "a bot that used nothing in the period has no figure to show");

        lead.Own.Should().Be(await StoredSum(world, [world.BotId]));
        lead.WithBeneath.Should().Be(await StoredSum(world, [world.BotId, helperId, finderId, siblingId]));
        planner.Own.Should().Be(await StoredSum(world, [helperId]));
        planner.WithBeneath.Should().Be(await StoredSum(world, [helperId, finderId]));
        finder.Own.Should().Be(await StoredSum(world, [finderId]));
        finder.WithBeneath.Should().BeNull("Finder has no bot beneath it");
        writer.Own.Should().Be(await StoredSum(world, [siblingId]));
        lead.WithBeneath!.Cost.Should().Be(2.83m);
    }

    [Fact]
    public async Task A_runs_figure_and_its_trees_figure_equal_the_sums_of_the_stored_records()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var helperId = await world.NewBotBeneath(world.BotId, "Planner");
        var finderId = await world.NewBotBeneath(helperId, "Finder");
        var threadId = await world.NewThread();
        var root = await SeedRun(world, threadId, world.BotId, world.UserId);
        var child = await SeedRun(world, threadId, helperId, world.UserId, root);
        var grandchild = await SeedRun(world, threadId, finderId, world.UserId, child);
        await SeedCall(world, root, 0.40m, 1_000, 200);
        await SeedCall(world, child, 1.25m, 2_000, 400);
        await SeedCall(world, grandchild, 0.70m, 700, 70);
        var alone = await SeedRun(world, threadId, world.BotId, world.UserId);
        await SeedCall(world, alone, 0.02m, 20, 2);

        var tree = await RunUsage(world, root.Id, world.UserId, isSpaceAdmin: false);
        var middle = await RunUsage(world, child.Id, world.UserId, isSpaceAdmin: false);
        var single = await RunUsage(world, alone.Id, world.UserId, isSpaceAdmin: false);

        tree!.Own.Should().Be(await StoredRunSum(world, [root.Id]));
        tree.Tree.Should().Be(await StoredRunSum(world, [root.Id, child.Id, grandchild.Id]));
        middle!.Tree.Should().Be(await StoredRunSum(world, [child.Id, grandchild.Id]), "a run's tree is the runs beneath it, not the whole tree it is in");
        single!.Own.Should().Be(new UsageFigure(0.02m, 20, 2));
        single.Tree.Should().BeNull("a run that handed no work to another has no tree to show");
    }

    #endregion

    #region Who Sees What

    [Fact]
    public async Task A_member_sees_only_their_own_runs_and_an_admin_sees_other_members_private_runs_only_as_a_total()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var memberId = await world.AddMember(MembershipRole.Member);
        var ownerThreadId = await world.NewThread();
        var memberThreadId = (await world.TransactAs(world.SpaceId, memberId, services => services.GetRequiredService<ThreadService>()
            .Create(world.BotId, "Plans for my landlord", memberId, RunWorld.Token)))!.Id;

        var ownerRun = await SeedRun(world, ownerThreadId, world.BotId, world.UserId);
        var memberRun = await SeedRun(world, memberThreadId, world.BotId, memberId);
        var scheduledRun = await SeedRun(world, memberThreadId, world.BotId, memberId, trigger: RunTrigger.Schedule);
        await SeedCall(world, ownerRun, 1.00m, 100, 10);
        await SeedCall(world, memberRun, 2.00m, 200, 20);
        await SeedCall(world, scheduledRun, 0.50m, 50, 5);

        var memberCrew = await Crew(world, memberId, isSpaceAdmin: false);
        var memberRuns = await CostliestRuns(world, memberId, isSpaceAdmin: false);
        memberCrew.Crew.Should().ContainSingle().Which.Own.Should().Be(new UsageFigure(2.50m, 250, 25), "a member's scheduled runs are theirs too");
        memberRuns.Runs.Select(run => run.RunId).Should().Equal(memberRun.Id, scheduledRun.Id);
        memberRuns.Unopened.Should().BeNull();
        (await RunUsage(world, ownerRun.Id, memberId, isSpaceAdmin: false)).Should().BeNull("a member reads only their own runs");

        var ownerCrew = await Crew(world, world.UserId, isSpaceAdmin: true);
        var ownerRuns = await CostliestRuns(world, world.UserId, isSpaceAdmin: true);
        ownerCrew.Crew.Should().ContainSingle().Which.Own.Should().Be(new UsageFigure(3.50m, 350, 35), "an admin's totals count every run of the space");
        ownerRuns.Runs.Should().ContainSingle().Which.RunId.Should().Be(ownerRun.Id);
        ownerRuns.Runs.Should().NotContain(run => run.ThreadId == memberThreadId);
        ownerRuns.Unopened.Should().Be(new UnopenedRuns(2, new UsageFigure(2.50m, 250, 25)));
        (await RunUsage(world, memberRun.Id, world.UserId, isSpaceAdmin: true)).Should().BeNull("an admin cannot open the member's private conversation");
        (await RunUsage(world, ownerRun.Id, world.UserId, isSpaceAdmin: true)).Should().NotBeNull();
    }

    #endregion

    #region Unknown Prices

    [Fact]
    public async Task A_call_on_a_model_with_no_known_price_is_stored_with_no_cost_and_not_as_free()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("Hello.")]));
        await world.Read(async db =>
        {
            var entry = await db.ModelCatalog.SingleAsync(found => found.ModelConnectionId == world.ConnectionId && found.Model == "scripted", RunWorld.Token);
            entry.Pricing = new ModelPricing();
            return true;
        });
        world.StartHost();
        var threadId = await world.NewThread();

        var runId = await world.Post(threadId, "Hi.");
        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        var record = (await world.ReadUsage(runId)).Should().ContainSingle().Subject;
        record.Cost.Should().BeNull();
        record.InputTokens.Should().BePositive();
        (await RunUsage(world, runId, world.UserId, isSpaceAdmin: false))!.Own.Should().Be(new UsageFigure(null, record.InputTokens, record.OutputTokens, true));
        var completed = (await world.ReadEvents(runId)).Where(stored => stored.Type == RunEventTypes.MessageCompleted).Select(RunWorld.Data<MessageCompleted>);
        completed.Should().NotBeEmpty().And.OnlyContain(
            message => message.Message.RunPrincipalUserId == world.UserId, "a client decides from it whether the reply's usage can be read");
    }

    [Fact]
    public async Task A_total_that_includes_a_model_with_no_known_price_says_it_leaves_it_out()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var helperId = await world.NewBotBeneath(world.BotId, "Planner");
        var threadId = await world.NewThread();
        var root = await SeedRun(world, threadId, world.BotId, world.UserId);
        var child = await SeedRun(world, threadId, helperId, world.UserId, root);
        await SeedCall(world, root, 1.00m, 100, 10);
        await SeedCall(world, root, 0.50m, 50, 5);
        await SeedCall(world, child, null, 400, 40);
        await SeedCall(world, child, 0.25m, 25, 2, currency: "EUR");

        var crew = await Crew(world, world.UserId, isSpaceAdmin: true);
        var detail = await RunUsage(world, root.Id, world.UserId, isSpaceAdmin: true);

        var lead = crew.Crew.Should().ContainSingle().Subject;
        lead.Own.Should().Be(new UsageFigure(1.50m, 150, 15));
        lead.WithBeneath.Should().Be(new UsageFigure(1.50m, 575, 57, LeavesOutUnpriced: true, UncountedCurrencies: "EUR"));
        lead.Children.Should().ContainSingle().Which.Own.Should().Be(
            new UsageFigure(null, 425, 42, LeavesOutUnpriced: true, UncountedCurrencies: "EUR"),
            "no call of the planner has a price in the period's currency, so it has no cost at all, and says why");
        detail!.Tree.Should().Be(new UsageFigure(1.50m, 575, 57, LeavesOutUnpriced: true, UncountedCurrencies: "EUR"));
    }

    [Fact]
    public async Task The_periods_currency_is_the_one_most_priced_calls_used_and_unpriced_calls_do_not_choose_it()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var threadId = await world.NewThread();
        var run = await SeedRun(world, threadId, world.BotId, world.UserId);
        await SeedCall(world, run, null, 10, 1);
        await SeedCall(world, run, null, 10, 1);
        await SeedCall(world, run, null, 10, 1);
        await SeedCall(world, run, 0.50m, 10, 1, currency: "EUR");
        await SeedCall(world, run, 0.25m, 10, 1, currency: "EUR");
        await SeedCall(world, run, 1.00m, 10, 1, currency: "USD");

        var crew = await Crew(world, world.UserId, isSpaceAdmin: true);

        crew.Currency.Should().Be("EUR", "three unpriced calls carry the catalog's default currency, which says nothing about a price");
        crew.Crew.Should().ContainSingle().Which.Own.Should().Be(
            new UsageFigure(0.75m, 60, 6, LeavesOutUnpriced: true, UncountedCurrencies: "USD"));
    }

    [Fact]
    public async Task A_bot_removed_since_keeps_its_figures_and_is_marked_removed()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var helperId = await world.NewBotBeneath(world.BotId, "Planner");
        var threadId = await world.NewThread();
        var run = await SeedRun(world, threadId, helperId, world.UserId);
        await SeedCall(world, run, 0.30m, 30, 3);
        await world.Read(async db =>
            await db.Bots.Where(bot => bot.Id == helperId).ExecuteUpdateAsync(bot => bot.SetProperty(row => row.Status, BotStatus.Removed), RunWorld.Token));

        var crew = await Crew(world, world.UserId, isSpaceAdmin: true);

        var lead = crew.Crew.Should().ContainSingle().Subject;
        lead.IsRemoved.Should().BeFalse();
        lead.Children.Should().ContainSingle().Which.Should().Match<UsageNode>(node => node.IsRemoved && node.Own.Cost == 0.30m);
    }

    #endregion

    #region Periods

    [Fact]
    public void Today_begins_at_midnight_where_the_person_is()
    {
        var now = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

        UsageService.Since(UsagePeriods.Today, utc, now).Should().Be(new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero));
        UsageService.Since(UsagePeriods.Today, berlin, now).Should().Be(new DateTimeOffset(2026, 11, 15, 23, 0, 0, TimeSpan.Zero));
        UsageService.Since(UsagePeriods.Week, utc, now).Should().Be(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero));
        UsageService.Since(UsagePeriods.Month, utc, now).Should().Be(new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero));
        UsageService.Since(UsagePeriods.Month, berlin, now).Should().Be(
            new DateTimeOffset(2026, 10, 17, 22, 0, 0, TimeSpan.Zero), "on 18 October Berlin is still on summer time");
    }

    [Fact]
    public async Task A_call_just_before_midnight_in_UTC_is_today_in_Berlin_and_yesterday_in_UTC()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var threadId = await world.NewThread();
        var run = await SeedRun(world, threadId, world.BotId, world.UserId);
        await SeedCall(world, run, 0.30m, 30, 3, at: new DateTimeOffset(2026, 11, 15, 23, 30, 0, TimeSpan.Zero));
        await SeedCall(world, run, 0.10m, 10, 1, at: new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero));
        await SeedCall(world, run, 5.00m, 500, 50, at: new DateTimeOffset(2026, 11, 15, 22, 30, 0, TimeSpan.Zero));
        var time = new FixedTime(new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero));

        var inUtc = await CrewAt(world, time, utc);
        var inBerlin = await CrewAt(world, time, berlin);

        inUtc.Crew.Should().ContainSingle().Which.Own.Should().Be(new UsageFigure(0.10m, 10, 1));
        inBerlin.Crew.Should().ContainSingle().Which.Own.Should().Be(new UsageFigure(0.40m, 40, 4));
    }

    [Fact]
    public async Task A_period_with_no_calls_has_no_bots()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var threadId = await world.NewThread();
        var run = await SeedRun(world, threadId, world.BotId, world.UserId);
        await SeedCall(world, run, 0.30m, 30, 3, at: new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));

        var crew = await CrewAt(world, new FixedTime(new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero)), utc);

        crew.Crew.Should().BeEmpty();
        crew.Currency.Should().Be("USD");
    }

    #endregion

    #region Private Methods

    private static Task<Run> SeedRun(
        RunWorld world,
        Guid threadId,
        Guid botId,
        Guid principalUserId,
        Run? parent = null,
        RunTrigger trigger = RunTrigger.Message) =>
        world.Read(db =>
        {
            var run = new Run
            {
                ThreadId = threadId,
                BotId = botId,
                PrincipalUserId = principalUserId,
                Trigger = parent is null ? trigger : RunTrigger.Delegation,
                ParentRunId = parent?.Id,
                Depth = parent is null ? 0 : parent.Depth + 1,
                Status = RunStatus.Completed,
            };
            run.RootRunId = parent?.RootRunId ?? run.Id;
            db.Runs.Add(run);
            return Task.FromResult(run);
        });

    private static Task<bool> SeedCall(
        RunWorld world,
        Run run,
        decimal? cost,
        long inputTokens,
        long outputTokens,
        DateTimeOffset? at = null,
        string currency = "USD") =>
        world.Read(db =>
        {
            db.UsageRecords.Add(new UsageRecord
            {
                RunId = run.Id,
                RootRunId = run.RootRunId,
                BotId = run.BotId,
                ModelConnectionId = world.ConnectionId,
                Model = "scripted",
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                Cost = cost,
                Currency = currency,
                CreatedAt = at ?? DateTimeOffset.UtcNow,
            });
            return Task.FromResult(true);
        });

    /// <summary>The figure the stored records of these bots add up to, summed here without the service.</summary>
    private static Task<UsageFigure> StoredSum(RunWorld world, Guid[] botIds) =>
        world.Read(async db => Figure(await db.UsageRecords.Where(usage => botIds.Contains(usage.BotId)).ToListAsync(RunWorld.Token)));

    private static Task<UsageFigure> StoredRunSum(RunWorld world, Guid[] runIds) =>
        world.Read(async db => Figure(await db.UsageRecords.Where(usage => runIds.Contains(usage.RunId)).ToListAsync(RunWorld.Token)));

    private static UsageFigure Figure(List<UsageRecord> records) =>
        new(records.Sum(record => record.Cost ?? 0), records.Sum(record => record.InputTokens), records.Sum(record => record.OutputTokens));

    private static Task<CrewUsage> Crew(RunWorld world, Guid userId, bool isSpaceAdmin) =>
        world.TransactAs(world.SpaceId, userId, services => services.GetRequiredService<UsageService>()
            .GetCrewUsage(UsagePeriods.Week, utc, userId, isSpaceAdmin, RunWorld.Token));

    private static Task<CrewUsage> CrewAt(RunWorld world, TimeProvider time, TimeZoneInfo zone) =>
        world.Transact(services => new UsageService(
                services.GetRequiredService<UsageRepository>(),
                services.GetRequiredService<BotRepository>(),
                services.GetRequiredService<RunRepository>(),
                services.GetRequiredService<ThreadRepository>(),
                time)
            .GetCrewUsage(UsagePeriods.Today, zone, world.UserId, isSpaceAdmin: true, RunWorld.Token));

    private static Task<BotRunsUsage> CostliestRuns(RunWorld world, Guid userId, bool isSpaceAdmin) =>
        world.TransactAs(world.SpaceId, userId, services => services.GetRequiredService<UsageService>()
            .ListCostliestRuns(world.BotId, UsagePeriods.Week, utc, userId, isSpaceAdmin, RunWorld.Token));

    private static Task<RunUsageDetail?> RunUsage(RunWorld world, Guid runId, Guid userId, bool isSpaceAdmin) =>
        world.TransactAs(world.SpaceId, userId, services => services.GetRequiredService<UsageService>()
            .GetRunUsage(runId, userId, isSpaceAdmin, RunWorld.Token));

    #endregion

    #region Types

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    #endregion
}
