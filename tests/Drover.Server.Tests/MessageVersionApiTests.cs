namespace Drover.Server.Tests;

/// <summary>
/// Editing a message, asking for another reply, and switching between the versions, end to end over real PostgreSQL:
/// the real worker and agent runtime inside the server, with the scripted model in place of a provider.
/// </summary>
public sealed class MessageVersionApiTests(PostgresServer postgres) : ServerTestBase(postgres)
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

    #region Regenerate

    [Fact]
    public async Task Another_reply_uses_the_conversations_current_model_and_the_earlier_one_can_be_shown_again()
    {
        script = new ScriptedModel([new TextOutput("First answer.")], [new TextOutput("Second answer.")]);
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var bot = await CreateBot(client, alice.SpaceId);
        await SeedModel(alice, bot.ModelConnectionId, BotModel, "Scripted");
        await SeedModel(alice, bot.ModelConnectionId, "stronger", "Stronger");
        var path = await NewThread(client, alice.SpaceId, bot.Id);
        var sent = await PostFor<SendMessageResponse>(client, $"{path}/messages", new SendMessageRequest("A question."), HttpStatusCode.Accepted);
        await WaitForRun(client, alice.SpaceId, sent.Run.Id);
        var first = (await GetFor<List<MessageResponse>>(client, $"{path}/messages")).Single(message => message.Role == "assistant");
        await PutFor<ConversationModelChoice>(client, $"{path}/model", new ChooseModelRequest("stronger"));

        var again = await PostFor<SendMessageResponse>(client, $"{path}/messages/{first.Id}/regenerate", new { }, HttpStatusCode.Accepted);
        (await WaitForRun(client, alice.SpaceId, again.Run.Id)).Status.Should().Be("completed");
        var afterAgain = await GetFor<List<MessageResponse>>(client, $"{path}/messages");
        var second = afterAgain.Single(message => message.Role == "assistant");
        var shown = await client.Post($"{path}/messages/{first.Id}/show", new { });
        var afterShow = await GetFor<List<MessageResponse>>(client, $"{path}/messages");
        var oldUsage = await GetFor<RunUsageDetail>(client, $"{Space(alice.SpaceId)}/runs/{sent.Run.Id}/usage");

        again.Message.Id.Should().Be(sent.Message.Id, "the person's message is answered again");
        second.Model.Should().Be("stronger", "another reply answers with the conversation's model as it is now");
        model!.ModelsAsked.Should().Equal(BotModel, "stronger");
        afterAgain.Select(message => message.Role).Should().Equal("user", "assistant");
        second.Versions.Should().Equal(first.Id, second.Id);
        afterAgain.Single(message => message.Role == "user").Versions.Should().BeNull("the person's message has one version");
        shown.StatusCode.Should().Be(HttpStatusCode.NoContent);
        afterShow.Single(message => message.Role == "assistant").Should().Match<MessageResponse>(message =>
            message.Id == first.Id && message.Model == BotModel && message.Versions!.SequenceEqual(new[] { first.Id, second.Id }));
        (await GetFor<RunSummary>(client, $"{Space(alice.SpaceId)}/runs/{sent.Run.Id}")).Status.Should().Be("completed");
        oldUsage.Own.InputTokens.Should().BeGreaterThan(0, "the earlier reply's run stays in usage");
    }

    #endregion

    #region Edit

    [Fact]
    public async Task Editing_the_last_message_reruns_from_it()
    {
        script = new ScriptedModel([new TextOutput("Four.")], [new TextOutput("Six.")]);
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var bot = await CreateBot(client, alice.SpaceId);
        var path = await NewThread(client, alice.SpaceId, bot.Id);
        var sent = await PostFor<SendMessageResponse>(client, $"{path}/messages", new SendMessageRequest("2 + 2?"), HttpStatusCode.Accepted);
        await WaitForRun(client, alice.SpaceId, sent.Run.Id);

        var edited = await PostFor<SendMessageResponse>(client, $"{path}/messages/{sent.Message.Id}/edit", new EditMessageRequest("3 + 3?"), HttpStatusCode.Accepted);
        (await WaitForRun(client, alice.SpaceId, edited.Run.Id)).Status.Should().Be("completed");
        var messages = await GetFor<List<MessageResponse>>(client, $"{path}/messages");

        messages.Select(message => (message.Role, Text: message.Parts.Single().Text)).Should().Equal(("user", "3 + 3?"), ("assistant", "Six."));
        messages[0].Versions.Should().Equal(sent.Message.Id, edited.Message.Id);
        messages[1].Versions.Should().BeNull("the reply to the edit is the only reply to it");
        script.Requests[1].Select(seen => seen.Summary).Should().Equal(["3 + 3?"]);
    }

    #endregion

    #region Refusals

    [Fact]
    public async Task Nothing_changes_while_the_bot_works_and_a_person_outside_the_conversation_finds_nothing()
    {
        script = new ScriptedModel([new TextOutput("Done.")], [new TextOutput("Thinking"), new PauseOutput()]);
        var alice = await SeedPerson("alice");
        var bob = await SeedMember(alice.SpaceId, "bob", MembershipRole.Member);
        var client = await SignIn(alice);
        var bobs = await SignIn((bob.Email, bob.Password, bob.UserId, alice.SpaceId));
        var bot = await CreateBot(client, alice.SpaceId);
        var path = await NewThread(client, alice.SpaceId, bot.Id);
        var sent = await PostFor<SendMessageResponse>(client, $"{path}/messages", new SendMessageRequest("Hello"), HttpStatusCode.Accepted);
        await WaitForRun(client, alice.SpaceId, sent.Run.Id);
        var reply = (await GetFor<List<MessageResponse>>(client, $"{path}/messages")).Single(message => message.Role == "assistant");

        var empty = await client.Post($"{path}/messages/{sent.Message.Id}/edit", new EditMessageRequest(" "));
        var unknown = await client.Post($"{path}/messages/{Guid.CreateVersion7()}/regenerate", new { });
        var notAReply = await client.Post($"{path}/messages/{sent.Message.Id}/regenerate", new { });
        var byBob = await bobs.Post($"{path}/messages/{reply.Id}/regenerate", new { });
        var working = await PostFor<SendMessageResponse>(client, $"{path}/messages", new SendMessageRequest("More"), HttpStatusCode.Accepted);
        await script.Paused.WaitAsync(patience, Token);
        var busy = await client.Post($"{path}/messages/{reply.Id}/regenerate", new { });
        var busyEdit = await client.Post($"{path}/messages/{working.Message.Id}/edit", new EditMessageRequest("Less"));
        await client.Post($"{Space(alice.SpaceId)}/runs/{working.Run.Id}/cancel", null);

        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        notAReply.StatusCode.Should().Be(HttpStatusCode.NotFound, "only a bot's reply is answered again");
        byBob.StatusCode.Should().Be(HttpStatusCode.NotFound, "Bob cannot see Alice's private conversation");
        busy.StatusCode.Should().Be(HttpStatusCode.Conflict);
        busyEdit.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await busy.Content.ReadAsStringAsync(Token)).Should().Contain("still working");
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

    private static async Task<BotSummary> CreateBot(ApiClient client, Guid spaceId)
    {
        var connection = await PostFor<ModelConnectionSummary>(
            client,
            $"{Space(spaceId)}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, BotModel),
            HttpStatusCode.Created);
        return await PostFor<BotSummary>(client, $"{Space(spaceId)}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, null), HttpStatusCode.Created);
    }

    private Task SeedModel((string Email, string Password, Guid UserId, Guid SpaceId) person, Guid connectionId, string modelId, string name) =>
        InSpace(person, services =>
        {
            services.GetRequiredService<DroverDbContext>().ModelCatalog.Add(new ModelCatalogEntry
            {
                ModelConnectionId = connectionId,
                Model = modelId,
                DisplayName = name,
                Pricing = new(),
                State = AvailabilityState.Available,
                LastSeenAt = Time.GetUtcNow(),
            });
            return Task.CompletedTask;
        });

    private async Task InSpace((string Email, string Password, Guid UserId, Guid SpaceId) person, Func<IServiceProvider, Task> work)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(person.SpaceId, person.UserId);
        await using var tenant = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        await work(scope.ServiceProvider);
        await tenant.Commit(Token);
    }

    private static async Task<string> NewThread(ApiClient client, Guid spaceId, Guid botId)
    {
        var thread = await PostFor<ThreadSummary>(client, $"{Space(spaceId)}/bots/{botId}/threads", new CreateThreadRequest(null), HttpStatusCode.Created);
        return $"{Space(spaceId)}/threads/{thread.Id}";
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
