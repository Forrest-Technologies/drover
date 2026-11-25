namespace Drover.Server.Tests;

/// <summary>
/// What a thread has for the person asking, through the API: it is on every thread they read, it changes on their
/// other screens through the signal stream, opening a thread clears it, and a failed run can be tried again.
/// </summary>
public sealed class AttentionApiTests(PostgresServer postgres) : ServerTestBase(postgres)
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

    #region Seen

    [Fact]
    public async Task Opening_a_thread_on_one_screen_clears_new_on_the_other_within_a_second()
    {
        script = new ScriptedModel([new TextOutput("Here is your answer.")]);
        var alice = await SeedPerson("alice");
        var phone = await Api(alice);
        var laptop = await Api(alice);
        var bot = await CreateBot(phone, alice.SpaceId);
        var thread = await phone.Api.CreateThread(alice.SpaceId, bot.Id, cancellationToken: Token);
        List<UserSignal> heard = [];
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var watching = Listen(laptop, alice.SpaceId, heard, stop.Token);
        await Task.Delay(300, Token);

        var sent = await phone.Api.SendMessage(alice.SpaceId, thread.Id, "Answer me", cancellationToken: Token);
        await WaitForRun(phone, alice.SpaceId, sent.Run.Id, "completed");
        var unseen = await laptop.Api.GetThread(alice.SpaceId, thread.Id, Token);
        var clock = Stopwatch.StartNew();
        await phone.Api.MarkThreadSeen(alice.SpaceId, thread.Id, Token);
        await Until(() => Snapshot(heard).Any(signal => signal.Kind == UserSignalKinds.ThreadSeen && signal.ThreadId == thread.Id));
        var heardAfter = clock.Elapsed;
        var seen = await laptop.Api.GetThread(alice.SpaceId, thread.Id, Token);
        await stop.CancelAsync();
        await Task.WhenAny(watching);

        unseen.Attention.Should().Be(new ThreadAttention(false, false, true, null));
        seen.Attention.Should().Be(new ThreadAttention(false, false, false, null));
        heardAfter.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Only_a_thread_the_caller_can_see_can_be_marked_seen()
    {
        script = new ScriptedModel([new TextOutput("Private.")]);
        var alice = await SeedPerson("alice");
        var bob = await SeedMember(alice.SpaceId, "bob", MembershipRole.Member);
        var mallory = await SeedPerson("mallory");
        var alices = await Api(alice);
        var bot = await CreateBot(alices, alice.SpaceId);
        var thread = await alices.Api.CreateThread(alice.SpaceId, bot.Id, cancellationToken: Token);
        var bobs = await Api((bob.Email, bob.Password, bob.UserId, alice.SpaceId));
        var mallorys = await Api(mallory);

        var byMember = () => bobs.Api.MarkThreadSeen(alice.SpaceId, thread.Id, Token);
        var byStranger = () => mallorys.Api.MarkThreadSeen(mallory.SpaceId, thread.Id, Token);

        (await byMember.Should().ThrowAsync<DroverApiException>()).Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await byStranger.Should().ThrowAsync<DroverApiException>()).Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await alices.Api.MarkThreadSeen(alice.SpaceId, thread.Id, Token);
    }

    #endregion

    #region Failed

    [Fact]
    public async Task A_failed_run_says_its_cause_and_trying_again_answers_the_same_message_once()
    {
        script = new ScriptedModel([new TextOutput("Reached it this time.")]);
        var alice = await SeedPerson("alice");
        var api = await Api(alice);
        var bot = await CreateBot(api, alice.SpaceId);
        var thread = await api.Api.CreateThread(alice.SpaceId, bot.Id, cancellationToken: Token);
        model!.FailNext = ModelProviderException.FromStatus(503);

        var sent = await api.Api.SendMessage(alice.SpaceId, thread.Id, "Hello", cancellationToken: Token);
        await WaitForRun(api, alice.SpaceId, sent.Run.Id, "failed");
        var failed = (await api.Api.GetThread(alice.SpaceId, thread.Id, Token)).Attention!;
        var retry = await api.Api.RetryRun(alice.SpaceId, sent.Run.Id, Token);
        await WaitForRun(api, alice.SpaceId, retry.Id, "completed");
        var after = (await api.Api.GetThread(alice.SpaceId, thread.Id, Token)).Attention!;
        var again = () => api.Api.RetryRun(alice.SpaceId, sent.Run.Id, Token);
        var messages = await api.Api.ListMessages(alice.SpaceId, thread.Id, Token);

        failed.Failed.Should().BeTrue();
        failed.LastFailure.Should().Match<RunFailure>(failure =>
            failure.RunId == sent.Run.Id && failure.Cause == FailureCauses.ModelUnreachable && failure.Reported.StartsWith("unreachable: "));
        retry.Status.Should().Be("queued");
        after.LastFailure.Should().BeNull();
        (await again.Should().ThrowAsync<DroverApiException>()).Which.StatusCode.Should().Be(HttpStatusCode.Conflict);
        messages.Select(message => message.Role).Should().Equal(["user", "assistant"], "the retry answered the message that was there");
    }

    [Fact]
    public async Task Another_member_cannot_retry_a_run_that_acts_for_someone_else()
    {
        script = new ScriptedModel([new TextOutput("Never said.")]);
        var alice = await SeedPerson("alice");
        var bob = await SeedMember(alice.SpaceId, "bob", MembershipRole.Admin);
        var alices = await Api(alice);
        var bot = await CreateBot(alices, alice.SpaceId);
        var thread = await alices.Api.CreateThread(alice.SpaceId, bot.Id, cancellationToken: Token);
        model!.FailNext = ModelProviderException.FromStatus(503);
        var sent = await alices.Api.SendMessage(alice.SpaceId, thread.Id, "Hello", cancellationToken: Token);
        await WaitForRun(alices, alice.SpaceId, sent.Run.Id, "failed");
        var bobs = await Api((bob.Email, bob.Password, bob.UserId, alice.SpaceId));

        var byAdmin = () => bobs.Api.RetryRun(alice.SpaceId, sent.Run.Id, Token);

        (await byAdmin.Should().ThrowAsync<DroverApiException>()).Which.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the run's thread is private to alice, so to anyone else the run is not there");
    }

    [Fact]
    public async Task A_run_stopped_by_step_limit_or_token_limit_carries_its_limit_kind_used_and_cap_in_the_thread_summary()
    {
        script = new ScriptedModel(
            [new ToolCallOutput("c1", "time_now")],
            [new ToolCallOutput("c2", "time_now")],
            [new ToolCallOutput("t1", "time_now")],
            [new ToolCallOutput("t2", "time_now")]);
        var alice = await SeedPerson("alice");
        var api = await Api(alice);
        var bot = await CreateBot(api, alice.SpaceId);

        await api.Api.UpdateBotDefinition(
            alice.SpaceId,
            bot.Id,
            new UpdateBotRequest(null, null, null, null, null, null, null, null, null, null, null, null, null, new RunLimitsBody(2, null, null)),
            Token);
        var stepThread = await api.Api.CreateThread(alice.SpaceId, bot.Id, cancellationToken: Token);
        var stepSent = await api.Api.SendMessage(alice.SpaceId, stepThread.Id, "Loop steps", cancellationToken: Token);
        await WaitForRun(api, alice.SpaceId, stepSent.Run.Id, "failed");
        var stepAttention = (await api.Api.GetThread(alice.SpaceId, stepThread.Id, Token)).Attention!;

        await api.Api.UpdateBotDefinition(
            alice.SpaceId,
            bot.Id,
            new UpdateBotRequest(null, null, null, null, null, null, null, null, null, null, null, null, null, new RunLimitsBody(null, 20, null)),
            Token);
        var tokenThread = await api.Api.CreateThread(alice.SpaceId, bot.Id, cancellationToken: Token);
        var tokenSent = await api.Api.SendMessage(alice.SpaceId, tokenThread.Id, "Loop tokens", cancellationToken: Token);
        await WaitForRun(api, alice.SpaceId, tokenSent.Run.Id, "failed");
        var tokenAttention = (await api.Api.GetThread(alice.SpaceId, tokenThread.Id, Token)).Attention!;

        stepAttention.Failed.Should().BeTrue();
        stepAttention.LastFailure.Should().Match<RunFailure>(failure =>
            failure.RunId == stepSent.Run.Id
            && failure.Cause == FailureCauses.BudgetReached
            && failure.Limit == new RunFailureLimit(FailureLimits.ModelCalls, 2, 2));
        tokenAttention.Failed.Should().BeTrue();
        tokenAttention.LastFailure.Should().Match<RunFailure>(failure =>
            failure.RunId == tokenSent.Run.Id
            && failure.Cause == FailureCauses.BudgetReached
            && failure.Limit == new RunFailureLimit(FailureLimits.Tokens, 30, 20));
    }

    #endregion

    #region Helpers

    /// <summary>One screen of a signed-in person: the typed client the interface uses, over the person's cookies.</summary>
    private async Task<Screen> Api((string Email, string Password, Guid UserId, Guid SpaceId) person)
    {
        var client = NewClient();
        (await client.SignIn(person.Email, person.Password)).EnsureSuccessStatusCode();
        (await client.Get("/api/antiforgery")).EnsureSuccessStatusCode();
        return new Screen(client, new DroverApi(NewRelay(client)) { AntiforgeryToken = client.Cookie("XSRF-TOKEN") });
    }

    private static async Task<BotSummary> CreateBot(Screen screen, Guid spaceId)
    {
        var created = await screen.Client.Post(
            $"/api/spaces/{spaceId}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted"));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Token));
        var connection = (await created.Content.ReadFromJsonAsync<ModelConnectionSummary>(Token))!;
        return await screen.Api.CreateBot(spaceId, new CreateBotRequest("Helper", "Answer briefly.", connection.Id, null), Token);
    }

    private static Task Listen(Screen screen, Guid spaceId, List<UserSignal> heard, CancellationToken stop) =>
        Task.Run(async () =>
        {
            await foreach (var signal in screen.Api.WatchSignals(spaceId, cancellationToken: stop))
            {
                lock (heard)
                {
                    heard.Add(signal);
                }
            }
        }, Token);

    private static async Task<RunSummary> WaitForRun(Screen screen, Guid spaceId, Guid runId, string status)
    {
        var watch = Stopwatch.StartNew();

        while (true)
        {
            var run = await screen.Api.GetRun(spaceId, runId, Token);

            if (run.Status == status)
            {
                return run;
            }

            (watch.Elapsed < patience).Should().BeTrue($"run {runId} should become {status}; it is {run.Status}");
            await Task.Delay(25, Token);
        }
    }

    private static async Task Until(Func<bool> reached)
    {
        var watch = Stopwatch.StartNew();

        while (!reached())
        {
            (watch.Elapsed < patience).Should().BeTrue("the awaited thing should happen");
            await Task.Delay(10, Token);
        }
    }

    private static List<T> Snapshot<T>(List<T> shared)
    {
        lock (shared)
        {
            return [.. shared];
        }
    }

    private sealed record Screen(ApiClient Client, DroverApi Api);

    #endregion
}
