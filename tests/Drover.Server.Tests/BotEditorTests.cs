namespace Drover.Server.Tests;

/// <summary>
/// The bot editor's API over real PostgreSQL: every part of a bot's definition, its tools and their grants, its
/// schedules, who may change them, and what the audit log keeps.
/// </summary>
public sealed class BotEditorTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    // Columns the server sets and no person edits, each with the reason. Everything else on a bot must be editable.
    private static readonly Dictionary<string, string> serverOwned = new()
    {
        [nameof(Bot.Id)] = "identity",
        [nameof(Bot.SpaceId)] = "the tenant, stamped by the database layer",
        [nameof(Bot.ParentBotId)] = "the tree changes only when a bot builds a bot, which is not part of the editor",
        [nameof(Bot.Depth)] = "follows the parent",
        [nameof(Bot.CreatedByUserId)] = "provenance",
        [nameof(Bot.CreatedByBotId)] = "provenance",
        [nameof(Bot.Status)] = "changed by pause, resume, and remove, which have routes of their own",
    };

    private ScriptedModel script = new();

    private ScriptedModelProvider? model;

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(script));

    #endregion

    #region Definition

    [Fact]
    public void Every_column_of_a_bot_has_a_path_through_the_api()
    {
        var columns = typeof(Bot).GetProperties().Select(property => property.Name).ToList();
        var editable = columns.Except(serverOwned.Keys).ToList();
        var request = typeof(UpdateBotRequest).GetProperties().Select(property => property.Name).ToList();
        var detail = typeof(BotDetail).GetProperties().Select(property => property.Name).ToList();

        serverOwned.Keys.Should().BeSubsetOf(columns, "a column that is gone needs no excuse");
        request.Should().Contain(editable, "every column a person may set is in the update request");
        detail.Should().Contain(columns.Except([nameof(Bot.SpaceId)]), "every column can be read back");
        Names<ModelSettingsBody>().Should().BeEquivalentTo(Names<ModelSettings>());
        Names<MemorySettingsBody>().Should().BeEquivalentTo(Names<MemorySettings>());
        Names<SandboxSettingsBody>().Should().BeEquivalentTo(Names<SandboxSettings>());
        Names<BuildLimitsBody>().Should().BeEquivalentTo(Names<BuildLimits>());
        Names<BotAvatarBody>().Should().BeEquivalentTo(Names<BotAvatar>());
    }

    [Fact]
    public async Task Every_editable_part_of_a_bot_is_stored_and_read_back()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var bot = await CreateBot(client, alice.SpaceId);
        var other = await CreateBot(client, alice.SpaceId, "Other");
        var second = await CreateConnection(client, alice.SpaceId, "Second");
        var fileId = await SeedFile(alice.SpaceId, alice.UserId);
        var untouched = await GetFor<BotDetail>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/definition");
        var edit = new UpdateBotRequest(
            "Atlas",
            "atlas-prime",
            new BotAvatarBody("A1", fileId),
            "Plans trips and books nothing.",
            "Answer in one paragraph.",
            second.Id,
            "other-model",
            new ModelSettingsBody(0.4, 900, "high", "automatic", true),
            new MemorySettingsBody(true, "summarization", 60_000, 12),
            "named",
            new SandboxSettingsBody("allowlist", ["Example.com", "api.example.org"], 30),
            [other.Id],
            new BuildLimitsBody(3, 2));

        var saved = await PutFor<BotDetail>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/definition", edit);
        var read = await GetFor<BotDetail>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/definition");

        read.Should().BeEquivalentTo(saved);
        read.Should().BeEquivalentTo(
            new
            {
                edit.Name,
                edit.Handle,
                edit.Avatar,
                edit.DescriptionForBots,
                edit.Instructions,
                edit.ModelConnectionId,
                edit.Model,
                edit.ModelSettings,
                edit.MemorySettings,
                edit.SandboxPolicy,
                SandboxSettings = new SandboxSettingsBody("allowlist", ["example.com", "api.example.org"], 30),
                edit.SandboxSharedWith,
                edit.BuildLimits,
                Status = "active",
                Depth = 0,
                CreatedByUserId = (Guid?)alice.UserId,
            });

        // Each part differs from what a new bot holds, so a part that was silently dropped fails here.
        foreach (var part in typeof(UpdateBotRequest).GetProperties())
        {
            var readPart = typeof(BotDetail).GetProperty(part.Name)!;
            JsonSerializer.Serialize(readPart.GetValue(read)).Should().NotBe(JsonSerializer.Serialize(readPart.GetValue(untouched)), $"{part.Name} was edited");
        }
    }

    [Fact]
    public async Task A_part_left_out_of_an_update_keeps_its_value()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var bot = await CreateBot(client, alice.SpaceId);

        var saved = await PutFor<BotDetail>(
            client,
            $"{Space(alice.SpaceId)}/bots/{bot.Id}/definition",
            new UpdateBotRequest("Renamed", null, null, null, null, null, null, null, null, null, null, null, null));

        saved.Should().Match<BotDetail>(found => found.Name == "Renamed" && found.Handle == bot.Handle && found.Instructions == bot.Instructions && found.Model == bot.Model);
    }

    [Theory]
    [MemberData(nameof(RefusedEdits))]
    public async Task An_edit_that_does_not_hold_is_refused_in_words_and_nothing_changes(string what, UpdateBotRequest edit, string expected)
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var bot = await CreateBot(client, alice.SpaceId);
        await CreateBot(client, alice.SpaceId, "Taken");
        await SeedCatalogEntry(alice.SpaceId, alice.UserId, bot.ModelConnectionId, "plain", new ModelCapabilities { MaxOutputTokens = 500 });

        var refused = await client.Put($"{Space(alice.SpaceId)}/bots/{bot.Id}/definition", edit);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(Token);
        var after = await GetFor<BotDetail>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/definition");

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest, what);
        problem.GetProperty("title").GetString().Should().Contain(expected, what);
        after.Should().Match<BotDetail>(found => found.Name == "Helper" && found.Handle == "helper" && found.Model == "scripted", "a refused edit stores nothing");
    }

    public static TheoryData<string, UpdateBotRequest, string> RefusedEdits()
    {
        var nothing = new UpdateBotRequest(null, null, null, null, null, null, null, null, null, null, null, null, null);
        return new()
        {
            { "blank name", nothing with { Name = " " }, "Give the bot a name" },
            { "handle with a space", nothing with { Handle = "not a handle" }, "A handle is" },
            { "handle in use", nothing with { Handle = "taken" }, "already uses the handle taken" },
            { "unknown connection", nothing with { ModelConnectionId = Guid.CreateVersion7() }, "model connection of this space" },
            { "unknown avatar file", nothing with { Avatar = new BotAvatarBody(null, Guid.CreateVersion7()) }, "not a file of this space" },
            { "temperature out of range", nothing with { ModelSettings = new ModelSettingsBody(3, null, null, "off", false) }, "Temperature is between" },
            { "misspelled web search", nothing with { ModelSettings = new ModelSettingsBody(null, null, null, "sometimes", false) }, "Web search is" },
            { "answer longer than the model writes", nothing with { Name = "Kept", Model = "plain", ModelSettings = new ModelSettingsBody(null, 501, null, "off", false) }, "at most 500 tokens" },
            { "effort on a model without one", nothing with { Model = "plain", ModelSettings = new ModelSettingsBody(null, null, "high", "off", false) }, "no reasoning effort" },
            { "web search on a model without it", nothing with { Model = "plain", ModelSettings = new ModelSettingsBody(null, null, null, "on", false) }, "no built-in web search" },
            { "structured output on a model without it", nothing with { Model = "plain", ModelSettings = new ModelSettingsBody(null, null, null, "off", true) }, "structured output" },
            { "misspelled compaction", nothing with { MemorySettings = new MemorySettingsBody(true, "squash", null, null) }, "how long threads are kept short" },
            { "negative threshold", nothing with { MemorySettings = new MemorySettingsBody(true, "truncation", -1, null) }, "above zero" },
            { "misspelled sandbox", nothing with { SandboxPolicy = "public" }, "A sandbox is" },
            { "host with a scheme", nothing with { SandboxSettings = new SandboxSettingsBody("allowlist", ["https://example.com"], null) }, "is not a host name" },
            { "shared with a stranger", nothing with { SandboxPolicy = "named", SandboxSharedWith = [Guid.CreateVersion7()] }, "only with bots of this space" },
            { "negative build limit", nothing with { BuildLimits = new BuildLimitsBody(-1, null) }, "build limit" },
            { "endless reasoning effort on a model nobody knows", nothing with { ModelSettings = new ModelSettingsBody(null, null, new string('x', 2_000_000), "off", false) }, "reasoning effort" },
            { "an answer longer than any model writes", nothing with { ModelSettings = new ModelSettingsBody(null, int.MaxValue, null, "off", false) }, "longest answer" },
            { "a threshold larger than any context", nothing with { MemorySettings = new MemorySettingsBody(true, "truncation", int.MaxValue, null) }, "at most" },
            { "more kept messages than a thread holds", nothing with { MemorySettings = new MemorySettingsBody(true, "truncation", null, int.MaxValue) }, "at most" },
            { "a host that is nothing", nothing with { SandboxSettings = new SandboxSettingsBody("allowlist", [null!], null) }, "is not a host name" },
            { "a blank host", nothing with { SandboxSettings = new SandboxSettingsBody("allowlist", [" "], null) }, "is not a host name" },
        };
    }

    #endregion

    #region Lifecycle

    [Fact]
    public async Task A_bot_is_duplicated_paused_resumed_and_removed_and_its_row_stays()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var bot = await CreateBot(client, alice.SpaceId);
        await PutFor<BotDetail>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/definition", Nothing() with { DescriptionForBots = "Looks things up." });
        await client.Post($"{Space(alice.SpaceId)}/bots/{bot.Id}/schedules", new BotScheduleBody("0 9 * * *", "UTC", "Morning report", "new_thread", true));

        var copy = await PostFor<BotDetail>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/duplicate", HttpStatusCode.Created);
        var copyTools = await GetFor<List<BotToolEntry>>(client, $"{Space(alice.SpaceId)}/bots/{copy.Id}/tools");
        var copySchedules = await GetFor<List<BotScheduleResponse>>(client, $"{Space(alice.SpaceId)}/bots/{copy.Id}/schedules");
        var paused = await PostFor<BotDetail>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/pause", HttpStatusCode.OK);
        var resumed = await PostFor<BotDetail>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/resume", HttpStatusCode.OK);
        var removed = await client.Delete($"{Space(alice.SpaceId)}/bots/{bot.Id}");
        var gone = await client.Get($"{Space(alice.SpaceId)}/bots/{bot.Id}/definition");
        var again = await client.Delete($"{Space(alice.SpaceId)}/bots/{bot.Id}");
        var listed = await GetFor<List<BotSummary>>(client, $"{Space(alice.SpaceId)}/bots");
        var stored = await InSpace(alice.SpaceId, alice.UserId, services =>
            services.GetRequiredService<DroverDbContext>().Bots.AsNoTracking().SingleAsync(row => row.Id == bot.Id, Token));

        copy.Should().Match<BotDetail>(found => found.Name == "Helper copy" && found.Handle == "helper-copy" && found.DescriptionForBots == "Looks things up." && found.Status == "active" && found.ParentBotId == null);
        copyTools.Should().ContainSingle(tool => tool.Name == "time.now").Which.Grant.Should().Be("allow", "the copy can use what the source could");
        copySchedules.Should().BeEmpty("a schedule is not copied");
        paused.Status.Should().Be("paused");
        resumed.Status.Should().Be("active");
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);
        again.StatusCode.Should().Be(HttpStatusCode.NotFound);
        listed.Should().ContainSingle(found => found.Id == copy.Id);
        stored.Status.Should().Be(Contracts.Bots.BotStatus.Removed, "removing a bot keeps its row and its history");
    }

    [Fact]
    public async Task Removing_a_bot_ends_the_sandbox_shares_with_it_and_the_bots_that_shared_stay_saveable()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var keeper = await CreateBot(client, alice.SpaceId, "Keeper");
        var leaver = await CreateBot(client, alice.SpaceId, "Leaver");
        var definition = $"{Space(alice.SpaceId)}/bots/{keeper.Id}/definition";
        await PutFor<BotDetail>(client, definition, Nothing() with { SandboxPolicy = "named", SandboxSharedWith = [leaver.Id] });

        (await client.Delete($"{Space(alice.SpaceId)}/bots/{leaver.Id}")).EnsureSuccessStatusCode();
        var read = await GetFor<BotDetail>(client, definition);
        var renamed = await client.Put(definition, Nothing() with { Name = "Still here" });
        var whole = await client.Put(definition, new UpdateBotRequest(
            "Still here", read.Handle, read.Avatar, read.DescriptionForBots, read.Instructions, read.ModelConnectionId, read.Model,
            read.ModelSettings, read.MemorySettings, read.SandboxPolicy, read.SandboxSettings, read.SandboxSharedWith, read.BuildLimits));

        read.SandboxSharedWith.Should().BeEmpty("a removed bot shares nothing");
        renamed.StatusCode.Should().Be(HttpStatusCode.OK, await renamed.Content.ReadAsStringAsync(Token));
        whole.StatusCode.Should().Be(HttpStatusCode.OK, "the editor sends back what it read");
    }

    #endregion

    #region Access

    [Fact]
    public async Task Only_owners_and_admins_reach_the_editor_and_another_space_sees_nothing()
    {
        var alice = await SeedPerson("alice");
        var outsider = await SeedPerson("outsider");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var admin = await SeedMember(alice.SpaceId, "admin", MembershipRole.Admin);
        var asOwner = await SignIn(alice);
        var asMember = await SignIn(member.Email, member.Password);
        var asAdmin = await SignIn(admin.Email, admin.Password);
        var asOutsider = await SignIn(outsider);
        var bot = await CreateBot(asOwner, alice.SpaceId);
        var tool = (await GetFor<List<BotToolEntry>>(asOwner, $"{Space(alice.SpaceId)}/bots/{bot.Id}/tools")).Single();
        var schedule = await PostFor<BotScheduleResponse>(asOwner, $"{Space(alice.SpaceId)}/bots/{bot.Id}/schedules", new BotScheduleBody("0 9 * * *", "UTC", "Report", "new_thread", true), HttpStatusCode.Created);
        var root = $"{Space(alice.SpaceId)}/bots/{bot.Id}";
        var body = new BotScheduleBody("0 9 * * *", "UTC", "Report", "new_thread", true);

        List<(string What, Func<ApiClient, string, Task<HttpResponseMessage>> Call)> routes =
        [
            ("read the definition", (client, at) => client.Get($"{at}/definition")),
            ("change the definition", (client, at) => client.Put($"{at}/definition", Nothing() with { Name = "Taken over" })),
            ("list tools", (client, at) => client.Get($"{at}/tools")),
            ("set a grant", (client, at) => client.Put($"{at}/tools/{tool.ToolId}", new SetToolGrantRequest("ask"))),
            ("list schedules", (client, at) => client.Get($"{at}/schedules")),
            ("add a schedule", (client, at) => client.Post($"{at}/schedules", body)),
            ("change a schedule", (client, at) => client.Put($"{at}/schedules/{schedule.Id}", body)),
            ("remove a schedule", (client, at) => client.Delete($"{at}/schedules/{schedule.Id}")),
            ("duplicate", (client, at) => client.Post($"{at}/duplicate")),
            ("pause", (client, at) => client.Post($"{at}/pause")),
            ("resume", (client, at) => client.Post($"{at}/resume")),
            ("remove", (client, at) => client.Delete(at)),
        ];

        foreach (var (what, call) in routes)
        {
            (await call(asMember, root)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"a member may not {what}");
            (await call(asOutsider, root)).StatusCode.Should().Be(HttpStatusCode.NotFound, $"a person outside the space may not {what}");
            (await call(asOutsider, $"{Space(outsider.SpaceId)}/bots/{bot.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound, $"a bot id from another space finds nothing to {what}");
        }

        var unsigned = await asOwner.PostWithoutToken($"{root}/pause");
        var after = await GetFor<BotDetail>(asAdmin, $"{root}/definition");
        var byAdmin = await asAdmin.Put($"{root}/definition", Nothing() with { Name = "Admin was here" });

        unsigned.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a state-changing request needs the antiforgery token");
        after.Should().Match<BotDetail>(found => found.Name == "Helper" && found.Status == "active", "no refused request changed the bot");
        byAdmin.StatusCode.Should().Be(HttpStatusCode.OK, "an admin edits bots");
    }

    #endregion

    #region Tools

    [Fact]
    public async Task A_new_space_already_holds_the_built_in_tools_as_rows()
    {
        var (_, session, _) = await SetUpFirstAccount();
        var spaceId = session.Spaces[0].Id;

        var tools = await InSpace(spaceId, session.UserId, services =>
            services.GetRequiredService<DroverDbContext>().Tools.AsNoTracking().ToListAsync(Token));

        tools.Should().ContainSingle(tool => tool.Name == "time.now" && tool.SourceKind == ToolSourceKind.Builtin);
    }

    [Fact]
    public async Task A_tool_set_to_off_is_not_in_the_request_the_runtime_sends_to_the_model()
    {
        script = new ScriptedModel([new TextOutput("First.")], [new TextOutput("Second.")]);
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var bot = await CreateBot(client, alice.SpaceId);
        var tool = (await GetFor<List<BotToolEntry>>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/tools")).Single();
        await Ask(client, alice.SpaceId, bot.Id, "With the tool");

        var off = await PutFor<BotToolEntry>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/tools/{tool.ToolId}", new SetToolGrantRequest("off"));
        var listed = (await GetFor<List<BotToolEntry>>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/tools")).Single();
        await Ask(client, alice.SpaceId, bot.Id, "Without the tool");
        var misspelled = await client.Put($"{Space(alice.SpaceId)}/bots/{bot.Id}/tools/{tool.ToolId}", new SetToolGrantRequest("maybe"));
        var unknown = await client.Put($"{Space(alice.SpaceId)}/bots/{bot.Id}/tools/{Guid.CreateVersion7()}", new SetToolGrantRequest("allow"));

        tool.Should().Match<BotToolEntry>(found => found.Name == "time.now" && found.Title == "Current time" && found.Source == "builtin" && found.Grant == "allow" && found.Ceiling == "allow" && found.Sensitivity == "read");
        off.Grant.Should().Be("off");
        listed.Grant.Should().Be("off");
        model!.ToolsOffered[0].Should().BeEquivalentTo(["time_now"], "the granted tool is sent to the model");
        model.ToolsOffered[1].Should().BeEmpty("a tool with no grant is never sent to the model");
        misspelled.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_grant_changed_during_a_run_takes_effect_on_its_next_tool_call()
    {
        script = new ScriptedModel([new ToolCallOutput("c1", "time_now")], [new TextOutput("I may not.")]);
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var bot = await CreateBot(client, alice.SpaceId);
        var tool = (await GetFor<List<BotToolEntry>>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/tools")).Single();
        _ = Services;

        // The run has assembled its tools and is at the model when a person turns the tool off in the editor.
        model!.BeforeCall = async call =>
        {
            if (call == 0)
            {
                (await client.Put($"{Space(alice.SpaceId)}/bots/{bot.Id}/tools/{tool.ToolId}", new SetToolGrantRequest("off"))).EnsureSuccessStatusCode();
            }
        };

        var thread = await Ask(client, alice.SpaceId, bot.Id, "What time is it?");
        var parts = (await GetFor<List<MessageResponse>>(client, $"{Space(alice.SpaceId)}/threads/{thread}/messages")).SelectMany(message => message.Parts).ToList();

        model.ToolsOffered[0].Should().Contain("time_now", "the tool was granted when the run started");
        parts.Should().ContainSingle(part => part.Kind == "tool_result").Which.Result.Should().Contain("no grant", "the gate reads the grant again when the call is made");
    }

    [Fact]
    public async Task A_child_is_never_given_more_than_its_parent_holds()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var atlas = await CreateBot(client, alice.SpaceId, "Atlas");
        var child = await SeedChild(alice.SpaceId, alice.UserId, atlas.Id, "Scout");
        var tool = (await GetFor<List<BotToolEntry>>(client, $"{Space(alice.SpaceId)}/bots/{atlas.Id}/tools")).Single();
        var grant = $"{Space(alice.SpaceId)}/bots/{child}/tools/{tool.ToolId}";

        var asParentHolds = await client.Put(grant, new SetToolGrantRequest("allow"));
        await PutFor<BotToolEntry>(client, $"{Space(alice.SpaceId)}/bots/{atlas.Id}/tools/{tool.ToolId}", new SetToolGrantRequest("ask"));
        var listed = (await GetFor<List<BotToolEntry>>(client, $"{Space(alice.SpaceId)}/bots/{child}/tools")).Single();
        var aboveAsk = await client.Put(grant, new SetToolGrantRequest("allow"));
        var atAsk = await client.Put(grant, new SetToolGrantRequest("ask"));
        await PutFor<BotToolEntry>(client, $"{Space(alice.SpaceId)}/bots/{atlas.Id}/tools/{tool.ToolId}", new SetToolGrantRequest("off"));
        var withoutParent = await client.Put(grant, new SetToolGrantRequest("ask"));
        var turnedOff = await client.Put(grant, new SetToolGrantRequest("off"));

        asParentHolds.StatusCode.Should().Be(HttpStatusCode.OK);
        listed.Should().Match<BotToolEntry>(found => found.Grant == "allow" && found.Ceiling == "ask" && found.CeilingBotName == "Atlas");
        aboveAsk.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Title(aboveAsk)).Should().Be("Atlas has this tool set to ask. Raise it on Atlas first.");
        atAsk.StatusCode.Should().Be(HttpStatusCode.OK);
        withoutParent.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Title(withoutParent)).Should().Be("Atlas does not have this tool. Give it to Atlas first.");
        turnedOff.StatusCode.Should().Be(HttpStatusCode.OK, "a grant can always be lowered");
    }

    [Fact]
    public async Task A_changed_server_tool_is_listed_with_what_was_reviewed_and_changing_its_grant_does_not_review_it()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var bot = await CreateBot(client, alice.SpaceId);
        var toolId = await SeedChangedServerTool(alice.SpaceId, alice.UserId, bot.Id);
        var tools = $"{Space(alice.SpaceId)}/bots/{bot.Id}/tools";

        var listed = (await GetFor<List<BotToolEntry>>(client, tools)).Single(tool => tool.ToolId == toolId);
        var lowered = await PutFor<BotToolEntry>(client, $"{tools}/{toolId}", new SetToolGrantRequest("ask"));
        var raised = await PutFor<BotToolEntry>(client, $"{tools}/{toolId}", new SetToolGrantRequest("allow"));
        var effective = await InSpace(alice.SpaceId, alice.UserId, services =>
            services.GetRequiredService<ToolRepository>().FindEffectiveGrant(bot.Id, toolId, Token));

        listed.Should().Match<BotToolEntry>(found => found.Source == "connection" && found.ConnectionName == "Sample server" && found.Grant == "allow" && found.NeedsReview && found.State == "available" && found.Sensitivity == "write");
        listed.Reviewed.Should().Be(new ToolDefinitionSnapshot("Look up", "What a person read.", "{}"));
        lowered.NeedsReview.Should().BeTrue();
        raised.Should().Match<BotToolEntry>(found => found.Grant == "allow" && found.NeedsReview, "choosing a grant is not reading the changed definition");
        effective.Should().Be(GrantEffect.Ask, "a grant that waits for review asks, whatever it is set to");
    }

    [Fact]
    public async Task No_write_in_the_editor_clears_a_waiting_review_and_each_one_is_in_the_audit_log()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var bot = await CreateBot(client, alice.SpaceId);
        var other = await CreateBot(client, alice.SpaceId, "Other");
        var toolId = await SeedChangedServerTool(alice.SpaceId, alice.UserId, bot.Id);
        var tools = $"{Space(alice.SpaceId)}/bots/{bot.Id}/tools";

        await PutFor<BotToolEntry>(client, $"{tools}/{toolId}", new SetToolGrantRequest("off"));
        var again = await PutFor<BotToolEntry>(client, $"{tools}/{toolId}", new SetToolGrantRequest("allow"));
        var elsewhere = await PutFor<BotToolEntry>(client, $"{Space(alice.SpaceId)}/bots/{other.Id}/tools/{toolId}", new SetToolGrantRequest("allow"));
        var copy = await PostFor<BotDetail>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/duplicate", HttpStatusCode.Created);
        var copied = (await GetFor<List<BotToolEntry>>(client, $"{Space(alice.SpaceId)}/bots/{copy.Id}/tools")).Single(tool => tool.ToolId == toolId);
        var rows = await InSpace(alice.SpaceId, alice.UserId, services =>
            services.GetRequiredService<DroverDbContext>().ToolGrants.AsNoTracking().Where(grant => grant.ToolId == toolId).ToListAsync(Token));
        var entries = await Audit(alice, bot.Id);
        var changes = entries.Where(entry => entry.Action == "grant.changed").ToList();

        again.Should().Match<BotToolEntry>(found => found.Grant == "allow" && found.NeedsReview, "taking a grant away and giving it back is not a review");
        elsewhere.NeedsReview.Should().BeTrue("a first grant on a tool that waits for review waits too");
        copied.Should().Match<BotToolEntry>(found => found.Grant == "allow" && found.NeedsReview, "a copy carries the state of the grant it copies");
        rows.Should().HaveCount(3).And.OnlyContain(grant => grant.Effect == GrantEffect.Allow && grant.ReviewState == ReviewState.NeedsReview);
        changes.Should().HaveCount(2);
        Fact(changes[0], "effect").Should().Be(("allow", null));
        Fact(changes[0], "review_state").Should().Be(("needs_review", null));
        Fact(changes[1], "effect").Should().Be((null, "allow"));
        Fact(changes[1], "review_state").Should().Be((null, "needs_review"));
    }

    [Fact]
    public async Task The_audit_log_names_the_tools_a_new_or_copied_bot_was_given_and_a_deny_that_was_taken_away()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var bot = await CreateBot(client, alice.SpaceId);
        var tool = (await GetFor<List<BotToolEntry>>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/tools")).Single();
        var copy = await PostFor<BotDetail>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/duplicate", HttpStatusCode.Created);
        await InSpace(alice.SpaceId, alice.UserId, services =>
            services.GetRequiredService<DroverDbContext>().ToolGrants.Where(grant => grant.BotId == bot.Id).ExecuteUpdateAsync(grant => grant.SetProperty(row => row.Effect, GrantEffect.Deny), Token));

        var listed = (await GetFor<List<BotToolEntry>>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/tools")).Single();
        await PutFor<BotToolEntry>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/tools/{tool.ToolId}", new SetToolGrantRequest("off"));
        var created = (await Audit(alice, bot.Id)).Single(entry => entry.Action == "bot.created");
        var duplicated = (await Audit(alice, copy.Id)).Single(entry => entry.Action == "bot.duplicated");
        var taken = (await Audit(alice, bot.Id)).Single(entry => entry.Action == "grant.changed");

        listed.Grant.Should().Be("off", "a deny shows as off");
        foreach (var entry in new[] { created, duplicated })
        {
            var grants = entry.Data.GetProperty("changes").GetProperty("grants").GetProperty("after");
            grants.GetArrayLength().Should().Be(1);
            grants[0].GetProperty("tool").GetString().Should().Be("time.now");
            grants[0].GetProperty("effect").GetString().Should().Be("allow");
            grants[0].GetProperty("review_state").GetString().Should().Be("current");
        }

        Fact(taken, "effect").Should().Be(("deny", null), "taking a deny away is a change, though both show as off");
    }

    #endregion

    #region Schedules

    [Fact]
    public async Task A_schedule_is_stored_with_the_next_time_it_is_due_and_can_be_changed_and_removed()
    {
        Time.Advance(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero) - Time.GetUtcNow());
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var bot = await CreateBot(client, alice.SpaceId);
        var schedules = $"{Space(alice.SpaceId)}/bots/{bot.Id}/schedules";

        var created = await PostFor<BotScheduleResponse>(client, schedules, new BotScheduleBody("30 9 * * 1-5", "Europe/Berlin", "Morning report", "new_thread", true), HttpStatusCode.Created);
        var changed = await PutFor<BotScheduleResponse>(client, $"{schedules}/{created.Id}", new BotScheduleBody("0 18 * * *", "UTC", "Evening report", "same_thread", false));
        var listed = await GetFor<List<BotScheduleResponse>>(client, schedules);
        var badCron = await client.Post(schedules, new BotScheduleBody("every morning", "UTC", "Report", "new_thread", true));
        var badZone = await client.Post(schedules, new BotScheduleBody("0 9 * * *", "Mars/Olympus", "Report", "new_thread", true));
        var noPrompt = await client.Post(schedules, new BotScheduleBody("0 9 * * *", "UTC", " ", "new_thread", true));
        var badMode = await client.Post(schedules, new BotScheduleBody("0 9 * * *", "UTC", "Report", "any_thread", true));
        var removed = await client.Delete($"{schedules}/{created.Id}");
        var again = await client.Delete($"{schedules}/{created.Id}");

        created.Should().Match<BotScheduleResponse>(found => found.Cron == "30 9 * * 1-5" && found.TimeZone == "Europe/Berlin" && found.ThreadMode == "new_thread" && found.Enabled && found.OwnerUserId == alice.UserId);
        created.NextRunAt.Should().Be(new DateTimeOffset(2026, 11, 16, 8, 30, 0, TimeSpan.Zero), "Friday's 9:30 in Berlin has passed, so Monday's is next");
        changed.Should().Match<BotScheduleResponse>(found => found.Cron == "0 18 * * *" && found.Prompt == "Evening report" && found.ThreadMode == "same_thread" && !found.Enabled && found.NextRunAt == null);
        listed.Should().ContainSingle().Which.Should().BeEquivalentTo(changed);
        badCron.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(badCron)).Should().Contain("five cron fields");
        badZone.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        noPrompt.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        badMode.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        again.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await GetFor<List<BotScheduleResponse>>(client, schedules)).Should().BeEmpty();
    }

    #endregion

    #region Audit

    [Fact]
    public async Task Bot_and_grant_changes_are_in_the_audit_log_with_who_and_before_and_after_and_no_secret()
    {
        var alice = await SeedPerson("alice");
        var admin = await SeedMember(alice.SpaceId, "admin", MembershipRole.Admin);
        var asOwner = await SignIn(alice);
        var asAdmin = await SignIn(admin.Email, admin.Password);
        var key = $"sk-{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}";
        var connection = await PostFor<ModelConnectionSummary>(
            asOwner,
            $"{Space(alice.SpaceId)}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, key, "scripted"),
            HttpStatusCode.Created);
        var bot = await PostFor<BotSummary>(asOwner, $"{Space(alice.SpaceId)}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, null), HttpStatusCode.Created);
        var root = $"{Space(alice.SpaceId)}/bots/{bot.Id}";
        var tool = (await GetFor<List<BotToolEntry>>(asOwner, $"{root}/tools")).Single();

        await PutFor<BotDetail>(asAdmin, $"{root}/definition", Nothing() with { Name = "Atlas", ModelSettings = new ModelSettingsBody(0.2, null, null, "off", false) });
        await PutFor<BotDetail>(asAdmin, $"{root}/definition", Nothing() with { Name = "Atlas" });
        await PutFor<BotToolEntry>(asAdmin, $"{root}/tools/{tool.ToolId}", new SetToolGrantRequest("ask"));
        await PutFor<BotToolEntry>(asAdmin, $"{root}/tools/{tool.ToolId}", new SetToolGrantRequest("ask"));
        var schedule = await PostFor<BotScheduleResponse>(asOwner, $"{root}/schedules", new BotScheduleBody("0 9 * * *", "UTC", "Report", "new_thread", true), HttpStatusCode.Created);
        (await asOwner.Delete($"{root}/schedules/{schedule.Id}")).EnsureSuccessStatusCode();
        await PostFor<BotDetail>(asOwner, $"{root}/pause", HttpStatusCode.OK);
        (await asOwner.Delete(root)).EnsureSuccessStatusCode();

        var entries = await InSpace(alice.SpaceId, alice.UserId, services =>
            services.GetRequiredService<AuditRepository>().ListEntriesAbout(bot.Id, Token));
        var updated = entries.Single(entry => entry.Action == "bot.updated");
        var granted = entries.Single(entry => entry.Action == "grant.changed");

        entries.Select(entry => entry.Action).Should().Equal(
            ["bot.created", "bot.updated", "grant.changed", "bot.schedule_added", "bot.schedule_removed", "bot.paused", "bot.removed"],
            "each change is one entry, and saving the same values again is not a change");
        entries.Should().OnlyContain(entry => entry.ActorKind == ActorKind.User && entry.TargetKind == "bot");
        entries[0].ActorId.Should().Be(alice.UserId);
        updated.ActorId.Should().Be(admin.UserId, "the entry names who made the change");
        updated.Data.GetProperty("changes").EnumerateObject().Select(change => change.Name).Should().BeEquivalentTo(["name", "model_settings"], "only what changed is kept");
        Fact(updated, "name").Should().Be(("Helper", "Atlas"));
        updated.Data.GetProperty("changes").GetProperty("model_settings").GetProperty("after").GetProperty("temperature").GetDouble().Should().Be(0.2);
        granted.Data.GetProperty("tool").GetString().Should().Be("time.now");
        Fact(granted, "effect").Should().Be(("allow", "ask"));
        entries.Should().OnlyContain(entry => !entry.Data.GetRawText().Contains(key, StringComparison.Ordinal), "no secret is written to the audit log");
        Logs.AllText().Should().NotContain(key);
    }

    #endregion

    #region Helpers

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    private static IEnumerable<string> Names<T>() => typeof(T).GetProperties().Select(property => property.Name);

    private static UpdateBotRequest Nothing() => new(null, null, null, null, null, null, null, null, null, null, null, null, null);

    private Task<List<AuditLogEntry>> Audit((string Email, string Password, Guid UserId, Guid SpaceId) person, Guid botId) =>
        InSpace(person.SpaceId, person.UserId, services => services.GetRequiredService<AuditRepository>().ListEntriesAbout(botId, Token));

    // One changed value of an entry, where either side may be absent.
    private static (string? Before, string? After) Fact(AuditLogEntry entry, string name)
    {
        var change = entry.Data.GetProperty("changes").GetProperty(name);
        return (Word(change.GetProperty("before")), Word(change.GetProperty("after")));

        static string? Word(JsonElement value) => value.ValueKind is JsonValueKind.Null ? null : value.GetString();
    }

    private static async Task<string?> Title(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString();

    private Task<ApiClient> SignIn((string Email, string Password, Guid UserId, Guid SpaceId) person) => SignIn(person.Email, person.Password);

    private async Task<ApiClient> SignIn(string email, string password)
    {
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    private static Task<ModelConnectionSummary> CreateConnection(ApiClient client, Guid spaceId, string name = "Scripted") =>
        PostFor<ModelConnectionSummary>(
            client,
            $"{Space(spaceId)}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, name, null, null, "scripted"),
            HttpStatusCode.Created);

    private static async Task<BotSummary> CreateBot(ApiClient client, Guid spaceId, string name = "Helper")
    {
        var connection = await CreateConnection(client, spaceId, $"For {name}");
        return await PostFor<BotSummary>(client, $"{Space(spaceId)}/bots", new CreateBotRequest(name, "Answer briefly.", connection.Id, null), HttpStatusCode.Created);
    }

    /// <summary>Sends one message to the bot in a new thread and waits for its run to end. Returns the thread.</summary>
    private static async Task<Guid> Ask(ApiClient client, Guid spaceId, Guid botId, string text)
    {
        var thread = await PostFor<ThreadSummary>(client, $"{Space(spaceId)}/bots/{botId}/threads", new CreateThreadRequest(null), HttpStatusCode.Created);
        var sent = await PostFor<SendMessageResponse>(client, $"{Space(spaceId)}/threads/{thread.Id}/messages", new SendMessageRequest(text), HttpStatusCode.Accepted);
        var watch = Stopwatch.StartNew();

        while (true)
        {
            var run = await GetFor<RunSummary>(client, $"{Space(spaceId)}/runs/{sent.Run.Id}");

            if (run.Status is "completed" or "failed")
            {
                run.Status.Should().Be("completed", run.Error?.Message);
                return thread.Id;
            }

            (watch.Elapsed < patience).Should().BeTrue($"run {sent.Run.Id} should end; it is {run.Status}");
            await Task.Delay(25, Token);
        }
    }

    private async Task<T> InSpace<T>(Guid spaceId, Guid userId, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var tenant = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var result = await work(scope.ServiceProvider);
        await tenant.Commit(Token);
        return result;
    }

    /// <summary>A bot beneath another, which only a bot building a bot makes; the editor has no route for it.</summary>
    private Task<Guid> SeedChild(Guid spaceId, Guid userId, Guid parentId, string name) =>
        InSpace(spaceId, userId, async services =>
        {
            var db = services.GetRequiredService<DroverDbContext>();
            var parent = await db.Bots.AsNoTracking().SingleAsync(bot => bot.Id == parentId, Token);
            var child = new Bot
            {
                Name = name,
                Handle = name.ToLowerInvariant(),
                ParentBotId = parentId,
                Depth = parent.Depth + 1,
                ModelConnectionId = parent.ModelConnectionId,
                Model = parent.Model,
                CreatedByBotId = parentId,
            };
            db.Bots.Add(child);
            db.BotAncestors.Add(new BotAncestor { AncestorBotId = child.Id, DescendantBotId = child.Id, Distance = 0 });
            db.BotAncestors.Add(new BotAncestor { AncestorBotId = parentId, DescendantBotId = child.Id, Distance = 1 });
            await db.SaveChangesAsync(Token);
            return child.Id;
        });

    /// <summary>A tool of a tool connection whose definition changed after the bot was granted it.</summary>
    private Task<Guid> SeedChangedServerTool(Guid spaceId, Guid userId, Guid botId) =>
        InSpace(spaceId, userId, async services =>
        {
            var db = services.GetRequiredService<DroverDbContext>();
            var connection = new ToolConnection { Name = "Sample server", Slug = "sample", Url = "https://tools.example.test/mcp" };
            var tool = new Tool
            {
                SourceKind = ToolSourceKind.Mcp,
                ToolConnectionId = connection.Id,
                Name = "lookup",
                Title = "Look up",
                Description = "What the server says now.",
                InputSchema = JsonSerializer.SerializeToElement(new { type = "object" }),
                DefinitionHash = "changed",
                ReviewedDefinition = JsonSerializer.SerializeToElement(new ReviewedToolDefinition("Look up", "What a person read.", "{}")),
                FirstSeenAt = Time.GetUtcNow(),
                LastSeenAt = Time.GetUtcNow(),
            };
            db.ToolConnections.Add(connection);
            db.Tools.Add(tool);
            db.ToolGrants.Add(new ToolGrant { BotId = botId, ToolId = tool.Id, Effect = GrantEffect.Allow, ReviewState = ReviewState.NeedsReview, GrantedByUserId = userId });
            await db.SaveChangesAsync(Token);
            return tool.Id;
        });

    private Task<Guid> SeedFile(Guid spaceId, Guid userId) =>
        InSpace(spaceId, userId, async services =>
        {
            var db = services.GetRequiredService<DroverDbContext>();
            var file = new StoredFile { AudienceUserId = userId, StorageKey = $"avatars/{Guid.CreateVersion7():N}", Name = "avatar.png", ContentType = "image/png", Size = 1, Sha256 = new string('0', 64), CreatedByUserId = userId };
            db.Files.Add(file);
            await db.SaveChangesAsync(Token);
            return file.Id;
        });

    private Task<Guid> SeedCatalogEntry(Guid spaceId, Guid userId, Guid connectionId, string modelId, ModelCapabilities capabilities) =>
        InSpace(spaceId, userId, async services =>
        {
            var db = services.GetRequiredService<DroverDbContext>();
            var entry = new ModelCatalogEntry { ModelConnectionId = connectionId, Model = modelId, DisplayName = "Plain", Capabilities = capabilities, LastSeenAt = Time.GetUtcNow() };
            db.ModelCatalog.Add(entry);
            await db.SaveChangesAsync(Token);
            return entry.ModelConnectionId;
        });

    private static async Task<T> PostFor<T>(ApiClient client, string path, HttpStatusCode expected) => await ReadAs<T>(await client.Post(path), expected);

    private static async Task<T> PostFor<T>(ApiClient client, string path, object body, HttpStatusCode expected) => await ReadAs<T>(await client.Post(path, body), expected);

    private static async Task<T> PutFor<T>(ApiClient client, string path, object body) => await ReadAs<T>(await client.Put(path, body), HttpStatusCode.OK);

    private static async Task<T> GetFor<T>(ApiClient client, string path) => await ReadAs<T>(await client.Get(path), HttpStatusCode.OK);

    private static async Task<T> ReadAs<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    #endregion
}
