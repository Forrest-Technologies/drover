namespace Drover.Server.Tests;

/// <summary>
/// A schedule saved through the API comes due and is sent, over the in-process server and real PostgreSQL: its run
/// is an ordinary run, readable through the thread endpoints, and its message says it was sent by the schedule.
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
