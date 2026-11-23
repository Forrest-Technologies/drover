namespace Drover.Server.Tests;

/// <summary>
/// Creating a bot beneath another through the API, over real PostgreSQL: where it lands in the tree, the caps that
/// refuse it, and the tools it starts with, which are never more than its parent holds.
/// </summary>
public sealed class BotTreeApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private ScriptedModelProvider? model;

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(new ScriptedModel()));

    #endregion

    #region Placement

    [Fact]
    public async Task A_bot_created_beneath_another_sits_one_level_below_it_with_closure_rows_to_every_bot_above()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);
        var atlas = await CreateBot(client, alice.SpaceId, connection.Id, "Atlas");
        var scout = await CreateBot(client, alice.SpaceId, connection.Id, "Scout", atlas.Id);
        var tracker = await CreateBot(client, alice.SpaceId, connection.Id, "Tracker", scout.Id);

        var listed = await GetFor<List<BotSummary>>(client, $"{Space(alice.SpaceId)}/bots");
        var read = await GetFor<BotSummary>(client, $"{Space(alice.SpaceId)}/bots/{tracker.Id}");
        var detail = await GetFor<BotDetail>(client, $"{Space(alice.SpaceId)}/bots/{tracker.Id}/definition");
        var closure = await InSpace(alice.SpaceId, alice.UserId, services =>
            services.GetRequiredService<DroverDbContext>().BotAncestors.AsNoTracking()
                .Where(row => row.DescendantBotId == tracker.Id)
                .OrderBy(row => row.Distance)
                .Select(row => new { row.AncestorBotId, row.Distance })
                .ToListAsync(Token));
        var created = (await Audit(alice, scout.Id)).Single(entry => entry.Action == "bot.created");

        atlas.ParentBotId.Should().BeNull();
        scout.ParentBotId.Should().Be(atlas.Id);
        tracker.ParentBotId.Should().Be(scout.Id);
        read.ParentBotId.Should().Be(scout.Id);
        listed.Select(bot => (bot.Name, bot.ParentBotId)).Should().BeEquivalentTo(
            [("Atlas", (Guid?)null), ("Scout", (Guid?)atlas.Id), ("Tracker", (Guid?)scout.Id)]);
        detail.Should().Match<BotDetail>(found => found.ParentBotId == scout.Id && found.Depth == 2 && found.CreatedByUserId == alice.UserId);
        closure.Should().Equal(
            new { AncestorBotId = tracker.Id, Distance = 0 },
            new { AncestorBotId = scout.Id, Distance = 1 },
            new { AncestorBotId = atlas.Id, Distance = 2 });
        created.Data.GetProperty("parent_bot_id").GetGuid().Should().Be(atlas.Id);
    }

    #endregion

    #region Refusals

    [Fact]
    public async Task A_parent_that_is_not_a_bot_of_this_space_is_refused_in_words()
    {
        var alice = await SeedPerson("alice");
        var bob = await SeedPerson("bob");
        var client = await SignIn(alice);
        var asBob = await SignIn(bob);
        var connection = await CreateConnection(client, alice.SpaceId);
        var removed = await CreateBot(client, alice.SpaceId, connection.Id, "Gone");
        (await client.Delete($"{Space(alice.SpaceId)}/bots/{removed.Id}")).EnsureSuccessStatusCode();
        var bobsConnection = await CreateConnection(asBob, bob.SpaceId);
        var bobsBot = await CreateBot(asBob, bob.SpaceId, bobsConnection.Id, "Bobs");

        var unknown = await TryCreate(client, alice.SpaceId, connection.Id, Guid.CreateVersion7());
        var fromAnotherSpace = await TryCreate(client, alice.SpaceId, connection.Id, bobsBot.Id);
        var beneathRemoved = await TryCreate(client, alice.SpaceId, connection.Id, removed.Id);

        foreach (var refused in new[] { unknown, fromAnotherSpace, beneathRemoved })
        {
            refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await Title(refused)).Should().Be("Choose a parent bot of this space.");
        }

        (await GetFor<List<BotSummary>>(client, $"{Space(alice.SpaceId)}/bots")).Should().BeEmpty("nothing was created");
        (await GetFor<List<BotSummary>>(asBob, $"{Space(bob.SpaceId)}/bots")).Should().ContainSingle("the other space is untouched");
    }

    [Fact]
    public async Task Only_owners_and_admins_create_a_bot_beneath_another()
    {
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var admin = await SeedMember(alice.SpaceId, "admin", MembershipRole.Admin);
        var client = await SignIn(alice);
        var asMember = await SignIn(member.Email, member.Password);
        var asAdmin = await SignIn(admin.Email, admin.Password);
        var connection = await CreateConnection(client, alice.SpaceId);
        var atlas = await CreateBot(client, alice.SpaceId, connection.Id, "Atlas");

        var byMember = await TryCreate(asMember, alice.SpaceId, connection.Id, atlas.Id);
        var byAdmin = await TryCreate(asAdmin, alice.SpaceId, connection.Id, atlas.Id);

        byMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        byAdmin.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_parent_at_its_own_limit_of_children_is_refused_and_a_removed_child_frees_its_place()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);
        var atlas = await CreateBot(client, alice.SpaceId, connection.Id, "Atlas");
        await SetBuildLimits(client, alice.SpaceId, atlas.Id, new BuildLimitsBody(1, null));
        var first = await CreateBot(client, alice.SpaceId, connection.Id, "First", atlas.Id);

        var second = await TryCreate(client, alice.SpaceId, connection.Id, atlas.Id);
        (await client.Delete($"{Space(alice.SpaceId)}/bots/{first.Id}")).EnsureSuccessStatusCode();
        var afterRemoval = await TryCreate(client, alice.SpaceId, connection.Id, atlas.Id);

        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(second)).Should().Be("Atlas may have at most 1 bot directly beneath it, and already has 1.");
        afterRemoval.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_bot_above_that_limits_how_deep_the_tree_goes_beneath_it_is_obeyed()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);
        var atlas = await CreateBot(client, alice.SpaceId, connection.Id, "Atlas");
        await SetBuildLimits(client, alice.SpaceId, atlas.Id, new BuildLimitsBody(null, 1));
        var scout = await CreateBot(client, alice.SpaceId, connection.Id, "Scout", atlas.Id);

        var tooDeep = await TryCreate(client, alice.SpaceId, connection.Id, scout.Id);

        tooDeep.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(tooDeep)).Should().Be("Atlas allows bots at most 1 level beneath it.");
    }

    [Fact]
    public async Task The_spaces_caps_on_depth_and_on_bots_beneath_one_bot_are_obeyed()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);
        var atlas = await CreateBot(client, alice.SpaceId, connection.Id, "Atlas");
        await SetSpaceCaps(alice.SpaceId, alice.UserId, new SpaceSettings { MaxBotDepth = 1, MaxChildrenPerBot = 2 });
        var scout = await CreateBot(client, alice.SpaceId, connection.Id, "Scout", atlas.Id);
        await CreateBot(client, alice.SpaceId, connection.Id, "Ranger", atlas.Id);

        var third = await TryCreate(client, alice.SpaceId, connection.Id, atlas.Id);
        var tooDeep = await TryCreate(client, alice.SpaceId, connection.Id, scout.Id);
        var atTheTop = await TryCreate(client, alice.SpaceId, connection.Id, null);

        third.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(third)).Should().Be("This space allows at most 2 bots directly beneath one bot, and Atlas already has 2.");
        tooDeep.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(tooDeep)).Should().Be("This space allows bots at most 1 level below the top, so Scout cannot have a bot beneath it.");
        atTheTop.StatusCode.Should().Be(HttpStatusCode.Created, "the caps are on the tree beneath a bot");
    }

    #endregion

    #region Grants

    [Fact]
    public async Task A_new_child_starts_with_no_tool_above_what_its_parent_holds()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);
        var atlas = await CreateBot(client, alice.SpaceId, connection.Id, "Atlas");
        var tool = (await GetFor<List<BotToolEntry>>(client, $"{Space(alice.SpaceId)}/bots/{atlas.Id}/tools")).Single(entry => entry.Name == "time.now");
        var atlasTool = $"{Space(alice.SpaceId)}/bots/{atlas.Id}/tools/{tool.ToolId}";

        var underAllow = await CreateBot(client, alice.SpaceId, connection.Id, "Under allow", atlas.Id);
        (await client.Put(atlasTool, new SetToolGrantRequest("ask"))).EnsureSuccessStatusCode();
        var underAsk = await CreateBot(client, alice.SpaceId, connection.Id, "Under ask", atlas.Id);
        (await client.Put(atlasTool, new SetToolGrantRequest("off"))).EnsureSuccessStatusCode();
        var underOff = await CreateBot(client, alice.SpaceId, connection.Id, "Under off", atlas.Id);
        var rows = await InSpace(alice.SpaceId, alice.UserId, services =>
            services.GetRequiredService<DroverDbContext>().ToolGrants.AsNoTracking()
                .Where(grant => grant.ToolId == tool.ToolId)
                .ToDictionaryAsync(grant => grant.BotId, grant => grant.Effect, Token));
        var askedFor = (await Audit(alice, underAsk.Id)).Single(entry => entry.Action == "bot.created");

        tool.Name.Should().Be("time.now");
        rows.Should().Contain(underAllow.Id, GrantEffect.Allow);
        rows.Should().Contain(underAsk.Id, GrantEffect.Ask, "a child is lowered to what its parent holds");
        rows.Should().NotContainKey(underOff.Id, "a tool the parent does not have is not given to the child");
        askedFor.Data.GetProperty("changes").GetProperty("grants").GetProperty("after")[0].GetProperty("effect").GetString().Should().Be("ask");
    }

    #endregion

    #region The Space's Cap On Its Bots

    [Fact]
    public async Task The_spaces_cap_on_its_bots_refuses_a_bot_at_the_top_beneath_a_parent_and_a_duplicate_and_removed_bots_do_not_count()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);
        var atlas = await CreateBot(client, alice.SpaceId, connection.Id, "Atlas");
        var scout = await CreateBot(client, alice.SpaceId, connection.Id, "Scout", atlas.Id);
        await SetSpaceCaps(alice.SpaceId, alice.UserId, new SpaceSettings { MaxBots = 2 });

        var atTheTop = await TryCreate(client, alice.SpaceId, connection.Id, null);
        var beneath = await TryCreate(client, alice.SpaceId, connection.Id, atlas.Id);
        var duplicate = await client.Post($"{Space(alice.SpaceId)}/bots/{atlas.Id}/duplicate", null);
        (await client.Delete($"{Space(alice.SpaceId)}/bots/{scout.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var afterRemoval = await TryCreate(client, alice.SpaceId, connection.Id, atlas.Id);

        foreach (var refused in new[] { atTheTop, beneath, duplicate })
        {
            refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await Title(refused)).Should().Be("This space allows at most 2 bots, and already has 2. Remove a bot to make room for another.");
        }

        afterRemoval.StatusCode.Should().Be(HttpStatusCode.Created, "a removed bot does not count");
        (await GetFor<List<BotSummary>>(client, $"{Space(alice.SpaceId)}/bots")).Select(bot => bot.Name).Should().BeEquivalentTo(["Atlas", "Child"]);
    }

    #endregion

    #region Removing

    [Fact]
    public async Task Removing_a_bot_with_bots_beneath_and_no_choice_is_refused_and_changes_nothing()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);
        var atlas = await CreateBot(client, alice.SpaceId, connection.Id, "Atlas");
        await CreateBot(client, alice.SpaceId, connection.Id, "Scout", atlas.Id);

        var unasked = await client.Delete($"{Space(alice.SpaceId)}/bots/{atlas.Id}");
        var misspelled = await client.Delete($"{Space(alice.SpaceId)}/bots/{atlas.Id}?beneath=both");

        unasked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(unasked)).Should().Be("Atlas has 1 bot directly beneath it. Choose whether they are removed with it or kept and moved up.");
        misspelled.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(misspelled)).Should().Be("Choose remove or keep for the bots beneath it.");
        (await GetFor<List<BotSummary>>(client, $"{Space(alice.SpaceId)}/bots")).Should().HaveCount(2, "nothing was removed");
    }

    [Fact]
    public async Task Removing_with_remove_takes_the_whole_branch_and_nothing_beside_it()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);
        var atlas = await CreateBot(client, alice.SpaceId, connection.Id, "Atlas");
        var scout = await CreateBot(client, alice.SpaceId, connection.Id, "Scout", atlas.Id);
        var tracker = await CreateBot(client, alice.SpaceId, connection.Id, "Tracker", scout.Id);
        var finder = await CreateBot(client, alice.SpaceId, connection.Id, "Finder", tracker.Id);
        await CreateBot(client, alice.SpaceId, connection.Id, "Ranger", atlas.Id);

        var removed = await client.Delete($"{Space(alice.SpaceId)}/bots/{scout.Id}?beneath=remove");

        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetFor<List<BotSummary>>(client, $"{Space(alice.SpaceId)}/bots")).Select(bot => bot.Name).Should().BeEquivalentTo(["Atlas", "Ranger"]);
        foreach (var below in new[] { tracker.Id, finder.Id })
        {
            (await Audit(alice, below)).Should().ContainSingle(entry => entry.Action == "bot.removed")
                .Which.Data.GetProperty("removed_with").GetGuid().Should().Be(scout.Id);
        }
    }

    [Fact]
    public async Task Removing_with_keep_moves_the_bots_beneath_up_and_rewrites_the_tree_beneath_them()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);
        var root = await CreateBot(client, alice.SpaceId, connection.Id, "Root");
        var atlas = await CreateBot(client, alice.SpaceId, connection.Id, "Atlas", root.Id);
        var scout = await CreateBot(client, alice.SpaceId, connection.Id, "Scout", atlas.Id);
        var tracker = await CreateBot(client, alice.SpaceId, connection.Id, "Tracker", scout.Id);

        var kept = await client.Delete($"{Space(alice.SpaceId)}/bots/{atlas.Id}?beneath=keep");

        kept.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetFor<BotDetail>(client, $"{Space(alice.SpaceId)}/bots/{scout.Id}/definition"))
            .Should().Match<BotDetail>(bot => bot.ParentBotId == root.Id && bot.Depth == 1 && bot.Status == "active");
        (await GetFor<BotDetail>(client, $"{Space(alice.SpaceId)}/bots/{tracker.Id}/definition"))
            .Should().Match<BotDetail>(bot => bot.ParentBotId == scout.Id && bot.Depth == 2);
        (await Closure(alice, tracker.Id)).Should().Equal((tracker.Id, 0), (scout.Id, 1), (root.Id, 2));
        (await Closure(alice, scout.Id)).Should().Equal((scout.Id, 0), (root.Id, 1));
        (await Audit(alice, scout.Id)).Should().ContainSingle(entry => entry.Action == "bot.moved");
        (await GetFor<List<BotSummary>>(client, $"{Space(alice.SpaceId)}/bots")).Select(bot => bot.Name).Should().BeEquivalentTo(["Root", "Scout", "Tracker"]);
    }

    [Fact]
    public async Task Bots_kept_from_beneath_a_removed_bot_and_moved_to_the_top_can_do_no_more_than_before()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);
        var atlas = await CreateBot(client, alice.SpaceId, connection.Id, "Atlas");
        var tools = await GetFor<List<BotToolEntry>>(client, $"{Space(alice.SpaceId)}/bots/{atlas.Id}/tools");
        var time = tools.Single(entry => entry.Name == "time.now").ToolId;
        var list = tools.Single(entry => entry.Name == "crew.list").ToolId;
        var scout = await CreateBot(client, alice.SpaceId, connection.Id, "Scout", atlas.Id);
        var tracker = await CreateBot(client, alice.SpaceId, connection.Id, "Tracker", scout.Id);
        await SetGrant(client, alice.SpaceId, scout.Id, list, "ask");
        await SetGrant(client, alice.SpaceId, atlas.Id, time, "ask");
        await SetGrant(client, alice.SpaceId, atlas.Id, list, "off");

        var kept = await client.Delete($"{Space(alice.SpaceId)}/bots/{atlas.Id}?beneath=keep");
        var scoutTools = await GetFor<List<BotToolEntry>>(client, $"{Space(alice.SpaceId)}/bots/{scout.Id}/tools");
        var effective = await InSpace(alice.SpaceId, alice.UserId, services =>
            services.GetRequiredService<ToolRepository>().ListEffectiveGrants(tracker.Id, Token));

        kept.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetFor<BotDetail>(client, $"{Space(alice.SpaceId)}/bots/{scout.Id}/definition"))
            .Should().Match<BotDetail>(bot => bot.ParentBotId == null && bot.Depth == 0);
        (await Closure(alice, tracker.Id)).Should().Equal((tracker.Id, 0), (scout.Id, 1));
        scoutTools.Single(entry => entry.ToolId == time).Grant.Should().Be("ask", "it was lowered to what Atlas could do");
        scoutTools.Single(entry => entry.ToolId == list).Grant.Should().Be("off", "Atlas could not use it, so it was taken away");
        effective.Should().Equal(new Dictionary<Guid, GrantEffect> { [time] = GrantEffect.Ask });
        (await Audit(alice, scout.Id)).Where(entry => entry.Action == "grant.changed" && entry.ActorId == alice.UserId).Should().HaveCount(3,
            "the person's own change and the two the move made are each recorded");
    }

    [Fact]
    public async Task Bots_kept_from_beneath_a_removed_bot_keep_its_rules_as_their_own()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);
        var space = Space(alice.SpaceId);
        var atlas = await CreateBot(client, alice.SpaceId, connection.Id, "Atlas");
        var scout = await CreateBot(client, alice.SpaceId, connection.Id, "Scout", atlas.Id);
        var original = await PostFor<BotRuleResponse>(
            client,
            $"{space}/bots/{atlas.Id}/rules",
            BlockRule("Block mail"),
            HttpStatusCode.Created);

        var kept = await client.Delete($"{space}/bots/{atlas.Id}?beneath=keep");
        var scoutRules = await GetFor<List<BotRuleResponse>>(client, $"{space}/bots/{scout.Id}/rules");

        kept.StatusCode.Should().Be(HttpStatusCode.NoContent);
        scoutRules.Should().ContainSingle().Which.Should().Match<BotRuleResponse>(rule =>
            !rule.Inherited && rule.BotId == scout.Id && rule.BotName == "Scout" && rule.Name == original.Name
            && rule.Enabled && rule.Position == 0 && rule.Checkpoint == original.Checkpoint
            && rule.Effect == original.Effect);
        scoutRules[0].Id.Should().NotBe(original.Id, "the moved bot holds a copy, not the removed bot's row");
    }

    [Fact]
    public async Task A_bot_two_levels_down_still_follows_the_removed_bots_rule_without_a_copy()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);
        var space = Space(alice.SpaceId);
        var atlas = await CreateBot(client, alice.SpaceId, connection.Id, "Atlas");
        var scout = await CreateBot(client, alice.SpaceId, connection.Id, "Scout", atlas.Id);
        var tracker = await CreateBot(client, alice.SpaceId, connection.Id, "Tracker", scout.Id);
        await PostFor<BotRuleResponse>(
            client,
            $"{space}/bots/{atlas.Id}/rules",
            BlockRule("Block mail"),
            HttpStatusCode.Created);

        var kept = await client.Delete($"{space}/bots/{atlas.Id}?beneath=keep");
        var scoutRules = await GetFor<List<BotRuleResponse>>(client, $"{space}/bots/{scout.Id}/rules");
        var trackerRules = await GetFor<List<BotRuleResponse>>(client, $"{space}/bots/{tracker.Id}/rules");

        kept.StatusCode.Should().Be(HttpStatusCode.NoContent);
        scoutRules.Should().ContainSingle().Which.Should().Match<BotRuleResponse>(rule =>
            !rule.Inherited && rule.BotName == "Scout");
        trackerRules.Should().ContainSingle().Which.Should().Match<BotRuleResponse>(rule =>
            rule.Inherited && rule.BotName == "Scout" && rule.Id == scoutRules[0].Id,
            "a bot two levels down inherits the copy on the bot it moved beneath");
    }

    [Fact]
    public async Task Switched_off_rules_and_rules_from_further_up_are_not_copied()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);
        var space = Space(alice.SpaceId);
        var root = await CreateBot(client, alice.SpaceId, connection.Id, "Root");
        var atlas = await CreateBot(client, alice.SpaceId, connection.Id, "Atlas", root.Id);
        var scout = await CreateBot(client, alice.SpaceId, connection.Id, "Scout", atlas.Id);
        await PostFor<BotRuleResponse>(client, $"{space}/bots/{root.Id}/rules",
            BlockRule("From above"), HttpStatusCode.Created);
        var on = await PostFor<BotRuleResponse>(client, $"{space}/bots/{atlas.Id}/rules",
            BlockRule("On Atlas"), HttpStatusCode.Created);
        await PostFor<BotRuleResponse>(client, $"{space}/bots/{atlas.Id}/rules",
            BlockRule("Off on Atlas", false), HttpStatusCode.Created);

        var kept = await client.Delete($"{space}/bots/{atlas.Id}?beneath=keep");
        var scoutRules = await GetFor<List<BotRuleResponse>>(client, $"{space}/bots/{scout.Id}/rules");

        kept.StatusCode.Should().Be(HttpStatusCode.NoContent);
        scoutRules.Select(rule => (rule.Name, rule.Inherited, rule.BotName)).Should().Equal(
            ("From above", true, "Root"),
            ("On Atlas", false, "Scout"));
        scoutRules[1].Id.Should().NotBe(on.Id);
        scoutRules.Should().NotContain(rule => rule.Name == "Off on Atlas",
            "a rule switched off bound nothing before the move");
    }

    [Fact]
    public async Task A_keep_that_takes_a_moved_bot_past_fifty_rules_still_succeeds_and_keeps_every_rule()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);
        var space = Space(alice.SpaceId);
        var atlas = await CreateBot(client, alice.SpaceId, connection.Id, "Atlas");
        var scout = await CreateBot(client, alice.SpaceId, connection.Id, "Scout", atlas.Id);

        for (var number = 0; number < 10; number++)
        {
            await PostFor<BotRuleResponse>(client, $"{space}/bots/{atlas.Id}/rules",
                BlockRule($"Atlas {number}"), HttpStatusCode.Created);
        }

        for (var number = 0; number < 45; number++)
        {
            await PostFor<BotRuleResponse>(client, $"{space}/bots/{scout.Id}/rules",
                BlockRule($"Scout {number}"), HttpStatusCode.Created);
        }

        var kept = await client.Delete($"{space}/bots/{atlas.Id}?beneath=keep");
        var scoutRules = await GetFor<List<BotRuleResponse>>(client, $"{space}/bots/{scout.Id}/rules");
        var over = await client.Post($"{space}/bots/{scout.Id}/rules", BlockRule("One too many"));
        string[] expected =
        [
            .. Enumerable.Range(0, 10).Select(number => $"Atlas {number}"),
            .. Enumerable.Range(0, 45).Select(number => $"Scout {number}"),
        ];

        kept.StatusCode.Should().Be(HttpStatusCode.NoContent);
        scoutRules.Should().HaveCount(55);
        scoutRules.Select(rule => rule.Name).Should().Equal(expected, "copies come first in the removed bot's order");
        scoutRules.Select(rule => rule.Position).Should().Equal(Enumerable.Range(0, 55));
        over.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(over)).Should().Be("A bot has at most 50 rules of its own. Remove one first.");
    }

    [Fact]
    public async Task Each_copied_rule_is_an_audit_entry_that_names_the_rule_and_the_removed_bot()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);
        var space = Space(alice.SpaceId);
        var atlas = await CreateBot(client, alice.SpaceId, connection.Id, "Atlas");
        var scout = await CreateBot(client, alice.SpaceId, connection.Id, "Scout", atlas.Id);
        var keptRule = await PostFor<BotRuleResponse>(client, $"{space}/bots/{atlas.Id}/rules",
            BlockRule("Keep this one"), HttpStatusCode.Created);
        await PostFor<BotRuleResponse>(client, $"{space}/bots/{atlas.Id}/rules",
            BlockRule("Drop this one", false), HttpStatusCode.Created);

        (await client.Delete($"{space}/bots/{atlas.Id}?beneath=keep")).EnsureSuccessStatusCode();
        var copy = (await GetFor<List<BotRuleResponse>>(client, $"{space}/bots/{scout.Id}/rules")).Single();
        var entries = (await Audit(alice, scout.Id)).Where(entry => entry.Action == "rule.added").ToList();

        entries.Should().ContainSingle().Which.Should().Match<AuditLogEntry>(entry =>
            entry.ActorId == alice.UserId && entry.TargetKind == AuditTargets.Bot && entry.TargetId == scout.Id);
        entries[0].Data.GetProperty("rule_id").GetGuid().Should().Be(copy.Id);
        entries[0].Data.GetProperty("copied_from_rule_id").GetGuid().Should().Be(keptRule.Id);
        entries[0].Data.GetProperty("removed_bot_id").GetGuid().Should().Be(atlas.Id);
        entries[0].Data.GetProperty("changes").GetProperty("name").GetProperty("after").GetString()
            .Should().Be("Keep this one");
    }

    [Fact]
    public async Task Keeping_more_bots_than_the_parent_has_room_for_is_refused_whole()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);
        var root = await CreateBot(client, alice.SpaceId, connection.Id, "Root");
        var atlas = await CreateBot(client, alice.SpaceId, connection.Id, "Atlas", root.Id);
        await CreateBot(client, alice.SpaceId, connection.Id, "Other", root.Id);
        var first = await CreateBot(client, alice.SpaceId, connection.Id, "First", atlas.Id);
        await CreateBot(client, alice.SpaceId, connection.Id, "Second", atlas.Id);
        await SetBuildLimits(client, alice.SpaceId, root.Id, new BuildLimitsBody(2, null));

        var kept = await client.Delete($"{Space(alice.SpaceId)}/bots/{atlas.Id}?beneath=keep");

        kept.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(kept)).Should().Be("Root may have at most 2 bots directly beneath it, so the 2 bots beneath Atlas cannot move up to it. Remove them with Atlas, or remove some of them first.");
        (await GetFor<BotDetail>(client, $"{Space(alice.SpaceId)}/bots/{first.Id}/definition")).ParentBotId.Should().Be(atlas.Id);
        (await GetFor<List<BotSummary>>(client, $"{Space(alice.SpaceId)}/bots")).Should().HaveCount(5, "nothing was removed or moved");
    }

    [Fact]
    public async Task A_bot_with_nothing_beneath_it_is_removed_without_a_choice_through_the_client()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);
        var atlas = await CreateBot(client, alice.SpaceId, connection.Id, "Atlas");
        var scout = await CreateBot(client, alice.SpaceId, connection.Id, "Scout", atlas.Id);
        var tracker = await CreateBot(client, alice.SpaceId, connection.Id, "Tracker", scout.Id);
        var api = new DroverApi(NewRelay(client)) { AntiforgeryToken = client.Cookie("XSRF-TOKEN") };

        await api.RemoveBot(alice.SpaceId, tracker.Id, cancellationToken: Token);
        var unasked = async () => await api.RemoveBot(alice.SpaceId, atlas.Id, cancellationToken: Token);
        var refused = await unasked.Should().ThrowAsync<DroverApiException>();
        await api.RemoveBot(alice.SpaceId, atlas.Id, removeBeneath: false, Token);

        refused.Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        refused.Which.Message.Should().Be("Atlas has 1 bot directly beneath it. Choose whether they are removed with it or kept and moved up.");
        (await GetFor<List<BotSummary>>(client, $"{Space(alice.SpaceId)}/bots")).Select(bot => bot.Name).Should().Equal("Scout");
    }

    #endregion

    #region Helpers

    private static BotRuleBody BlockRule(string name, bool enabled = true) =>
        new(name, enabled, "before_tool", "block", [new RuleConditionBody("tool_is") { Tools = ["time.now"] }]);

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    private Task<ApiClient> SignIn((string Email, string Password, Guid UserId, Guid SpaceId) person) => SignIn(person.Email, person.Password);

    private async Task<ApiClient> SignIn(string email, string password)
    {
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    private static Task<ModelConnectionSummary> CreateConnection(ApiClient client, Guid spaceId) =>
        PostFor<ModelConnectionSummary>(
            client,
            $"{Space(spaceId)}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted"),
            HttpStatusCode.Created);

    private static Task<BotSummary> CreateBot(ApiClient client, Guid spaceId, Guid connectionId, string name, Guid? parentBotId = null) =>
        PostFor<BotSummary>(client, $"{Space(spaceId)}/bots", new CreateBotRequest(name, "Answer briefly.", connectionId, null, parentBotId), HttpStatusCode.Created);

    private static Task<HttpResponseMessage> TryCreate(ApiClient client, Guid spaceId, Guid connectionId, Guid? parentBotId) =>
        client.Post($"{Space(spaceId)}/bots", new CreateBotRequest("Child", "Answer briefly.", connectionId, null, parentBotId));

    private static async Task SetBuildLimits(ApiClient client, Guid spaceId, Guid botId, BuildLimitsBody limits)
    {
        var edit = new UpdateBotRequest(null, null, null, null, null, null, null, null, null, null, null, null, limits);
        (await client.Put($"{Space(spaceId)}/bots/{botId}/definition", edit)).EnsureSuccessStatusCode();
    }

    /// <summary>The space's own caps on the tree. No route sets them yet; an owner will, through the space settings.</summary>
    private Task<int> SetSpaceCaps(Guid spaceId, Guid userId, SpaceSettings settings) =>
        InSpace(spaceId, userId, async services =>
        {
            var db = services.GetRequiredService<DroverDbContext>();
            var space = await db.Spaces.SingleAsync(found => found.Id == spaceId, Token);
            space.Settings = settings;
            return await db.SaveChangesAsync(Token);
        });

    private static async Task SetGrant(ApiClient client, Guid spaceId, Guid botId, Guid toolId, string grant) =>
        (await client.Put($"{Space(spaceId)}/bots/{botId}/tools/{toolId}", new SetToolGrantRequest(grant))).EnsureSuccessStatusCode();

    /// <summary>The bot's closure rows, nearest first: each bot above it, and itself at distance 0.</summary>
    private async Task<List<(Guid, int)>> Closure((string Email, string Password, Guid UserId, Guid SpaceId) person, Guid botId) =>
        [
            .. (await InSpace(person.SpaceId, person.UserId, services =>
                services.GetRequiredService<DroverDbContext>().BotAncestors.AsNoTracking()
                    .Where(row => row.DescendantBotId == botId)
                    .OrderBy(row => row.Distance)
                    .Select(row => new { row.AncestorBotId, row.Distance })
                    .ToListAsync(Token)))
                .Select(row => (row.AncestorBotId, row.Distance)),
        ];

    private Task<List<AuditLogEntry>> Audit((string Email, string Password, Guid UserId, Guid SpaceId) person, Guid botId) =>
        InSpace(person.SpaceId, person.UserId, services => services.GetRequiredService<AuditRepository>().ListEntriesAbout(botId, Token));

    private async Task<T> InSpace<T>(Guid spaceId, Guid userId, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var tenant = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var result = await work(scope.ServiceProvider);
        await tenant.Commit(Token);
        return result;
    }

    private static async Task<string?> Title(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString();

    private static async Task<T> PostFor<T>(ApiClient client, string path, object body, HttpStatusCode expected)
    {
        var response = await client.Post(path, body);
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private static async Task<T> GetFor<T>(ApiClient client, string path)
    {
        var response = await client.Get(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    #endregion
}
