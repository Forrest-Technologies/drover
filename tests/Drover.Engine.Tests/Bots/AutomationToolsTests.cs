namespace Drover.Engine.Tests.Bots;

public sealed class AutomationToolsTests(PostgresServer postgres)
{
    #region Private Fields

    private const string Routine =
        """
        {"cron":"0 6 * * *","time_zone":"America/New_York","prompt":"Plan the day.","thread_mode":"same_thread","enabled":true}
        """;

    private const string Rule =
        """
        {"name":"Review web writes","enabled":true,"checkpoint":"before_tool","effect":"ask","conditions":[{"kind":"tool_kind","tool_kinds":["write"]}]}
        """;

    private const string NarrowRule =
        """
        {"name":"Review web fetch","enabled":true,"checkpoint":"before_tool","effect":"ask","conditions":[{"kind":"tool_is","tools":["web.fetch"]}]}
        """;

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_bot_adds_and_changes_its_own_routine_without_approval()
    {
        var script = new ScriptedModel(
            [new ToolCallOutput("routine", "routine_manage_own", $$"""{"operation":"add","routine":{{Routine}}}""")],
            [new TextOutput("Routine saved.")]);
        await using var world = await RunWorld.Create(postgres, script);
        var host = world.StartHost();
        var runId = await world.Post(await world.NewThread(), "Plan the day every morning.");
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Waiting, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        var saved = await world.Read(db => db.BotSchedules.AsNoTracking().SingleAsync(RunWorld.Token));
        saved.BotId.Should().Be(world.BotId);
        saved.OwnerUserId.Should().Be(world.UserId);
        saved.TimeZone.Should().Be("America/New_York");
        saved.ThreadMode.Should().Be(ScheduleThreadMode.SameThread);
        world.Model.ToolsOffered[0].Should().Contain("routine_manage_own");

        var changedRoutine = Routine.Replace("0 6", "0 22", StringComparison.Ordinal);
        await Call(world, host, world.BotId, "routine.manage_own",
            $$"""{"operation":"change","id":"{{saved.Id}}","routine":{{changedRoutine}}}""");
        var listed = await Call(world, host, world.BotId, "routine.list", "{}");
        listed.GetProperty("routines")[0].GetProperty("cron").GetString().Should().Be("0 22 * * *");

        await Call(world, host, world.BotId, "routine.manage_own", $$"""{"operation":"remove","id":"{{saved.Id}}"}""");
        (await world.Read(db => db.BotSchedules.CountAsync(RunWorld.Token))).Should().Be(0);
        var audit = await world.Read(db => db.AuditLog.AsNoTracking()
            .Where(entry => entry.Action.StartsWith("bot.schedule_")).ToListAsync(RunWorld.Token));
        audit.Should().HaveCount(3).And.OnlyContain(entry => entry.ActorId == world.BotId && entry.ActorKind == ActorKind.Bot);
        audit.Should().OnlyContain(entry => entry.Data.GetProperty("for_user_id").GetGuid() == world.UserId);
    }

