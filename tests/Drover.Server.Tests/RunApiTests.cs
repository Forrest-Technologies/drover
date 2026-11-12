namespace Drover.Server.Tests;

/// <summary>
/// Bots, threads, messages, runs, and cancel through the API, end to end over real PostgreSQL: the real worker and
/// agent runtime inside the server, with the scripted model in place of a provider.
/// </summary>
public sealed class RunApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    private ScriptedModel script = new();

    private ScriptedModelProvider? model;

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(script));

    #endregion

    #region Chat

    [Fact]
    public async Task Sending_a_message_starts_a_run_that_a_worker_finishes_and_the_reply_is_in_the_thread()
    {
        script = new ScriptedModel([new ToolCallOutput("c1", "time_now")], [new TextOutput("It is noon.")]);
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var bot = await CreateBot(client, alice.SpaceId);

        var thread = await PostFor<ThreadSummary>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/threads", new CreateThreadRequest(null), HttpStatusCode.Created);
        var sent = await client.Post($"{Space(alice.SpaceId)}/threads/{thread.Id}/messages", new SendMessageRequest("What time is it?"));
        var accepted = (await sent.Content.ReadFromJsonAsync<SendMessageResponse>(Token))!;
        var run = await WaitForRun(client, alice.SpaceId, accepted.Run.Id, "completed", "failed");
        var messages = await GetFor<List<MessageResponse>>(client, $"{Space(alice.SpaceId)}/threads/{thread.Id}/messages");
        var threads = await GetFor<List<ThreadSummary>>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/threads");

        sent.StatusCode.Should().Be(HttpStatusCode.Accepted);
        accepted.Run.Status.Should().Be("queued");
        accepted.Message.Should().Match<MessageResponse>(message => message.Role == "user" && message.Position == 1);
        run.Status.Should().Be("completed", run.Error?.Message);
        run.Usage.Should().Be(new UsageTotals(20, 10, 0, 0m));
        messages.Select(message => message.Role).Should().Equal("user", "assistant", "tool", "assistant");
        messages[1].Parts.Should().ContainSingle(part => part.Kind == "tool_call" && part.ToolName == "time_now");
        messages[2].Parts.Should().ContainSingle(part => part.Kind == "tool_result" && !part.IsError && part.Result!.Contains("utc"));
        messages[3].Parts.Should().ContainSingle(part => part.Kind == "text" && part.Text == "It is noon.");
        threads.Should().ContainSingle().Which.Should().Match<ThreadSummary>(found =>
            found.Id == thread.Id && found.Title == "What time is it?" && !found.IsShared && found.ActiveRunId == null);
        model!.ToolsOffered[0].Should().Equal("time_now");
    }

    [Fact]
    public async Task A_thread_shows_its_active_run_and_refuses_another_message_until_it_ends()
    {
        script = new ScriptedModel([new TextOutput("Thinking"), new PauseOutput()]);
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var bot = await CreateBot(client, alice.SpaceId);
        var thread = await PostFor<ThreadSummary>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/threads", new CreateThreadRequest("Plans"), HttpStatusCode.Created);
        var first = await PostFor<SendMessageResponse>(client, $"{Space(alice.SpaceId)}/threads/{thread.Id}/messages", new SendMessageRequest("First"), HttpStatusCode.Accepted);
        await script.Paused.WaitAsync(patience, Token);

        var during = await GetFor<ThreadSummary>(client, $"{Space(alice.SpaceId)}/threads/{thread.Id}");
        var second = await client.Post($"{Space(alice.SpaceId)}/threads/{thread.Id}/messages", new SendMessageRequest("Second"));
        var cancelled = await client.Post($"{Space(alice.SpaceId)}/runs/{first.Run.Id}/cancel");
        var run = await WaitForRun(client, alice.SpaceId, first.Run.Id, "cancelled", "completed", "failed");
        var after = await GetFor<ThreadSummary>(client, $"{Space(alice.SpaceId)}/threads/{thread.Id}");

        during.ActiveRunId.Should().Be(first.Run.Id);
        during.Title.Should().Be("Plans", "a title the person gave is kept");
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK);
        run.Status.Should().Be("cancelled");
        run.StatusReason.Should().Be("cancelled_by_user");
        after.ActiveRunId.Should().BeNull();
    }

    [Fact]
    public async Task An_empty_or_oversized_message_is_refused_and_nothing_is_stored()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var bot = await CreateBot(client, alice.SpaceId);
        var thread = await PostFor<ThreadSummary>(client, $"{Space(alice.SpaceId)}/bots/{bot.Id}/threads", new CreateThreadRequest(null), HttpStatusCode.Created);

        var empty = await client.Post($"{Space(alice.SpaceId)}/threads/{thread.Id}/messages", new SendMessageRequest("  "));
        var missing = await client.Post($"{Space(alice.SpaceId)}/threads/{Guid.CreateVersion7()}/messages", new SendMessageRequest("Hello"));
        var unsigned = await client.PostWithoutToken($"{Space(alice.SpaceId)}/threads/{thread.Id}/messages", new SendMessageRequest("Hello"));

        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unsigned.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a state-changing request needs the antiforgery token");
        (await GetFor<List<MessageResponse>>(client, $"{Space(alice.SpaceId)}/threads/{thread.Id}/messages")).Should().BeEmpty();
    }

    #endregion

    #region Bots

    [Fact]
    public async Task An_admin_creates_a_bot_and_a_member_can_see_it_but_not_create_one()
    {
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var asOwner = await SignIn(alice);
        var asMember = NewClient();
        await asMember.SignIn(member.Email, member.Password);
        var connection = await CreateConnection(asOwner, alice.SpaceId);

        var created = await asOwner.Post($"{Space(alice.SpaceId)}/bots", new CreateBotRequest("Atlas", "Be brief.", connection.Id, null));
        var bot = (await created.Content.ReadFromJsonAsync<BotSummary>(Token))!;
        var refused = await asMember.Post($"{Space(alice.SpaceId)}/bots", new CreateBotRequest("Mine", string.Empty, connection.Id, null));
        var unnamed = await asOwner.Post($"{Space(alice.SpaceId)}/bots", new CreateBotRequest(" ", string.Empty, connection.Id, null));
        var elsewhere = await asOwner.Post($"{Space(alice.SpaceId)}/bots", new CreateBotRequest("Lost", string.Empty, Guid.CreateVersion7(), null));
        var listed = await GetFor<List<BotSummary>>(asMember, $"{Space(alice.SpaceId)}/bots");
        var second = await PostFor<BotSummary>(asOwner, $"{Space(alice.SpaceId)}/bots", new CreateBotRequest("Atlas", string.Empty, connection.Id, "other-model"), HttpStatusCode.Created);

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        bot.Should().Match<BotSummary>(found => found.Name == "Atlas" && found.Handle == "atlas" && found.Model == "scripted" && found.Status == "active");
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        unnamed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        elsewhere.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        listed.Should().ContainSingle(found => found.Id == bot.Id);
        second.Should().Match<BotSummary>(found => found.Handle == "atlas-2" && found.Model == "other-model");
    }

    #endregion

    #region Access

    [Fact]
    public async Task Another_member_cannot_read_or_post_to_a_private_thread_and_a_person_outside_the_space_gets_404()
    {
        script = new ScriptedModel([new TextOutput("Private answer.")]);
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var outsider = await SeedPerson("outsider");
        var asOwner = await SignIn(alice);
        var asMember = NewClient();
        await asMember.SignIn(member.Email, member.Password);
        var asOutsider = await SignIn(outsider);
        var bot = await CreateBot(asOwner, alice.SpaceId);
        var thread = await PostFor<ThreadSummary>(asOwner, $"{Space(alice.SpaceId)}/bots/{bot.Id}/threads", new CreateThreadRequest(null), HttpStatusCode.Created);
        var sent = await PostFor<SendMessageResponse>(asOwner, $"{Space(alice.SpaceId)}/threads/{thread.Id}/messages", new SendMessageRequest("A private question"), HttpStatusCode.Accepted);
        await WaitForRun(asOwner, alice.SpaceId, sent.Run.Id, "completed", "failed");

        var memberThread = await asMember.Get($"{Space(alice.SpaceId)}/threads/{thread.Id}");
        var memberMessages = await asMember.Get($"{Space(alice.SpaceId)}/threads/{thread.Id}/messages");
        var memberPost = await asMember.Post($"{Space(alice.SpaceId)}/threads/{thread.Id}/messages", new SendMessageRequest("Let me in"));
        var memberThreads = await GetFor<List<ThreadSummary>>(asMember, $"{Space(alice.SpaceId)}/bots/{bot.Id}/threads");
        var memberCancel = await asMember.Post($"{Space(alice.SpaceId)}/runs/{sent.Run.Id}/cancel");
        var memberRun = await asMember.Get($"{Space(alice.SpaceId)}/runs/{sent.Run.Id}");
        var admin = await SeedMember(alice.SpaceId, "admin", MembershipRole.Admin);
        var asAdmin = NewClient();
        await asAdmin.SignIn(admin.Email, admin.Password);
        var adminRun = await asAdmin.Get($"{Space(alice.SpaceId)}/runs/{sent.Run.Id}");
        var adminThread = await asAdmin.Get($"{Space(alice.SpaceId)}/threads/{thread.Id}");
        var outsiderThread = await asOutsider.Get($"{Space(alice.SpaceId)}/threads/{thread.Id}");
        var outsiderRun = await asOutsider.Get($"{Space(alice.SpaceId)}/runs/{sent.Run.Id}");
        var outsiderOwnSpace = await asOutsider.Get($"{Space(outsider.SpaceId)}/runs/{sent.Run.Id}");
        var outsiderCancel = await asOutsider.Post($"{Space(outsider.SpaceId)}/runs/{sent.Run.Id}/cancel");

        memberThread.StatusCode.Should().Be(HttpStatusCode.NotFound);
        memberMessages.StatusCode.Should().Be(HttpStatusCode.NotFound);
        memberPost.StatusCode.Should().Be(HttpStatusCode.NotFound);
        memberThreads.Should().BeEmpty();
        memberCancel.StatusCode.Should().Be(HttpStatusCode.NotFound, "a member who cannot see the thread learns nothing about its run, not even that it exists");
        memberRun.StatusCode.Should().Be(HttpStatusCode.NotFound, "a run id tells a member nothing about another member's private thread");
        adminRun.StatusCode.Should().Be(HttpStatusCode.OK, "an admin sees activity and cost");
        adminThread.StatusCode.Should().Be(HttpStatusCode.NotFound, "and still cannot read the private thread itself");
        outsiderThread.StatusCode.Should().Be(HttpStatusCode.NotFound);
        outsiderRun.StatusCode.Should().Be(HttpStatusCode.NotFound);
        outsiderOwnSpace.StatusCode.Should().Be(HttpStatusCode.NotFound, "a run id from another space finds nothing in one's own");
        outsiderCancel.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await GetFor<List<MessageResponse>>(asOwner, $"{Space(alice.SpaceId)}/threads/{thread.Id}/messages")).Should().HaveCount(2);
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

    private static Task<ModelConnectionSummary> CreateConnection(ApiClient client, Guid spaceId) =>
        PostFor<ModelConnectionSummary>(
            client,
            $"{Space(spaceId)}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted"),
            HttpStatusCode.Created);

    private static async Task<BotSummary> CreateBot(ApiClient client, Guid spaceId)
    {
        var connection = await CreateConnection(client, spaceId);
        return await PostFor<BotSummary>(client, $"{Space(spaceId)}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, null), HttpStatusCode.Created);
    }

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

    private static async Task<RunSummary> WaitForRun(ApiClient client, Guid spaceId, Guid runId, params string[] statuses)
    {
        var watch = Stopwatch.StartNew();

        while (true)
        {
            var run = await GetFor<RunSummary>(client, $"{Space(spaceId)}/runs/{runId}");

            if (statuses.Contains(run.Status))
            {
                return run;
            }

            (watch.Elapsed < patience).Should().BeTrue($"run {runId} should reach {string.Join(" or ", statuses)}; it is {run.Status}");
            await Task.Delay(25, Token);
        }
    }

    #endregion
}
