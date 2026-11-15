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

    #region Helpers

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
