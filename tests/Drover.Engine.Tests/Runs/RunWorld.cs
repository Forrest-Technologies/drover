namespace Drover.Engine.Tests.Runs;

/// <summary>
/// One database with one person, their space, a model connection to the scripted provider, and a bot, made through
/// the engine's own services the way the product makes them. Hosts started from it share only the database, the
/// model, and the tools.
/// </summary>
public sealed class RunWorld : IAsyncDisposable
{
    #region Private Fields

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    private readonly TestDatabase database;

    private readonly string keyRing = Path.Combine(Path.GetTempPath(), $"drover-keys-{Guid.NewGuid():N}");

    private readonly List<EngineHost> hosts = [];

    private readonly EngineHost seeder;

    #endregion

    #region Constructors

    private RunWorld(TestDatabase database, ScriptedModel script, TestTools tools, string? modelKey)
    {
        this.database = database;
        ModelKey = modelKey ?? NewModelKey();
        Script = script;
        Tools = tools;
        Model = new ScriptedModelProvider(script);
        seeder = NewHost();
    }

    #endregion

    #region Properties

    public ScriptedModel Script { get; }

    public ScriptedModelProvider Model { get; }

    public TestTools Tools { get; }

    public Guid SpaceId { get; } = Guid.CreateVersion7();

    public Guid UserId { get; } = Guid.CreateVersion7();

    public Guid ConnectionId { get; private set; }

    public Guid BotId { get; private set; }

    /// <summary>The key of the model connection, made for this run of the tests. It must never be stored or emitted.</summary>
    public string ModelKey { get; }

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    #endregion

    #region Public Methods

    public static string NewModelKey() => $"sk-{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}";

    public static async Task<RunWorld> Create(PostgresServer postgres, ScriptedModel script, TestTools? tools = null, string? modelKey = null)
    {
        var world = new RunWorld(await postgres.CreateDatabase(), script, tools ?? new TestTools(), modelKey);
        await world.Seed();
        return world;
    }

    /// <summary>A new server process that executes runs.</summary>
    public EngineHost StartHost(Action<RunOptions>? configure = null, Action<IServiceCollection>? replaceServices = null) =>
        NewHost(configure, replaceServices).Start();

    public Task<T> Transact<T>(Func<IServiceProvider, Task<T>> work) => TransactAs(SpaceId, UserId, work);

    public async Task<T> TransactAs<T>(Guid spaceId, Guid userId, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = seeder.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var transaction = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var result = await work(scope.ServiceProvider);
        await transaction.Commit(Token);
        return result;
    }

    public Task<T> Read<T>(Func<DroverDbContext, Task<T>> query) =>
        Transact(services => query(services.GetRequiredService<DroverDbContext>()));

    /// <summary>Gives the bot a grant for a tool, as a person would in the bot editor.</summary>
    public Task Grant(string toolName, GrantEffect effect) =>
        Read(async db =>
        {
            var tool = await db.Tools.SingleAsync(found => found.Name == toolName, Token);
            db.ToolGrants.Add(new ToolGrant { BotId = BotId, ToolId = tool.Id, Effect = effect, GrantedByUserId = UserId });
            return true;
        });

    public Task Revoke(string toolName) =>
        Read(async db =>
        {
            var tool = await db.Tools.SingleAsync(found => found.Name == toolName, Token);
            return await db.ToolGrants.Where(grant => grant.BotId == BotId && grant.ToolId == tool.Id).ExecuteDeleteAsync(Token);
        });

    public async Task<Guid> NewThread() =>
        (await Transact(services => services.GetRequiredService<ThreadService>().Create(BotId, null, UserId, Token)))!.Id;

    /// <summary>Posts a message through the engine, as the API does, on any host. Returns the run it started.</summary>
    public async Task<Guid> Post(Guid threadId, string text, EngineHost? through = null)
    {
        var post = await PostRaw(threadId, text, through);
        post.Problem.Should().Be(PostProblem.None, post.Detail);
        return post.Run!.Id;
    }

    public async Task<MessagePost> PostRaw(Guid threadId, string text, EngineHost? through = null)
    {
        await using var scope = (through ?? seeder).Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(SpaceId, UserId);
        return await scope.ServiceProvider.GetRequiredService<RunService>().PostMessage(threadId, text, UserId, Token);
    }

    public async Task<RunCancel> Cancel(Guid runId, EngineHost through)
    {
        await using var scope = through.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(SpaceId, UserId);
        return await scope.ServiceProvider.GetRequiredService<RunService>().Cancel(runId, UserId, isSpaceAdmin: false, Token);
    }

    public Task<Run> ReadRun(Guid runId) => Read(db => db.Runs.AsNoTracking().SingleAsync(run => run.Id == runId, Token));

    public async Task<Run> WaitForStatus(Guid runId, params RunStatus[] statuses)
    {
        var watch = Stopwatch.StartNew();

        while (true)
        {
            var run = await ReadRun(runId);

            if (statuses.Contains(run.Status))
            {
                return run;
            }

            (watch.Elapsed < patience).Should().BeTrue($"run {runId} should reach {string.Join(" or ", statuses)}; it is {run.Status}");
            await Task.Delay(25, Token);
        }
    }

