namespace Drover.Server.Tests;

/// <summary>
/// A schedule saved through the API comes due and is sent, over the in-process server and real PostgreSQL: its run
/// is an ordinary run, readable through the thread endpoints, and its message says it was sent by the schedule. Run once
/// now, it is an ordinary run of the person who asked.
/// </summary>
public sealed class ScheduleApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private const string Prompt = "Brief me on what changed overnight.";

    private const string Cron = "0 9 * * *";

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    private readonly ScriptedModel script = new([new TextOutput("Nothing changed overnight.")]);

    private ScriptedModelProvider? model;

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(script));

    #endregion

    #region Firing

    [Fact]
    public async Task A_schedule_saved_through_the_api_fires_and_its_run_and_message_are_read_through_the_thread_endpoints()
    {
        var (email, password, userId, spaceId) = await SeedPerson("alice");
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        var botId = await CreateBot(client, spaceId);
        var schedules = $"{Space(spaceId)}/bots/{botId}/schedules";
        var saved = await PostFor<BotScheduleResponse>(client, schedules, new BotScheduleBody(Cron, "UTC", Prompt, "new_thread", true), HttpStatusCode.Created);

        await MakeDue(spaceId, userId, saved.Id);
        await Services.GetRequiredService<ScheduleRunner>().SendDue(Token);
        var thread = (await GetFor<List<ThreadSummary>>(client, $"{Space(spaceId)}/bots/{botId}/threads")).Should().ContainSingle().Which;
        var run = await WaitForCompleted(client, spaceId, (await LastOf(client, schedules)).LastRunId!.Value);
        var messages = await GetFor<List<MessageResponse>>(client, $"{Space(spaceId)}/threads/{thread.Id}/messages");
        var after = await LastOf(client, schedules);

        thread.Title.Should().Be(Prompt);
        thread.IsMain.Should().BeFalse("this schedule sends to a new conversation each time");
        run.ThreadId.Should().Be(thread.Id);
        messages.Select(message => message.Role).Should().Equal("user", "assistant");
        messages[0].Should().Match<MessageResponse>(sent => sent.ScheduleId == saved.Id && sent.ScheduleCron == Cron && sent.Parts[0].Text == Prompt);
        messages[1].ScheduleId.Should().BeNull("the bot's reply is the bot's own");
        after.Should().Match<BotScheduleResponse>(schedule =>
            schedule.LastRunId == run.Id && schedule.LastOutcome == "completed" && schedule.LastRunAt != null && schedule.NextRunAt == saved.NextRunAt);
        var stored = await ReadRun(spaceId, userId, run.Id);
        stored.Trigger.Should().Be(RunTrigger.Schedule);
        stored.PrincipalUserId.Should().Be(userId);
    }

    #endregion

    #region Running Once

    [Fact]
    public async Task Running_a_schedule_once_is_for_owners_and_admins_runs_as_the_one_who_asked_and_leaves_its_next_time()
    {
        var (email, password, aliceId, spaceId) = await SeedPerson("alice");
        var alice = NewClient();
        (await alice.SignIn(email, password)).EnsureSuccessStatusCode();
        var botId = await CreateBot(alice, spaceId);
        var schedules = $"{Space(spaceId)}/bots/{botId}/schedules";
        var saved = await PostFor<BotScheduleResponse>(alice, schedules, new BotScheduleBody(Cron, "UTC", Prompt, "new_thread", true), HttpStatusCode.Created);
        var (bobEmail, bobPassword, bobId) = await SeedMember(spaceId, "bob", MembershipRole.Admin);
        var bob = NewClient();
        (await bob.SignIn(bobEmail, bobPassword)).EnsureSuccessStatusCode();
        var (carolEmail, carolPassword, _) = await SeedMember(spaceId, "carol", MembershipRole.Member);
        var carol = NewClient();
        (await carol.SignIn(carolEmail, carolPassword)).EnsureSuccessStatusCode();

        var byMember = await carol.Post($"{schedules}/{saved.Id}/run-once", null);
        var unknown = await bob.Post($"{schedules}/{Guid.CreateVersion7()}/run-once", null);
        var started = await PostFor<ScheduleRunOnceResponse>(bob, $"{schedules}/{saved.Id}/run-once", null, HttpStatusCode.Accepted);
        var run = await WaitForCompleted(bob, spaceId, started.RunId!.Value);
        var bobsThreads = await GetFor<List<ThreadSummary>>(bob, $"{Space(spaceId)}/bots/{botId}/threads");
        var alicesThreads = await GetFor<List<ThreadSummary>>(alice, $"{Space(spaceId)}/bots/{botId}/threads");
        var messages = await GetFor<List<MessageResponse>>(bob, $"{Space(spaceId)}/threads/{started.ThreadId}/messages");
        var after = await LastOf(alice, schedules);

        byMember.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a member reads routines and does not run them");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        run.ThreadId.Should().Be(started.ThreadId);
        bobsThreads.Should().ContainSingle(thread => thread.Id == started.ThreadId);
        alicesThreads.Should().NotContain(thread => thread.Id == started.ThreadId, "the conversation is bob's own");
        messages[0].Should().Match<MessageResponse>(sent => sent.ScheduleId == saved.Id && sent.Parts[0].Text == Prompt);
        var stored = await ReadRun(spaceId, bobId, run.Id);
        stored.PrincipalUserId.Should().Be(bobId, "a run started by hand acts for the person who started it, never the schedule's owner");
        stored.PrincipalUserId.Should().NotBe(aliceId);
        after.Should().Match<BotScheduleResponse>(schedule =>
            schedule.NextRunAt == saved.NextRunAt && schedule.LastRunId == null && schedule.LastRunAt == null && schedule.LastOutcome == null);
    }

    [Fact]
    public async Task Running_a_schedule_of_a_paused_bot_once_says_why_in_a_sentence()
    {
        var (email, password, _, spaceId) = await SeedPerson("alice");
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        var botId = await CreateBot(client, spaceId);
        var schedules = $"{Space(spaceId)}/bots/{botId}/schedules";
        var saved = await PostFor<BotScheduleResponse>(client, schedules, new BotScheduleBody(Cron, "UTC", Prompt, "new_thread", true), HttpStatusCode.Created);
        (await client.Post($"{Space(spaceId)}/bots/{botId}/pause", null)).EnsureSuccessStatusCode();

        var refused = await client.Post($"{schedules}/{saved.Id}/run-once", null);

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString().Should().Be("Briefer is paused, so its routines do not run. Resume it first.");
    }

    [Fact]
    public async Task A_time_is_checked_before_it_is_saved_with_the_servers_sentence_or_its_next_times()
    {
        var (email, password, _, spaceId) = await SeedPerson("alice");
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        var botId = await CreateBot(client, spaceId);
        var check = $"{Space(spaceId)}/bots/{botId}/schedules/check";

        var valid = await PostFor<ScheduleCheckResponse>(client, check, new ScheduleCheckBody("*/20 9-17 * * 1-5", "Europe/Berlin"), HttpStatusCode.OK);
        var invalid = await PostFor<ScheduleCheckResponse>(client, check, new ScheduleCheckBody("61 8 * * *", "UTC"), HttpStatusCode.OK);
        var badZone = await PostFor<ScheduleCheckResponse>(client, check, new ScheduleCheckBody("0 8 * * *", "Mars/Olympus"), HttpStatusCode.OK);

        valid.Problem.Should().BeNull();
        valid.Next.Should().HaveCount(3).And.BeInAscendingOrder();
        valid.Next.Should().OnlyContain(time => time > Time.GetUtcNow() && time.Minute % 20 == 0);
        invalid.Problem.Should().StartWith("Write the time as five cron fields");
        invalid.Next.Should().BeEmpty();
        badZone.Problem.Should().StartWith("Choose a time zone by its IANA name");
        (await GetFor<List<BotScheduleResponse>>(client, $"{Space(spaceId)}/bots/{botId}/schedules")).Should().BeEmpty("checking saves nothing");
    }

    #endregion

    #region Private Methods

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    private static async Task<Guid> CreateBot(ApiClient client, Guid spaceId)
    {
        var connection = await PostFor<ModelConnectionSummary>(
            client,
            $"{Space(spaceId)}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted"),
            HttpStatusCode.Created);
        var bot = await PostFor<BotSummary>(client, $"{Space(spaceId)}/bots", new CreateBotRequest("Briefer", "Brief me.", connection.Id, null), HttpStatusCode.Created);
        return bot.Id;
    }

    private static async Task<BotScheduleResponse> LastOf(ApiClient client, string schedules) =>
        (await GetFor<List<BotScheduleResponse>>(client, schedules)).Single();

    private static async Task<RunSummary> WaitForCompleted(ApiClient client, Guid spaceId, Guid runId)
    {
        var watch = Stopwatch.StartNew();

        while (true)
        {
            var run = await GetFor<RunSummary>(client, $"{Space(spaceId)}/runs/{runId}");

            if (run.Status is "completed")
            {
                return run;
            }

            (watch.Elapsed < patience).Should().BeTrue($"the scheduled run should complete; it is {run.Status}");
            await Task.Delay(50, Token);
        }
    }

    /// <summary>
    /// The time the schedule is due has passed: its next_run_at is put a minute behind the server's clock, as if the
    /// server had been waiting for it since it was saved. A day of the server's clock is not moved, so the session
    /// stays signed in.
    /// </summary>
    private Task<int> MakeDue(Guid spaceId, Guid userId, Guid scheduleId) =>
        InSpace(spaceId, userId, db => db.BotSchedules
            .Where(schedule => schedule.Id == scheduleId)
            .ExecuteUpdateAsync(set => set.SetProperty(schedule => schedule.NextRunAt, Time.GetUtcNow().AddMinutes(-1)), Token));

    /// <summary>The stored run, read as its own person in its space: the contract does not carry its trigger.</summary>
    private Task<Run> ReadRun(Guid spaceId, Guid userId, Guid runId) =>
        InSpace(spaceId, userId, db => db.Runs.AsNoTracking().SingleAsync(found => found.Id == runId, Token));

    private async Task<T> InSpace<T>(Guid spaceId, Guid userId, Func<DroverDbContext, Task<T>> work)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var transaction = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var result = await work(scope.ServiceProvider.GetRequiredService<DroverDbContext>());
        await transaction.Commit(Token);
        return result;
    }

    private static async Task<T> PostFor<T>(ApiClient client, string path, object? body, HttpStatusCode expected)
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
