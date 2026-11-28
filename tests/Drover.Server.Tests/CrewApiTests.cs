namespace Drover.Server.Tests;

/// <summary>
/// The crew as the API shows it, over real PostgreSQL: the tools a bot starts with at the top of the tree and
/// beneath a parent, and the bot that made a bot in every bot summary.
/// </summary>
public sealed class CrewApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private static readonly string[] crewTools = ["crew.add", "crew.change", "crew.list", "crew.remove"];

    private ScriptedModelProvider? model;

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_bot_a_person_makes_at_the_top_leads_a_crew_and_one_made_beneath_a_parent_does_not()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);
        var lead = await CreateBot(client, alice.SpaceId, connection.Id, "Lead");
        var leadTools = await Tools(client, alice.SpaceId, lead.Id);
        foreach (var tool in leadTools.Where(entry => crewTools.Contains(entry.Name)))
        {
            (await client.Put($"{Space(alice.SpaceId)}/bots/{lead.Id}/tools/{tool.ToolId}", new SetToolGrantRequest("allow"))).EnsureSuccessStatusCode();
        }

        var allowed = await Tools(client, alice.SpaceId, lead.Id);
        var child = await CreateBot(client, alice.SpaceId, connection.Id, "Child", lead.Id);
        var childTools = await Tools(client, alice.SpaceId, child.Id);

        var defaultGrants = BuiltinTools.GrantedToNewBots.Select(name => (name, "allow"))
            .Concat(AutomationTools.AskedByDefault.Select(name => (name, "ask")));
        leadTools.Where(entry => entry.Grant != "off").Select(entry => (entry.Name, entry.Grant)).Should().BeEquivalentTo(
            [.. defaultGrants, ("crew.list", "ask"), ("crew.add", "ask"), ("crew.change", "ask"), ("crew.remove", "ask")]);
        leadTools.Where(entry => crewTools.Contains(entry.Name)).Should().OnlyContain(entry => entry.Sensitivity == (entry.Name == "crew.list" ? "read" : "write"));
        leadTools.Where(entry => entry.AlwaysAsks).Select(entry => entry.Name).Should().Equal("crew.add");
        allowed.Where(entry => entry.AlwaysAsks).Select(entry => (entry.Name, entry.Grant)).Should().Equal(
            [("crew.add", "allow")],
            "making a bot asks a person whatever its grant, and the list says so even where it is set to allow");
        childTools.Where(entry => entry.Grant != "off").Select(entry => (entry.Name, entry.Grant)).Should().BeEquivalentTo(
            defaultGrants,
            "a bot beneath a parent is not given the crew tools, even where its parent allows them");
    }

    [Fact]
    public async Task Every_bot_summary_names_the_bot_that_made_it_and_none_for_a_bot_a_person_made()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateConnection(client, alice.SpaceId);
        var lead = await CreateBot(client, alice.SpaceId, connection.Id, "Lead");
        var byPerson = await CreateBot(client, alice.SpaceId, connection.Id, "By person", lead.Id);

        var made = await RunCrewTool(alice, lead.Id, "crew.add", """{"name":"Scout","description":"Finds sources.","instructions":"Search."}""");
        var scoutId = made.GetProperty("id").GetGuid();
        var listed = await GetFor<List<BotSummary>>(client, $"{Space(alice.SpaceId)}/bots");
        var read = await GetFor<BotSummary>(client, $"{Space(alice.SpaceId)}/bots/{scoutId}");
        var detail = await GetFor<BotDetail>(client, $"{Space(alice.SpaceId)}/bots/{scoutId}/definition");

        listed.Select(bot => (bot.Name, bot.ParentBotId, bot.MadeByBotId)).Should().BeEquivalentTo(
            [("Lead", (Guid?)null, (Guid?)null), ("By person", lead.Id, (Guid?)null), ("Scout", lead.Id, (Guid?)lead.Id)]);
        byPerson.MadeByBotId.Should().BeNull();
        read.MadeByBotId.Should().Be(lead.Id);
        detail.Should().Match<BotDetail>(found => found.CreatedByBotId == lead.Id && found.CreatedByUserId == alice.UserId
            && found.DescriptionForBots == "Finds sources." && found.ParentBotId == lead.Id);
    }

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(new ScriptedModel()));

    #endregion

    #region Helpers

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    /// <summary>Calls a crew tool as a run of the bot would, for the person, through the server's own tool.</summary>
    private Task<JsonElement> RunCrewTool((string Email, string Password, Guid UserId, Guid SpaceId) person, Guid botId, string name, string arguments)
    {
        var tool = Services.GetServices<BuiltinTool>().Single(found => found.Name == name);
        var call = new BuiltinToolCall(
            JsonElement.Parse(arguments),
            botId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Services.GetRequiredService<RunScopes>().For(person.SpaceId, person.UserId),
            _ => Task.CompletedTask,
            () => Task.CompletedTask,
            _ => { });
        return tool.Run(call, Token);
    }

    private async Task<ApiClient> SignIn((string Email, string Password, Guid UserId, Guid SpaceId) person)
    {
        var client = NewClient();
        (await client.SignIn(person.Email, person.Password)).EnsureSuccessStatusCode();
        return client;
    }

    private static Task<ModelConnectionSummary> CreateConnection(ApiClient client, Guid spaceId) =>
        PostFor<ModelConnectionSummary>(
            client,
            $"{Space(spaceId)}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted"));

    private static Task<BotSummary> CreateBot(ApiClient client, Guid spaceId, Guid connectionId, string name, Guid? parentBotId = null) =>
        PostFor<BotSummary>(client, $"{Space(spaceId)}/bots", new CreateBotRequest(name, "Answer briefly.", connectionId, null, parentBotId));

    private static Task<List<BotToolEntry>> Tools(ApiClient client, Guid spaceId, Guid botId) =>
        GetFor<List<BotToolEntry>>(client, $"{Space(spaceId)}/bots/{botId}/tools");

    private static async Task<T> PostFor<T>(ApiClient client, string path, object body)
    {
        var response = await client.Post(path, body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
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