    /// <summary>Waits until the run's log holds an event the predicate accepts, as a watching client would have seen it.</summary>
    public async Task<RunEventResponse> WaitForEvent(Guid rootRunId, Func<RunEventResponse, bool> wanted)
    {
        var watch = Stopwatch.StartNew();

        while (true)
        {
            if ((await ReadEvents(rootRunId)).FirstOrDefault(wanted) is { } found)
            {
                return found;
            }

            (watch.Elapsed < patience).Should().BeTrue($"run {rootRunId} should write the awaited event");
            await Task.Delay(25, Token);
        }
    }

    /// <summary>
    /// Time passes for a dead host's lease: it is made to have expired, again and again until another worker holds
    /// the run, because a write the dead host had in flight may still land a moment after it died.
    /// </summary>
    public async Task ExpireLeaseOf(string deadWorkerId, Guid runId)
    {
        var watch = Stopwatch.StartNew();

        while (true)
        {
            var expired = await Read(db => db.Database.ExecuteSqlAsync(
                $"UPDATE runs SET lease_expires_at = now() - interval '1 second' WHERE id = {runId} AND lease_owner = {deadWorkerId} AND status = 'running'",
                Token));
            var run = await ReadRun(runId);

            if (expired == 0 && run.LeaseOwner != deadWorkerId)
            {
                return;
            }

            (watch.Elapsed < patience).Should().BeTrue("another worker should take over a run whose lease expired");
            await Task.Delay(50, Token);
        }
    }

    public async Task<IReadOnlyList<RunEventResponse>> ReadEvents(Guid rootRunId) =>
        [
            .. (await Transact(services => services.GetRequiredService<RunEventRepository>().ListEvents(rootRunId, 0, 10_000, Token)))
                .Select(stored => stored.ToResponse()),
        ];

    public Task<List<Message>> ReadMessages(Guid threadId) =>
        Read(db => db.Messages.AsNoTracking().Where(message => message.ThreadId == threadId).OrderBy(message => message.Position).ToListAsync(Token));

    public Task<List<ToolCall>> ReadToolCalls(Guid runId) =>
        Read(db => db.ToolCalls.AsNoTracking().Where(call => call.RunId == runId).ToListAsync(Token));

    public Task<List<UsageRecord>> ReadUsage(Guid runId) =>
        Read(db => db.UsageRecords.AsNoTracking().Where(usage => usage.RunId == runId).ToListAsync(Token));

    public static T Data<T>(RunEventResponse stored) => RunEventTypes.Read<T>(stored);

    public static IReadOnlyList<string> Summaries(IReadOnlyList<Message> messages) =>
        [.. messages.Select(message => $"{message.Role.ToString().ToLowerInvariant()}: {string.Join(" | ", message.Parts.Select(Summary))}")];

    #endregion

    #region Interface Implementations

    public async ValueTask DisposeAsync()
    {
        foreach (var host in hosts)
        {
            await host.DisposeAsync();
        }

        await database.DisposeAsync();

        if (Directory.Exists(keyRing))
        {
            Directory.Delete(keyRing, recursive: true);
        }
    }

    #endregion

    #region Private Methods

    private EngineHost NewHost(Action<RunOptions>? configure = null, Action<IServiceCollection>? replaceServices = null)
    {
        var host = new EngineHost(database.AppConnectionString, keyRing, Model, Tools.All, configure, replaceServices);
        hosts.Add(host);
        return host;
    }

    private async Task Seed()
    {
        await Read(db =>
        {
            db.Users.Add(new User
            {
                Id = UserId,
                UserName = $"user-{UserId:N}",
                NormalizedUserName = $"USER-{UserId:N}".ToUpperInvariant(),
                Email = $"{UserId:N}@example.test",
                NormalizedEmail = $"{UserId:N}@EXAMPLE.TEST".ToUpperInvariant(),
                DisplayName = "Test user",
            });
            db.Spaces.Add(new Space { Id = SpaceId, Kind = SpaceKind.Personal, Name = "Personal", Slug = $"personal-{SpaceId:N}" });
            db.Memberships.Add(new Membership { UserId = UserId, Role = MembershipRole.Owner });
            return Task.FromResult(true);
        });

        ConnectionId = await Transact(async services =>
        {
            var change = await services.GetRequiredService<ModelConnectionService>()
                .Create(ScriptedModelProvider.ConnectionKind, "Scripted", null, ModelKey, "scripted", Token);
            change.Problem.Should().BeNull();
            services.GetRequiredService<DroverDbContext>().ModelCatalog.Add(new ModelCatalogEntry
            {
                ModelConnectionId = change.Connection!.Id,
                Model = "scripted",
                DisplayName = "Scripted",
                Pricing = new ModelPricing { InputPerMillion = 2_000_000m, OutputPerMillion = 4_000_000m },
            });
            return change.Connection.Id;
        });

        BotId = await Transact(async services =>
        {
            var change = await services.GetRequiredService<BotService>().Create("Helper", "Answer briefly.", ConnectionId, null, UserId, Token);
            change.Problem.Should().BeNull();
            return change.Bot!.Id;
        });

        // A new bot gets only the tools on the default list. The read-only test tools are granted here, as a person would.
        foreach (var tool in Tools.All.Where(tool => tool.Sensitivity is ToolSensitivity.Read))
        {
            await Grant(tool.Name, GrantEffect.Allow);
        }
    }

    private static string Summary(MessagePart part) => part switch
    {
        TextPart text => text.Text,
        ToolCallPart call => $"call {call.CallId} {call.ToolName}",
        ToolResultPart result => $"result {result.CallId} {JsonSerializer.Serialize(result.Result)}",
        _ => part.GetType().Name,
    };

    #endregion
}
