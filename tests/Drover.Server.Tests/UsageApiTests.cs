namespace Drover.Server.Tests;

/// <summary>
/// Who reads what through the usage endpoints: an owner reads the whole space, a member only their own runs, a
/// private conversation's runs reach an admin only as a total, and another space reads nothing. Runs and their model
/// calls are seeded as rows; the bots, connections, and threads are made through the API. Issue 163.
/// </summary>
public sealed class UsageApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private const string Week = $"period={UsagePeriods.Week}&zone=Europe/Berlin";

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(_ => new ScriptedModelProvider(new ScriptedModel()));

    #endregion

    #region Who Reads What

    [Fact]
    public async Task A_member_reads_only_their_own_runs_and_an_owner_reads_the_space_but_not_a_private_conversation_s_runs()
    {
        var (owner, session, _) = await SetUpFirstAccount();
        var spaceId = session.Spaces[0].Id;
        var (email, password, memberId) = await SeedMember(spaceId, "member", MembershipRole.Member);
        var member = NewClient();
        (await member.SignIn(email, password)).EnsureSuccessStatusCode();
        var bot = await CreateBot(owner, spaceId);
        var ownerThread = await PostFor<ThreadSummary>(owner, $"{Space(spaceId)}/bots/{bot.Id}/threads", new CreateThreadRequest(null));
        var memberThread = await PostFor<ThreadSummary>(member, $"{Space(spaceId)}/bots/{bot.Id}/threads", new CreateThreadRequest("Private plans"));
        var ownerRun = await SeedRunWithCall(spaceId, session.UserId, ownerThread.Id, bot.Id, 1.00m);
        var memberRun = await SeedRunWithCall(spaceId, memberId, memberThread.Id, bot.Id, 2.00m);

        var asMember = await GetFor<CrewUsage>(member, $"{Space(spaceId)}/usage?{Week}");
        var asOwner = await GetFor<CrewUsage>(owner, $"{Space(spaceId)}/usage?{Week}");
        var ownerRuns = await GetFor<BotRunsUsage>(owner, $"{Space(spaceId)}/usage/bots/{bot.Id}/runs?{Week}");
        var memberRuns = await GetFor<BotRunsUsage>(member, $"{Space(spaceId)}/usage/bots/{bot.Id}/runs?{Week}");

        asMember.Crew.Should().ContainSingle().Which.Own.Cost.Should().Be(2.00m);
        asOwner.Crew.Should().ContainSingle().Which.Own.Cost.Should().Be(3.00m);
        memberRuns.Runs.Should().ContainSingle().Which.RunId.Should().Be(memberRun);
        memberRuns.Unopened.Should().BeNull();
        ownerRuns.Runs.Should().ContainSingle().Which.RunId.Should().Be(ownerRun);
        ownerRuns.Unopened.Should().Be(new UnopenedRuns(1, new UsageFigure(2.00m, 100, 10)));
        var body = await (await owner.Get($"{Space(spaceId)}/usage/bots/{bot.Id}/runs?{Week}")).Content.ReadAsStringAsync(Token);
        body.Should().NotContain(memberRun.ToString()).And.NotContain(memberThread.Id.ToString()).And.NotContain("Private plans");

        (await GetFor<RunUsageDetail>(member, $"{Space(spaceId)}/runs/{memberRun}/usage")).Own.Cost.Should().Be(2.00m);
        (await GetFor<RunUsageDetail>(owner, $"{Space(spaceId)}/runs/{ownerRun}/usage")).Own.Cost.Should().Be(1.00m);
        (await owner.Get($"{Space(spaceId)}/runs/{memberRun}/usage")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await member.Get($"{Space(spaceId)}/runs/{ownerRun}/usage")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_space_reads_nothing_of_this_one()
    {
        var (owner, session, _) = await SetUpFirstAccount();
        var spaceId = session.Spaces[0].Id;
        var bot = await CreateBot(owner, spaceId);
        var thread = await PostFor<ThreadSummary>(owner, $"{Space(spaceId)}/bots/{bot.Id}/threads", new CreateThreadRequest(null));
        var runId = await SeedRunWithCall(spaceId, session.UserId, thread.Id, bot.Id, 1.00m);
        var (email, password, _, strangerSpace) = await SeedPerson("stranger");
        var stranger = NewClient();
        (await stranger.SignIn(email, password)).EnsureSuccessStatusCode();

        (await stranger.Get($"{Space(spaceId)}/usage?{Week}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.Get($"{Space(spaceId)}/usage/bots/{bot.Id}/runs?{Week}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.Get($"{Space(spaceId)}/runs/{runId}/usage")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await GetFor<CrewUsage>(stranger, $"{Space(strangerSpace)}/usage?{Week}")).Crew.Should().BeEmpty();
        (await GetFor<BotRunsUsage>(stranger, $"{Space(strangerSpace)}/usage/bots/{bot.Id}/runs?{Week}"))
            .Should().Match<BotRunsUsage>(runs => runs.Runs.Count == 0 && runs.Unopened == null);
        (await stranger.Get($"{Space(strangerSpace)}/runs/{runId}/usage")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_period_or_time_zone_it_does_not_know_is_refused_with_a_sentence()
    {
        var (owner, session, _) = await SetUpFirstAccount();
        var spaceId = session.Spaces[0].Id;

        var badPeriod = await owner.Get($"{Space(spaceId)}/usage?period=year&zone=UTC");
        var badZone = await owner.Get($"{Space(spaceId)}/usage?period=today&zone=Mars/Olympus");
        var noZone = await owner.Get($"{Space(spaceId)}/usage?period=today");

        badPeriod.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await badPeriod.Content.ReadAsStringAsync(Token)).Should().Contain("Choose today, 7d, or 30d");
        badZone.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        noZone.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Helpers

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    private static async Task<BotSummary> CreateBot(ApiClient client, Guid spaceId)
    {
        var connection = await PostFor<ModelConnectionSummary>(
            client,
            $"{Space(spaceId)}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted"));
        return await PostFor<BotSummary>(client, $"{Space(spaceId)}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, null));
    }

    /// <summary>A finished run that acts for <paramref name="userId"/>, with one model call of the given cost. Returns the run's id.</summary>
    private async Task<Guid> SeedRunWithCall(Guid spaceId, Guid userId, Guid threadId, Guid botId, decimal cost)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var db = scope.ServiceProvider.GetRequiredService<DroverDbContext>();
        var connectionId = await db.Bots.Where(bot => bot.Id == botId).Select(bot => bot.ModelConnectionId).SingleAsync(Token);
        var run = new Run { ThreadId = threadId, BotId = botId, PrincipalUserId = userId, Status = RunStatus.Completed };
        run.RootRunId = run.Id;
        db.Runs.Add(run);
        db.UsageRecords.Add(new UsageRecord
        {
            RunId = run.Id,
            RootRunId = run.Id,
            BotId = botId,
            ModelConnectionId = connectionId,
            Model = "scripted",
            InputTokens = 100,
            OutputTokens = 10,
            Cost = cost,
            CreatedAt = Time.GetUtcNow(),
        });
        await work.Commit(Token);
        return run.Id;
    }

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