    [Fact]
    public async Task A_parents_routine_waits_until_the_person_approves_and_never_works_upward()
    {
        var script = new ScriptedModel();
        await using var world = await RunWorld.Create(postgres, script);
        var child = await world.NewBotBeneath(world.BotId, "Scout");
        script.Then(
            [new ToolCallOutput("routine", "routine_manage_descendant",
                $$"""{"operation":"add","bot_id":"{{child}}","routine":{{Routine}}}""")],
            [new TextOutput("Routine saved.")]);
        var host = world.StartHost();
        var runId = await world.Post(await world.NewThread(), "Schedule a routine for Scout.");
        (await world.WaitForStatus(runId, RunStatus.Waiting, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Waiting);
        (await world.Read(db => db.BotSchedules.CountAsync(RunWorld.Token))).Should().Be(0);

        var asked = (await world.ReadToolCalls(runId)).Single();
        (await world.Decide(asked.Id, approved: true)).Outcome.Should().Be(DecisionOutcome.Recorded);
        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        (await world.Read(db => db.BotSchedules.AsNoTracking().SingleAsync(RunWorld.Token))).BotId.Should().Be(child);

        var upward = () => Call(world, host, child, "routine.manage_descendant",
            $$"""{"operation":"add","bot_id":"{{world.BotId}}","routine":{{Routine}}}""");
        await upward.Should().ThrowAsync<BuiltinToolException>();
    }

    [Fact]
    public async Task A_bot_changes_its_own_rules_and_cannot_take_back_a_rule_its_parent_edits()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var child = await world.NewBotBeneath(world.BotId, "Scout");
        var unrelated = await world.NewBotBeneath(null, "Another root");
        var host = world.StartHost();

        var created = await Call(world, host, child, "rule.manage_own", $$"""{"operation":"add","rule":{{Rule}}}""");
        var rule = created.GetProperty("id").GetGuid();
        await Call(world, host, child, "rule.manage_own", $$"""{"operation":"change","id":"{{rule}}","rule":{{NarrowRule}}}""");
        await Call(world, host, world.BotId, "rule.change_for_descendant",
            $$"""{"operation":"change","bot_id":"{{child}}","id":"{{rule}}","rule":{{Rule}}}""");
        var blocked = () => Call(world, host, child, "rule.manage_own", $$"""{"operation":"remove","id":"{{rule}}"}""");
        await blocked.Should().ThrowAsync<BuiltinToolException>();

        var parentAdded = await Call(world, host, world.BotId, "rule.add_for_descendant",
            $$"""{"operation":"add","bot_id":"{{child}}","rule":{{NarrowRule}}}""");
        var parentRule = parentAdded.GetProperty("id").GetGuid();
        var unrelatedChange = () => Call(world, host, unrelated, "rule.change_for_descendant",
            $$"""{"operation":"remove","bot_id":"{{child}}","id":"{{parentRule}}"}""");
        await unrelatedChange.Should().ThrowAsync<BuiltinToolException>();

        var listed = await Call(world, host, child, "rule.list", "{}");
        listed.GetProperty("rules").EnumerateArray().Should().OnlyContain(item => !item.GetProperty("may_change").GetBoolean());
        await Call(world, host, world.BotId, "rule.change_for_descendant",
            $$"""{"operation":"remove","bot_id":"{{child}}","id":"{{rule}}"}""");
    }

    [Fact]
    public async Task A_parent_adds_a_rule_automatically_and_removing_it_defaults_to_ask()
    {
        var script = new ScriptedModel();
        await using var world = await RunWorld.Create(postgres, script);
        var child = await world.NewBotBeneath(world.BotId, "Scout");
        world.StartHost();
        var thread = await world.NewThread();
        script.Then(
            [new ToolCallOutput("add", "rule_add_for_descendant",
                $$"""{"operation":"add","bot_id":"{{child}}","rule":{{NarrowRule}}}""")],
            [new TextOutput("Rule saved.")]);

        var added = await world.Post(thread, "Ask before fetching web pages for Scout.");
        (await world.WaitForStatus(added, RunStatus.Completed, RunStatus.Waiting, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        var rule = await world.Read(db => db.BotRules.AsNoTracking().SingleAsync(RunWorld.Token));
        rule.BotId.Should().Be(child);
        rule.ManagingBotId.Should().Be(world.BotId);

        script.Then(
            [new ToolCallOutput("remove", "rule_change_for_descendant",
                $$"""{"operation":"remove","bot_id":"{{child}}","id":"{{rule.Id}}"}""")],
            [new TextOutput("Rule removed.")]);
        var removed = await world.Post(thread, "Remove Scout's web rule.");
        (await world.WaitForStatus(removed, RunStatus.Waiting, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Waiting);
        (await world.Read(db => db.BotRules.CountAsync(RunWorld.Token))).Should().Be(1);
        (await world.Decide((await world.ReadToolCalls(removed)).Single().Id, approved: true)).Outcome.Should().Be(DecisionOutcome.Recorded);
        (await world.WaitForStatus(removed, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        (await world.Read(db => db.BotRules.CountAsync(RunWorld.Token))).Should().Be(0);
    }

    [Fact]
    public async Task An_unapproved_tainted_run_cannot_store_a_prompt_or_rule()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.StartHost();
        var call = () => Call(world, host, world.BotId, "rule.manage_own",
            $$"""{"operation":"add","rule":{{Rule}}}""", tainted: true);

        await call.Should().ThrowAsync<BuiltinToolException>();
        (await world.Read(db => db.BotRules.CountAsync(RunWorld.Token))).Should().Be(0);
    }

    #endregion

    #region Private Methods

    private static Task<JsonElement> Call(
        RunWorld world, EngineHost host, Guid bot, string name, string arguments, bool tainted = false)
    {
        var tool = host.Services.GetServices<BuiltinTool>().Single(tool => tool.Name == name);
        var call = new BuiltinToolCall(
            JsonElement.Parse(arguments), bot, Guid.CreateVersion7(), Guid.CreateVersion7(),
            host.Services.GetRequiredService<RunScopes>().For(world.SpaceId, world.UserId),
            _ => Task.CompletedTask, () => Task.CompletedTask, _ => { })
        {
            IsTainted = () => tainted,
        };
        return tool.Run(call, RunWorld.Token);
    }

    #endregion
}
