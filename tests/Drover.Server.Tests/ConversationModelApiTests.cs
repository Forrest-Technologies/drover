namespace Drover.Server.Tests;

/// <summary>
/// Choosing the model a conversation answers with, end to end over real PostgreSQL: the real worker and agent runtime
/// inside the server, with the scripted model in place of a provider. A bot's allowance is the models on its own
/// model connection, narrowed by the space's cost limits.
/// </summary>
public sealed class ConversationModelApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private const string BotModel = "scripted";

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    private ScriptedModel script = new();

    private ScriptedModelProvider? model;

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(script));

    #endregion

    #region Choosing

    [Fact]
    public async Task Picking_a_model_makes_the_next_run_answer_with_it_and_each_reply_names_its_model()
    {
        script = new ScriptedModel([new TextOutput("Hard answer.")], [new TextOutput("Plain answer.")]);
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var bot = await CreateBot(client, alice.SpaceId);
        await SeedModel(alice, bot.ModelConnectionId, BotModel, "Scripted");
        await SeedModel(alice, bot.ModelConnectionId, "stronger", "Stronger", new ModelPricing { InputPerMillion = 3, OutputPerMillion = 15 });
        var thread = await PostFor<ThreadSummary>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/threads", new CreateThreadRequest(null), HttpStatusCode.Created);
        var path = $"{Space(alice.SpaceId)}/threads/{thread.Id}";

        var before = await GetFor<ConversationModelChoice>(client, $"{path}/models");
        var chosen = await PutFor<ConversationModelChoice>(client, $"{path}/model", new ChooseModelRequest("stronger"));
        var first = await PostFor<SendMessageResponse>(client, $"{path}/messages", new SendMessageRequest("A hard one."), HttpStatusCode.Accepted);
        (await WaitForRun(client, alice.SpaceId, first.Run.Id)).Status.Should().Be("completed");
        var back = await PutFor<ConversationModelChoice>(client, $"{path}/model", new ChooseModelRequest(null));
        var second = await PostFor<SendMessageResponse>(client, $"{path}/messages", new SendMessageRequest("An easy one."), HttpStatusCode.Accepted);
        (await WaitForRun(client, alice.SpaceId, second.Run.Id)).Status.Should().Be("completed");
        var messages = await GetFor<List<MessageResponse>>(client, $"{path}/messages");
        var metered = await MeteredModels(alice, first.Run.Id);

        before.Should().Match<ConversationModelChoice>(choice => choice.BotModel == BotModel && choice.BotModelName == "Scripted" && choice.Chosen == null);
        before.Models.Select(option => option.Model).Should().Equal(BotModel, "stronger");
        chosen.Chosen.Should().Be("stronger");
        back.Chosen.Should().BeNull("choosing nothing goes back to the bot's own model");
        model!.ModelsAsked.Should().Equal("stronger", BotModel);
        messages.Where(message => message.Role == "assistant").Select(message => message.Model).Should().Equal("stronger", BotModel);
        messages.Where(message => message.Role == "user").Should().OnlyContain(message => message.Model == null);
        metered.Should().Equal(["stronger"], "the run's usage is counted against the chosen model");
        (await GetFor<BotSummary>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}")).Model.Should().Be(BotModel, "the bot keeps its own model");
    }

    [Fact]
    public async Task Choosing_the_bots_own_model_stores_no_choice()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var bot = await CreateBot(client, alice.SpaceId);
        var thread = await PostFor<ThreadSummary>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/threads", new CreateThreadRequest(null), HttpStatusCode.Created);

        var chosen = await PutFor<ConversationModelChoice>(client, $"{Space(alice.SpaceId)}/threads/{thread.Id}/model", new ChooseModelRequest($" {BotModel} "));

        chosen.Should().Match<ConversationModelChoice>(choice => choice.Chosen == null && choice.BotModelName == BotModel);
        (await StoredChoice(alice, thread.Id)).Should().BeNull();
    }

    #endregion

    #region Refusals

    [Fact]
    public async Task A_model_outside_the_bots_allowance_is_refused()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var bot = await CreateBot(client, alice.SpaceId);
        var other = await CreateConnection(client, alice.SpaceId, "Other");
        await SeedModel(alice, other.Id, "elsewhere", "Elsewhere");
        await SeedModel(alice, bot.ModelConnectionId, "gone", "Gone", state: AvailabilityState.Missing);
        var thread = await PostFor<ThreadSummary>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/threads", new CreateThreadRequest(null), HttpStatusCode.Created);
        var path = $"{Space(alice.SpaceId)}/threads/{thread.Id}";

        var unknown = await client.Put($"{path}/model", new ChooseModelRequest("invented"));
        var onAnotherConnection = await client.Put($"{path}/model", new ChooseModelRequest("elsewhere"));
        var noLongerOffered = await client.Put($"{path}/model", new ChooseModelRequest("gone"));
        var tooLong = await client.Put($"{path}/model", new ChooseModelRequest(new string('m', 201)));
        var listed = await GetFor<ConversationModelChoice>(client, $"{path}/models");

        foreach (var refused in new[] { unknown, onAnotherConnection, noLongerOffered, tooLong })
        {
            refused.StatusCode.Should().Be(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync(Token));
        }

        (await unknown.Content.ReadAsStringAsync(Token)).Should().Contain("not one of the models on the bot's model connection");
        listed.Models.Should().BeEmpty("the other connection's model and the missing one are not the bot's to use");
        (await StoredChoice(alice, thread.Id)).Should().BeNull();
    }

    [Fact]
    public async Task A_model_that_could_cost_more_than_the_space_allows_is_refused_and_a_lower_limit_undoes_an_earlier_choice()
    {
        script = new ScriptedModel([new TextOutput("Answered.")]);
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var bot = await CreateBot(client, alice.SpaceId);

        // At the server's 5,000,000 tokens a run, $15 a million is $75 at most: inside the server's $100. $40 is $200.
        await SeedModel(alice, bot.ModelConnectionId, "fits", "Fits", new ModelPricing { InputPerMillion = 3, OutputPerMillion = 15 });
        await SeedModel(alice, bot.ModelConnectionId, "dear", "Dear", new ModelPricing { InputPerMillion = 40, OutputPerMillion = 10 });
        var thread = await PostFor<ThreadSummary>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/threads", new CreateThreadRequest(null), HttpStatusCode.Created);
        var path = $"{Space(alice.SpaceId)}/threads/{thread.Id}";

        var dear = await client.Put($"{path}/model", new ChooseModelRequest("dear"));
        var fits = await PutFor<ConversationModelChoice>(client, $"{path}/model", new ChooseModelRequest("fits"));

        // The space lowers what a run tree may cost to $50: the choice made under the server's limit no longer fits.
        await SetSpaceSettings(alice, new SpaceSettings { MaxDelegationTreeCostCents = 5_000 });
        var narrowed = await GetFor<ConversationModelChoice>(client, $"{path}/models");
        var again = await client.Put($"{path}/model", new ChooseModelRequest("fits"));
        var sent = await PostFor<SendMessageResponse>(client, $"{path}/messages", new SendMessageRequest("Hello"), HttpStatusCode.Accepted);
        (await WaitForRun(client, alice.SpaceId, sent.Run.Id)).Status.Should().Be("completed");
        var reply = (await GetFor<List<MessageResponse>>(client, $"{path}/messages")).Last(message => message.Role == "assistant");

        dear.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await dear.Content.ReadAsStringAsync(Token)).Should().Contain("could cost more in one run than this space lets a run cost");
        fits.Chosen.Should().Be("fits");
        fits.Models.Select(option => option.Model).Should().Equal("fits");
        narrowed.Chosen.Should().BeNull("a choice the space's limits no longer allow is not in force");
        narrowed.Models.Should().BeEmpty();
        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        model!.ModelsAsked.Should().Equal(BotModel);
        reply.Model.Should().Be(BotModel, "the run answers with the bot's own model");
    }

    [Fact]
    public async Task A_bots_lower_token_limit_lets_a_dearer_model_fit()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var bot = await CreateBot(client, alice.SpaceId);
        await SeedModel(alice, bot.ModelConnectionId, "dear", "Dear", new ModelPricing { InputPerMillion = 40, OutputPerMillion = 10 });
        await SetRunLimits(alice, bot.Id, new RunLimits { MaxTokens = 1_000_000 });
        var thread = await PostFor<ThreadSummary>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/threads", new CreateThreadRequest(null), HttpStatusCode.Created);

        var chosen = await PutFor<ConversationModelChoice>(client, $"{Space(alice.SpaceId)}/threads/{thread.Id}/model", new ChooseModelRequest("dear"));

        chosen.Chosen.Should().Be("dear", "a run of this bot can use at most a million tokens, so $40 at most");
    }

    [Fact]
    public async Task Only_someone_who_can_see_the_conversation_can_read_or_choose_its_model()
    {
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Admin);
        var outsider = await SeedPerson("outsider");
        var client = await SignIn(alice);
        var asMember = NewClient();
        await asMember.SignIn(member.Email, member.Password);
        var asOutsider = await SignIn(outsider);
        var bot = await CreateBot(client, alice.SpaceId);
        await SeedModel(alice, bot.ModelConnectionId, "stronger", "Stronger");
        var thread = await PostFor<ThreadSummary>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/threads", new CreateThreadRequest(null), HttpStatusCode.Created);
        var path = $"{Space(alice.SpaceId)}/threads/{thread.Id}";

        (await asMember.Get($"{path}/models")).StatusCode.Should().Be(HttpStatusCode.NotFound, "a private conversation is not found, even by an admin");
        (await asMember.Put($"{path}/model", new ChooseModelRequest("stronger"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await asOutsider.Get($"{Space(outsider.SpaceId)}/threads/{thread.Id}/models")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await asOutsider.Put($"{Space(outsider.SpaceId)}/threads/{thread.Id}/model", new ChooseModelRequest("stronger"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await StoredChoice(alice, thread.Id)).Should().BeNull();
    }

    #endregion

    #region Helpers

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    private async Task<ApiClient> SignIn((string Email, string Password, Guid UserId, Guid SpaceId) person)
    {
        var client = NewClient();
        (await client.SignIn(person.Email, person.Password)).EnsureSuccessStatusCode();
        return client;
    }

    private static Task<ModelConnectionSummary> CreateConnection(ApiClient client, Guid spaceId, string name = "Scripted") =>
        PostFor<ModelConnectionSummary>(
            client,
            $"{Space(spaceId)}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, name, null, null, BotModel),
            HttpStatusCode.Created);

    private static async Task<BotSummary> CreateBot(ApiClient client, Guid spaceId)
    {
        var connection = await CreateConnection(client, spaceId);
        return await PostFor<BotSummary>(client, $"{Space(spaceId)}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, null), HttpStatusCode.Created);
    }

    private Task SeedModel(
        (string Email, string Password, Guid UserId, Guid SpaceId) person,
        Guid connectionId,
        string modelId,
        string name,
        ModelPricing? pricing = null,
        AvailabilityState state = AvailabilityState.Available) =>
        InSpace(person, services =>
        {
            services.GetRequiredService<DroverDbContext>().ModelCatalog.Add(new ModelCatalogEntry
            {
                ModelConnectionId = connectionId,
                Model = modelId,
                DisplayName = name,
                Pricing = pricing ?? new(),
                State = state,
                LastSeenAt = Time.GetUtcNow(),
            });
            return Task.CompletedTask;
        });

    private Task SetSpaceSettings((string Email, string Password, Guid UserId, Guid SpaceId) person, SpaceSettings settings) =>
        InSpace(person, async services =>
        {
            var space = await services.GetRequiredService<DroverDbContext>().Spaces.SingleAsync(row => row.Id == person.SpaceId, Token);
            space.Settings = settings;
        });

    private Task SetRunLimits((string Email, string Password, Guid UserId, Guid SpaceId) person, Guid botId, RunLimits limits) =>
        InSpace(person, async services =>
        {
            var bot = await services.GetRequiredService<DroverDbContext>().Bots.SingleAsync(row => row.Id == botId, Token);
            bot.RunLimits = limits;
        });

    private async Task<string?> StoredChoice((string Email, string Password, Guid UserId, Guid SpaceId) person, Guid threadId)
    {
        string? stored = null;
        await InSpace(person, async services =>
            stored = (await services.GetRequiredService<DroverDbContext>().Threads.SingleAsync(row => row.Id == threadId, Token)).Model);
        return stored;
    }

    private async Task<List<string>> MeteredModels((string Email, string Password, Guid UserId, Guid SpaceId) person, Guid runId)
    {
        List<string> models = [];
        await InSpace(person, async services =>
            models = await services.GetRequiredService<DroverDbContext>().UsageRecords
                .Where(row => row.RunId == runId)
                .Select(row => row.Model)
                .Distinct()
                .ToListAsync(Token));
        return models;
    }

    private async Task InSpace((string Email, string Password, Guid UserId, Guid SpaceId) person, Func<IServiceProvider, Task> work)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(person.SpaceId, person.UserId);
        await using var tenant = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        await work(scope.ServiceProvider);
        await tenant.Commit(Token);
    }

    private static async Task<T> PostFor<T>(ApiClient client, string path, object body, HttpStatusCode expected)
    {
        var response = await client.Post(path, body);
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private static async Task<T> PutFor<T>(ApiClient client, string path, object body)
    {
        var response = await client.Put(path, body);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private static async Task<T> GetFor<T>(ApiClient client, string path)
    {
        var response = await client.Get(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private static async Task<RunSummary> WaitForRun(ApiClient client, Guid spaceId, Guid runId)
    {
        var watch = Stopwatch.StartNew();

        while (true)
        {
            var run = await GetFor<RunSummary>(client, $"{Space(spaceId)}/runs/{runId}");

            if (run.Status is "completed" or "failed" or "cancelled")
            {
                return run;
            }

            (watch.Elapsed < patience).Should().BeTrue($"run {runId} should finish; it is {run.Status}");
            await Task.Delay(25, Token);
        }
    }

    #endregion
}
