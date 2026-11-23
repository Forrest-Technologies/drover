namespace Drover.Engine.Tests.Bots;

/// <summary>
/// The crew tools over real PostgreSQL: a bot makes, lists, changes, pauses, and removes the bots beneath it, through
/// a real run with the scripted model and by calling the tools directly, and is refused everything else in words.
/// </summary>
public sealed class CrewToolsTests(PostgresServer postgres)
{
    #region Private Fields

    private Guid otherSpace;

    private Guid otherPerson;

    #endregion

    #region Through A Run

    [Fact]
    public async Task A_bot_makes_a_bot_beneath_itself_in_a_run_and_the_new_bot_records_its_maker_and_holds_no_more_than_it()
    {
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "crew_add", """{"name":"Scout","description":"Finds sources.","instructions":"Search carefully."}""")],
            [new TextOutput("Scout is ready.")]);
        await using var world = await RunWorld.Create(postgres, script);
        await SetGrant(world, world.BotId, "crew.add", GrantEffect.Allow);
        await SetGrant(world, world.BotId, "time.now", GrantEffect.Ask);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "We need someone to find sources.");
        var waiting = await world.WaitForStatus(runId, RunStatus.Waiting, RunStatus.Completed, RunStatus.Failed);

        waiting.Status.Should().Be(RunStatus.Waiting, "making a bot always waits for a person, even where crew.add is set to allow");
        (await CountBots(world)).Should().Be(1, "nothing is made before a person says yes");
        (await world.Decide((await world.ReadToolCalls(runId)).Single().Id, approved: true)).Outcome.Should().Be(DecisionOutcome.Recorded);
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        var lead = await ReadBot(world, world.BotId);
        var scout = await world.Read(db => db.Bots.AsNoTracking().SingleAsync(bot => bot.Name == "Scout", RunWorld.Token));
        scout.Should().Match<Bot>(bot => bot.ParentBotId == world.BotId && bot.Depth == 1
            && bot.CreatedByBotId == world.BotId && bot.CreatedByUserId == world.UserId
            && bot.DescriptionForBots == "Finds sources." && bot.Instructions == "Search carefully."
            && bot.ModelConnectionId == lead.ModelConnectionId && bot.Model == lead.Model
            && bot.SandboxPolicy == SandboxPolicy.None && bot.Status == BotStatus.Active);

        var grants = await OwnGrants(world, scout.Id);
        grants.Keys.Should().Equal(["time.now"], "a bot made beneath a parent never gets the crew tools by default");
        grants["time.now"].Should().Match<ToolGrant>(grant => grant.Effect == GrantEffect.Ask
            && grant.GrantedByBotId == world.BotId && grant.GrantedByUserId == world.UserId);

        var created = (await AuditAbout(world, scout.Id)).Single(entry => entry.Action == "bot.created");
        created.Should().Match<AuditLogEntry>(entry => entry.ActorKind == ActorKind.Bot && entry.ActorId == world.BotId);
        created.Data.GetProperty("for_user_id").GetGuid().Should().Be(world.UserId);
        created.Data.GetProperty("parent_bot_id").GetGuid().Should().Be(world.BotId);

        var completed = (await world.ReadEvents(runId)).Where(stored => stored.Type == RunEventTypes.ToolCompleted).Select(RunWorld.Data<ToolCompleted>);
        completed.Should().ContainSingle().Which.Outcome.Should().Be("completed");
        script.Requests[1].Should().Contain(message => message.Role == "tool" && message.Summary.Contains(scout.Id.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_the_default_grant_of_ask_the_run_waits_and_no_bot_exists_until_a_person_approves()
    {
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "crew_add", """{"name":"Scout","description":"Finds sources.","instructions":"Search."}""")],
            [new TextOutput("Scout is ready.")]);
        await using var world = await RunWorld.Create(postgres, script);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Make a scout.");
        var waiting = await world.WaitForStatus(runId, RunStatus.Waiting, RunStatus.Completed, RunStatus.Failed);

        waiting.Status.Should().Be(RunStatus.Waiting, waiting.Error?.Message);
        waiting.StatusReason.Should().Be(RunReasons.AwaitingApproval);
        (await CountBots(world)).Should().Be(1, "nothing is made before a person says yes");
        var requested = (await world.ReadEvents(runId)).Single(stored => stored.Type == RunEventTypes.ToolRequested);
        RunWorld.Data<ToolRequested>(requested).Effect.Should().Be("ask");
        var asked = RunWorld.Data<ToolApprovalRequested>((await world.ReadEvents(runId)).Single(stored => stored.Type == RunEventTypes.ToolApprovalRequested));
        asked.Details.Should().Equal(
            new ToolApprovalDetail(ToolApprovalRequested.ModelDetail, "scripted"),
            new ToolApprovalDetail(ToolApprovalRequested.BeneathDetail, "Helper"));

        (await world.Decide((await world.ReadToolCalls(runId)).Single().Id, approved: true)).Outcome.Should().Be(DecisionOutcome.Recorded);
        var done = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        done.Status.Should().Be(RunStatus.Completed, done.Error?.Message);
        (await CountBots(world)).Should().Be(2);
    }

    [Fact]
    public async Task Where_the_server_does_not_let_bots_build_bots_no_crew_tool_is_granted_offered_or_run()
    {
        var policy = DeploymentPolicy.SelfHosted with { AllowBotsBuildingBots = false };
        var script = new ScriptedModel([new TextOutput("I cannot.")]);
        await using var world = await RunWorld.Create(postgres, script, everyHost: services => UsePolicy(services, policy));

        (await OwnGrants(world, world.BotId)).Keys.Should().Equal(["time.now"], "a new bot at the top is granted no crew tool");
        await SetGrant(world, world.BotId, "crew.add", GrantEffect.Allow);
        var host = world.StartHost();
        var runId = await world.Post(await world.NewThread(), "Make a scout.");
        await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);
        var call = async () => await Tool(host, "crew.add").Run(Call(world, host, world.BotId, """{"name":"Scout"}"""), RunWorld.Token);

        world.Model.ToolsOffered[0].Should().NotContain("crew_add", "a granted crew tool is not offered where the rule is off");
        (await call.Should().ThrowAsync<BuiltinToolException>()).Which.Message.Should().Contain(nameof(DeploymentPolicy.AllowBotsBuildingBots));
        (await CountBots(world)).Should().Be(1);
    }

    #endregion

    #region Calling The Tools

    [Fact]
    public async Task A_bot_lists_changes_pauses_resumes_and_removes_the_bots_beneath_it_and_each_change_names_it()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.StartHost();
        var scoutId = await Add(world, host, world.BotId, """{"name":"Scout","description":"Finds sources.","instructions":"Search."}""");
        var trackerId = await Add(world, host, world.BotId, $$"""{"name":"Tracker","description":"Follows leads.","instructions":"Track.","parent_id":"{{scoutId}}"}""");

        var listed = await Run(world, host, world.BotId, "crew.list", "{}");
        var changed = await Run(world, host, world.BotId, "crew.change", $$"""{"bot_id":"{{trackerId}}","name":"Finder","description":"Finds people.","instructions":"Find."}""");
        await Run(world, host, world.BotId, "crew.change", $$"""{"bot_id":"{{trackerId}}","status":"paused"}""");
        var paused = await ReadBot(world, trackerId);
        await Run(world, host, scoutId, "crew.change", $$"""{"bot_id":"{{trackerId}}","status":"active"}""");
        var resumed = await ReadBot(world, trackerId);
        var removed = await Run(world, host, world.BotId, "crew.remove", $$"""{"bot_id":"{{scoutId}}","beneath":"remove"}""");

        listed.GetProperty("bots").EnumerateArray().Select(bot => (bot.GetProperty("id").GetGuid(), bot.GetProperty("parent_id").GetGuid(), bot.GetProperty("description").GetString()))
            .Should().Equal((scoutId, world.BotId, "Finds sources."), (trackerId, scoutId, "Follows leads."));
        changed.GetProperty("name").GetString().Should().Be("Finder");
        (await ReadBot(world, trackerId)).Should().Match<Bot>(bot => bot.Name == "Finder" && bot.DescriptionForBots == "Finds people." && bot.Instructions == "Find.");
        paused.Status.Should().Be(BotStatus.Paused);
        resumed.Status.Should().Be(BotStatus.Active, "a bot beneath the tracker's parent may resume it too");
        removed.GetProperty("status").GetString().Should().Be("removed");
        removed.GetProperty("beneath").GetString().Should().Be("remove");
        removed.GetProperty("bots_beneath").EnumerateArray().Select(id => id.GetGuid()).Should().Equal(trackerId);
        (await ReadBot(world, scoutId)).Status.Should().Be(BotStatus.Removed);
        (await ReadBot(world, trackerId)).Status.Should().Be(BotStatus.Removed, "the person chose to remove the bots beneath with it");
        (await AuditAbout(world, trackerId)).Where(entry => entry.Action != "bot.created")
            .Select(entry => (entry.Action, entry.ActorKind, entry.ActorId))
            .Should().Equal(
                ("bot.updated", ActorKind.Bot, world.BotId),
                ("bot.paused", ActorKind.Bot, world.BotId),
                ("bot.resumed", ActorKind.Bot, scoutId),
                ("bot.removed", ActorKind.Bot, world.BotId));
        (await AuditAbout(world, scoutId)).Should().ContainSingle(entry => entry.Action == "bot.removed" && entry.ActorId == world.BotId);
    }

    [Fact]
    public async Task A_bot_cannot_act_on_itself_its_parent_a_sibling_or_a_bot_of_another_space()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.StartHost();
        var scoutId = await Add(world, host, world.BotId, """{"name":"Scout","description":"","instructions":""}""");
        var siblingId = await Add(world, host, world.BotId, """{"name":"Sibling","description":"","instructions":""}""");
        var otherSpaceBot = await BotOfAnotherSpace(world);

        foreach (var target in new[] { world.BotId, siblingId, otherSpaceBot, Guid.CreateVersion7() })
        {
            var change = async () => await Run(world, host, scoutId, "crew.change", $$"""{"bot_id":"{{target}}","name":"Taken"}""");
            var remove = async () => await Run(world, host, scoutId, "crew.remove", $$"""{"bot_id":"{{target}}"}""");
            var beneath = async () => await Run(world, host, scoutId, "crew.add", $$"""{"name":"Hidden","parent_id":"{{target}}"}""");

            (await change.Should().ThrowAsync<BuiltinToolException>()).Which.Message.Should().Be(
                $"There is no bot with the id {target} beneath this bot. A bot can manage only the bots beneath it; crew_list shows them.");
            await remove.Should().ThrowAsync<BuiltinToolException>();
            await beneath.Should().ThrowAsync<BuiltinToolException>();
        }

        var self = async () => await Run(world, host, scoutId, "crew.remove", $$"""{"bot_id":"{{scoutId}}"}""");
        (await self.Should().ThrowAsync<BuiltinToolException>()).Which.Message.Should().StartWith("A bot cannot change itself");
        (await Run(world, host, scoutId, "crew.list", "{}")).GetProperty("bots").GetArrayLength().Should().Be(0, "the parent and the sibling are not beneath it");
        (await world.Read(db => db.Bots.AsNoTracking().Where(bot => bot.Name == "Taken" || bot.Name == "Hidden" || bot.Status != BotStatus.Active).CountAsync(RunWorld.Token)))
            .Should().Be(0, "nothing was changed, removed, or made");
        (await world.TransactAs(otherSpace, otherPerson, services =>
            services.GetRequiredService<DroverDbContext>().Bots.AsNoTracking().SingleAsync(bot => bot.Id == otherSpaceBot, RunWorld.Token)))
            .Name.Should().Be("Elsewhere");
    }

    [Fact]
    public async Task The_caps_on_the_tree_refuse_a_new_bot_with_the_sentence_a_person_is_shown()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.StartHost();
        await world.Read(async db =>
        {
            var lead = await db.Bots.SingleAsync(bot => bot.Id == world.BotId, RunWorld.Token);
            lead.BuildLimits = new BuildLimits { MaxChildren = 1 };
            var space = await db.Spaces.SingleAsync(found => found.Id == world.SpaceId, RunWorld.Token);
            space.Settings = new SpaceSettings { MaxBotDepth = 1 };
            return true;
        });
        var scoutId = await Add(world, host, world.BotId, """{"name":"Scout"}""");

        var second = async () => await Run(world, host, world.BotId, "crew.add", """{"name":"Second"}""");
        var tooDeep = async () => await Run(world, host, scoutId, "crew.add", """{"name":"Deep"}""");

        (await second.Should().ThrowAsync<BuiltinToolException>()).Which.Message.Should().Be("Helper may have at most 1 bot directly beneath it, and already has 1.");
        (await tooDeep.Should().ThrowAsync<BuiltinToolException>()).Which.Message.Should().Be("This space allows bots at most 1 level below the top, so Scout cannot have a bot beneath it.");
        (await CountBots(world)).Should().Be(2);
    }

    [Fact]
    public async Task A_run_for_a_member_may_list_the_crew_but_not_change_it()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.StartHost();
        var scoutId = await Add(world, host, world.BotId, """{"name":"Scout"}""");
        var member = await world.AddMember(MembershipRole.Member);

        var listed = await Tool(host, "crew.list").Run(Call(world, host, world.BotId, "{}", member), RunWorld.Token);
        var add = async () => await Tool(host, "crew.add").Run(Call(world, host, world.BotId, """{"name":"Mine"}""", member), RunWorld.Token);
        var remove = async () => await Tool(host, "crew.remove").Run(Call(world, host, world.BotId, $$"""{"bot_id":"{{scoutId}}"}""", member), RunWorld.Token);

        listed.GetProperty("bots").GetArrayLength().Should().Be(1);
        (await add.Should().ThrowAsync<BuiltinToolException>()).Which.Message.Should().StartWith("Only an owner or admin of this space can make or change bots");
        await remove.Should().ThrowAsync<BuiltinToolException>();
        (await CountBots(world)).Should().Be(2);
        (await ReadBot(world, scoutId)).Status.Should().Be(BotStatus.Active);
    }

    [Fact]
    public async Task Removing_a_branch_retires_the_sandbox_of_every_bot_in_it_as_a_persons_remove_does()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await RunWorld.Create(postgres, new ScriptedModel(), everyHost: services =>
        {
            services.AddSingleton<ISandboxProvider>(provider);
            services.AddSandboxTools();
        });
        var host = world.StartHost();
        var scoutId = await Add(world, host, world.BotId, """{"name":"Scout"}""");
        var trackerId = await Add(world, host, world.BotId, $$"""{"name":"Tracker","parent_id":"{{scoutId}}"}""");
        await world.Read(async db =>
        {
            foreach (var bot in await db.Bots.Where(bot => bot.Id == scoutId || bot.Id == trackerId).ToListAsync(RunWorld.Token))
            {
                bot.SandboxPolicy = SandboxPolicy.Private;
            }

            return true;
        });
        await host.Services.GetRequiredService<SandboxBroker>().Run(Call(world, host, scoutId, "{}"), null, "true", null, null, RunWorld.Token);
        await host.Services.GetRequiredService<SandboxBroker>().Run(Call(world, host, trackerId, "{}"), null, "true", null, null, RunWorld.Token);

        await Run(world, host, world.BotId, "crew.remove", $$"""{"bot_id":"{{scoutId}}","beneath":"remove"}""");

        (await world.Read(db => db.Sandboxes.AsNoTracking().Select(sandbox => sandbox.State).ToListAsync(RunWorld.Token)))
            .Should().Equal(SandboxState.Retiring, SandboxState.Retiring);
        (await world.Read(db => db.Bots.AsNoTracking().CountAsync(bot => bot.Status == BotStatus.Removed, RunWorld.Token))).Should().Be(2);
    }

    [Fact]
    public async Task Removing_a_bot_with_bots_beneath_tells_the_model_to_ask_the_person_and_keep_moves_them_up()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.StartHost();
        var scoutId = await Add(world, host, world.BotId, """{"name":"Scout"}""");
        var trackerId = await Add(world, host, world.BotId, $$"""{"name":"Tracker","parent_id":"{{scoutId}}"}""");

        var unasked = async () => await Run(world, host, world.BotId, "crew.remove", $$"""{"bot_id":"{{scoutId}}"}""");
        var refusal = (await unasked.Should().ThrowAsync<BuiltinToolException>()).Which.Message;
        var untouched = await CountBots(world, BotStatus.Removed);
        var kept = await Run(world, host, world.BotId, "crew.remove", $$"""{"bot_id":"{{scoutId}}","beneath":"keep"}""");

        refusal.Should().Be("Scout has 1 bot directly beneath it. Choose whether they are removed with it or kept and moved up. Ask the person which they want, then call crew_remove again with beneath set to remove or keep.");
        untouched.Should().Be(0, "nothing is removed until the person has chosen");
        Tool(host, "crew.remove").Description.Should().Contain("ask the person");
        kept.GetProperty("beneath").GetString().Should().Be("keep");
        (await ReadBot(world, scoutId)).Status.Should().Be(BotStatus.Removed);
        (await ReadBot(world, trackerId)).Should().Match<Bot>(bot => bot.Status == BotStatus.Active && bot.ParentBotId == world.BotId && bot.Depth == 1);
        (await world.Read(db => db.BotAncestors.AsNoTracking().Where(row => row.DescendantBotId == trackerId)
                .OrderBy(row => row.Distance).Select(row => new { row.AncestorBotId, row.Distance }).ToListAsync(RunWorld.Token)))
            .Should().Equal(new { AncestorBotId = trackerId, Distance = 0 }, new { AncestorBotId = world.BotId, Distance = 1 });
        (await Run(world, host, world.BotId, "crew.remove", $$"""{"bot_id":"{{trackerId}}"}""")).GetProperty("status").GetString()
            .Should().Be("removed", "a bot with nothing beneath it needs no choice");
    }

    [Fact]
    public async Task Keep_through_the_crew_tool_copies_the_removed_bots_rules_to_the_bots_that_move_up()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.StartHost();
        var scoutId = await Add(world, host, world.BotId, """{"name":"Scout"}""");
        var trackerId = await Add(world, host, world.BotId, $$"""{"name":"Tracker","parent_id":"{{scoutId}}"}""");
        var originalId = await world.Read(db =>
        {
            var rule = new BotRule
            {
                BotId = scoutId,
                Name = "Never say the word",
                Enabled = true,
                Checkpoint = RuleCheckpoint.BeforeReply,
                Effect = RuleEffect.Block,
                Conditions = [new RuleCondition { Kind = RuleConditionKind.TextContains, Entries = ["tangerine"] }],
                CreatedByUserId = world.UserId,
            };
            db.BotRules.Add(rule);
            return Task.FromResult(rule.Id);
        });

        await Run(world, host, world.BotId, "crew.remove", $$"""{"bot_id":"{{scoutId}}","beneath":"keep"}""");

        var copied = await world.Read(db => db.BotRules.AsNoTracking()
            .Where(rule => rule.BotId == trackerId)
            .Select(rule => new { rule.Id, rule.Name, rule.Enabled, rule.CreatedByUserId, rule.Position })
            .ToListAsync(RunWorld.Token));
        copied.Should().ContainSingle();
        copied[0].Id.Should().NotBe(originalId, "the copy is a new row");
        copied[0].Name.Should().Be("Never say the word");
        copied[0].Enabled.Should().BeTrue();
        copied[0].CreatedByUserId.Should().Be(world.UserId);
        copied[0].Position.Should().Be(0);
    }

    [Fact]
    public async Task Making_a_bot_is_refused_at_the_spaces_cap_on_its_bots_and_a_removed_bot_frees_its_place()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.StartHost();
        await world.Read(async db =>
        {
            (await db.Spaces.SingleAsync(found => found.Id == world.SpaceId, RunWorld.Token)).Settings = new SpaceSettings { MaxBots = 2 };
            return true;
        });
        var scoutId = await Add(world, host, world.BotId, """{"name":"Scout"}""");

        var full = async () => await Run(world, host, world.BotId, "crew.add", """{"name":"Third"}""");
        (await full.Should().ThrowAsync<BuiltinToolException>()).Which.Message.Should().Be(
            "This space allows at most 2 bots, and already has 2. Remove a bot to make room for another.");
        await Run(world, host, world.BotId, "crew.remove", $$"""{"bot_id":"{{scoutId}}"}""");
        await Add(world, host, world.BotId, """{"name":"Third"}""");

        (await CountBots(world)).Should().Be(3, "the removed bot's row stays");
        (await CountBots(world, BotStatus.Active)).Should().Be(2);
    }

    [Fact]
    public async Task Making_a_bot_always_asks_and_says_what_model_it_will_use_and_which_bot_it_will_go_beneath()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var host = world.StartHost();
        var scoutId = await Add(world, host, world.BotId, """{"name":"Scout"}""");
        var tool = Tool(host, "crew.add");

        var beneathScout = await tool.DescribeForApproval!(Call(world, host, world.BotId, $$"""{"name":"Deep","parent_id":"{{scoutId}}"}"""), RunWorld.Token);
        var elsewhere = await tool.DescribeForApproval!(Call(world, host, scoutId, $$"""{"name":"Up","parent_id":"{{world.BotId}}"}"""), RunWorld.Token);

        tool.AlwaysAsks.Should().BeTrue();
        CrewTools.Names.Where(name => Tool(host, name).AlwaysAsks).Should().Equal(["crew.add"], "changing and removing follow their grant");
        beneathScout.Should().Equal(
            new ToolApprovalDetail(ToolApprovalRequested.ModelDetail, "scripted"),
            new ToolApprovalDetail(ToolApprovalRequested.BeneathDetail, "Scout"));
        elsewhere.Should().Equal([new ToolApprovalDetail(ToolApprovalRequested.ModelDetail, "scripted")], "a bot not beneath the caller is not named");
    }

    #endregion

    #region Private Methods

    private static void UsePolicy(IServiceCollection services, DeploymentPolicy policy) =>
        services.AddSingleton<IOptions<DeploymentPolicyOptions>>(Options.Create(new DeploymentPolicyOptions { Rules = policy }));

    private static BuiltinTool Tool(EngineHost host, string name) =>
        host.Services.GetServices<BuiltinTool>().Single(tool => tool.Name == name);

    private static BuiltinToolCall Call(RunWorld world, EngineHost host, Guid botId, string arguments, Guid? asUser = null) =>
        new(
            JsonElement.Parse(arguments),
            botId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            host.Services.GetRequiredService<RunScopes>().For(world.SpaceId, asUser ?? world.UserId),
            _ => Task.CompletedTask,
            () => Task.CompletedTask,
            _ => { });

    private static Task<JsonElement> Run(RunWorld world, EngineHost host, Guid botId, string tool, string arguments) =>
        Tool(host, tool).Run(Call(world, host, botId, arguments), RunWorld.Token);

    private static async Task<Guid> Add(RunWorld world, EngineHost host, Guid botId, string arguments) =>
        (await Run(world, host, botId, "crew.add", arguments)).GetProperty("id").GetGuid();

    private static Task<Bot> ReadBot(RunWorld world, Guid botId) =>
        world.Read(db => db.Bots.AsNoTracking().SingleAsync(bot => bot.Id == botId, RunWorld.Token));

    private static Task<int> CountBots(RunWorld world) => world.Read(db => db.Bots.CountAsync(RunWorld.Token));

    private static Task<int> CountBots(RunWorld world, BotStatus status) =>
        world.Read(db => db.Bots.CountAsync(bot => bot.Status == status, RunWorld.Token));

    private static Task<Dictionary<string, ToolGrant>> OwnGrants(RunWorld world, Guid botId) =>
        world.Read(db => db.ToolGrants.AsNoTracking()
            .Where(grant => grant.BotId == botId)
            .Join(db.Tools, grant => grant.ToolId, tool => tool.Id, (grant, tool) => new { tool.Name, Grant = grant })
            .ToDictionaryAsync(entry => entry.Name, entry => entry.Grant, RunWorld.Token));

    private static Task<List<AuditLogEntry>> AuditAbout(RunWorld world, Guid botId) =>
        world.Transact(services => services.GetRequiredService<AuditRepository>().ListEntriesAbout(botId, RunWorld.Token));

    /// <summary>Sets a bot's own grant on a tool, as a person would in the bot editor.</summary>
    private static Task<bool> SetGrant(RunWorld world, Guid botId, string toolName, GrantEffect effect) =>
        world.Read(async db =>
        {
            var tool = await db.Tools.SingleAsync(found => found.Name == toolName, RunWorld.Token);
            var grant = await db.ToolGrants.SingleOrDefaultAsync(found => found.BotId == botId && found.ToolId == tool.Id, RunWorld.Token);

            if (grant is null)
            {
                db.ToolGrants.Add(new ToolGrant { BotId = botId, ToolId = tool.Id, Effect = effect, GrantedByUserId = world.UserId });
            }
            else
            {
                grant.Effect = effect;
            }

            return true;
        });

    /// <summary>A second person's space with one bot, made through the same service.</summary>
    private async Task<Guid> BotOfAnotherSpace(RunWorld world)
    {
        otherSpace = Guid.CreateVersion7();
        otherPerson = Guid.CreateVersion7();
        await world.TransactAs(otherSpace, otherPerson, services =>
        {
            var db = services.GetRequiredService<DroverDbContext>();
            db.Users.Add(new User
            {
                Id = otherPerson,
                UserName = $"user-{otherPerson:N}",
                NormalizedUserName = $"USER-{otherPerson:N}".ToUpperInvariant(),
                Email = $"{otherPerson:N}@example.test",
                NormalizedEmail = $"{otherPerson:N}@EXAMPLE.TEST".ToUpperInvariant(),
                DisplayName = "Someone else",
            });
            db.Spaces.Add(new Space { Id = otherSpace, Kind = SpaceKind.Personal, Name = "Other", Slug = $"other-{otherSpace:N}" });
            db.Memberships.Add(new Membership { UserId = otherPerson, Role = MembershipRole.Owner });
            return Task.FromResult(true);
        });

        var connectionId = await world.TransactAs(otherSpace, otherPerson, async services =>
            (await services.GetRequiredService<ModelConnectionService>()
                .Create(ScriptedModelProvider.ConnectionKind, "Scripted", null, RunWorld.NewModelKey(), "scripted", RunWorld.Token)).Connection!.Id);

        return await world.TransactAs(otherSpace, otherPerson, async services =>
            (await services.GetRequiredService<BotService>()
                .Create(new NewBot("Elsewhere", string.Empty, string.Empty, connectionId, null, null), AuditActor.Person(otherPerson), RunWorld.Token)).Bot!.Id);
    }

    #endregion
}
