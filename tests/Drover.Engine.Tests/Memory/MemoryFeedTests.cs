namespace Drover.Engine.Tests.Memory;

/// <summary>
/// External memory service integration over real PostgreSQL: private finished runs send only the person's message and
/// the bot's final reply with no tool text, tainted runs send only the person's words, shared conversations and bots
/// with memory off or AllowMemoryService off send and recall nothing, timeouts and failures emit one
/// memory_service_unavailable event and let runs finish normally, no tenant transaction is open during any provider
/// call, deleting a conversation or removing a person forgets at the provider with hourly retry on failure, and no
/// message text, fact, or key appears in any log line.
/// </summary>
public sealed class MemoryFeedTests(PostgresServer postgres)
{
    #region Remembering

    [Fact]
    public async Task A_finished_private_run_sends_the_persons_message_and_the_final_reply_only()
    {
        var memory = new FakeMemoryProvider();
        List<bool> transactionsOpen = [];
        memory.BeforeCall = _ => transactionsOpen.Add(TenantScope.IsOpenInCurrentFlow);
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "lookup", """{"query":"secret tool args"}""")],
            [new TextOutput("You should drink oolong tea.")]);
        var tools = new TestTools().Add("lookup", ToolSensitivity.Read, """{"result":"raw tool output never sent"}""");
        await using var world = await RunWorld.Create(
            postgres,
            script,
            tools,
            everyHost: services => UseMemoryProvider(services, memory));
        await TurnMemoryOn(world);
        await world.GrantTo(world.BotId, "lookup", GrantEffect.Allow);
        var host = world.StartHost();
        var thread = await world.NewThread();
        var upload = await world.Upload(thread, "notes.txt", Encoding.UTF8.GetBytes("attached file text never sent"));
        upload.Problem.Should().Be(FileProblem.None);

        var post = await world.PostRaw(thread, "What tea should I drink?", through: host, fileIds: [upload.File!.Id]);
        post.Problem.Should().Be(PostProblem.None, post.Detail);
        var run = await world.WaitForStatus(post.Run!.Id, RunStatus.Completed, RunStatus.Failed);
        await WaitFor(() => memory.Remembers.Count > 0);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        var recorded = memory.Remembers.Should().ContainSingle().Subject;
        recorded.Scope.Should().Be(new MemoryScope(world.SpaceId, world.BotId, world.UserId, thread));
        recorded.Turns.Should().HaveCount(2);
        recorded.Turns[0].Speaker.Should().Be(MemorySpeaker.Person);
        recorded.Turns[0].Text.Should().Be("What tea should I drink?");
        recorded.Turns[1].Speaker.Should().Be(MemorySpeaker.Bot);
        recorded.Turns[1].Text.Should().Be("You should drink oolong tea.");
        var allSentText = string.Join("\n", recorded.Turns.Select(turn => turn.Text));
        allSentText.Should()
            .NotContain("secret tool args")
            .And.NotContain("raw tool output never sent")
            .And.NotContain("attached file text never sent");
        transactionsOpen.Should().NotBeEmpty().And.OnlyContain(open => !open, "no tenant transaction is open during any provider call");
        AssertNothingLeakedInLogs(host.Log, "What tea should I drink?", "oolong tea", memory.ApiKey);
    }

    [Fact]
    public async Task A_tainted_run_sends_the_persons_words_and_not_the_reply()
    {
        var memory = new FakeMemoryProvider();
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "untrusted__fetch", "{}")],
            [new TextOutput("The page told me to say this untrusted reply.")]);
        await using var world = await RunWorld.Create(
            postgres,
            script,
            everyHost: services => UseMemoryProvider(services, memory));
        await TurnMemoryOn(world);
        world.Server.Offer("fetch", "Fetch an untrusted page.", readOnly: true, """{"body":"untrusted page text"}""");
        var connection = await world.Connect("Untrusted", TrustLevel.Untrusted);
        var toolName = await world.Read(db => db.Tools.AsNoTracking()
            .Where(tool => tool.ToolConnectionId == connection.Id)
            .Select(tool => tool.Name)
            .SingleAsync(RunWorld.Token));
        await world.Grant(toolName, GrantEffect.Allow);
        world.StartHost();
        var thread = await world.NewThread();

        var runId = await world.Post(thread, "Check that site for me.");
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);
        await WaitFor(() => memory.Remembers.Count > 0);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        run.Tainted.Should().BeTrue();
        var recorded = memory.Remembers.Should().ContainSingle().Subject;
        recorded.Turns.Should().ContainSingle();
        recorded.Turns[0].Speaker.Should().Be(MemorySpeaker.Person);
        recorded.Turns[0].Text.Should().Be("Check that site for me.");
    }

    [Fact]
    public async Task Nothing_is_sent_from_a_conversation_with_more_than_one_person()
    {
        var memory = new FakeMemoryProvider();
        await using var world = await RunWorld.Create(
            postgres,
            new ScriptedModel([new TextOutput("Hello everyone.")]),
            everyHost: services => UseMemoryProvider(services, memory));
        await TurnMemoryOn(world);
        await world.AddMember(MembershipRole.Member);
        var host = world.StartHost();
        var shared = await world.NewSharedThread();

        var runId = await world.Post(shared, "Hello team.", through: host);
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);
        await Task.Delay(150, RunWorld.Token);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        memory.Recalls.Should().BeEmpty();
        memory.Remembers.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failing_provider_never_changes_the_runs_outcome()
    {
        var memory = new FakeMemoryProvider
        {
            FailAllWith = MemoryFailure.Unreachable,
        };
        await using var world = await RunWorld.Create(
            postgres,
            new ScriptedModel([new TextOutput("Still answered.")]),
            everyHost: services => UseMemoryProvider(services, memory));
        await TurnMemoryOn(world);
        var host = world.StartHost();
        var thread = await world.NewThread();

        var runId = await world.Post(thread, "Sensitive person message 99a1.", through: host);
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);
        await Task.Delay(150, RunWorld.Token);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        var events = await world.ReadEvents(runId);
        var unavailable = events.Where(stored => stored.Type == RunEventTypes.MemoryServiceUnavailable).ToList();
        unavailable.Should().ContainSingle();
        RunWorld.Data<MemoryServiceUnavailable>(unavailable[0]).Should().Be(new MemoryServiceUnavailable(memory.Kind, "unreachable"));
        AssertNothingLeakedInLogs(host.Log, "Sensitive person message 99a1.", "Still answered.", memory.ApiKey);
    }

    #endregion

    #region Recall, Timeout, And Policy

    [Fact]
    public async Task A_hanging_provider_times_out_and_emits_one_memory_service_unavailable_event()
    {
        var memory = new FakeMemoryProvider
        {
            OnRecall = async (_, _, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                return new MemoryRecall(["Never arrives."], null);
            },
        };
        await using var world = await RunWorld.Create(
            postgres,
            new ScriptedModel([new TextOutput("Answered on time.")]),
            everyHost: services => UseMemoryProvider(services, memory, recallTimeoutSeconds: 1));
        await TurnMemoryOn(world);
        var host = world.StartHost();
        var thread = await world.NewThread();

        var runId = await world.Post(thread, "Hello while service hangs.", through: host);
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        var unavailable = (await world.ReadEvents(runId))
            .Where(stored => stored.Type == RunEventTypes.MemoryServiceUnavailable)
            .ToList();
        unavailable.Should().ContainSingle();
        RunWorld.Data<MemoryServiceUnavailable>(unavailable[0]).Reason.Should().Be("timeout");
        AssertNothingLeakedInLogs(host.Log, "Hello while service hangs.", "Never arrives.", memory.ApiKey);
    }

    [Fact]
    public async Task Nothing_is_sent_or_recalled_when_bot_memory_is_off_or_AllowMemoryService_is_false()
    {
        var offBotMemory = new FakeMemoryProvider();
        await using (var world = await RunWorld.Create(
            postgres,
            new ScriptedModel([new TextOutput("Bot memory off.")]),
            everyHost: services => UseMemoryProvider(services, offBotMemory)))
        {
            var host = world.StartHost();
            var runId = await world.Post(await world.NewThread(), "Do not remember this.", through: host);
            (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
            await Task.Delay(150, RunWorld.Token);

            offBotMemory.Recalls.Should().BeEmpty();
            offBotMemory.Remembers.Should().BeEmpty();
        }

        var disallowedMemory = new FakeMemoryProvider();
        var policy = DeploymentPolicy.SelfHosted with { AllowMemoryService = false };
        await using (var world = await RunWorld.Create(
            postgres,
            new ScriptedModel([new TextOutput("Policy off.")]),
            everyHost: services =>
            {
                UseMemoryProvider(services, disallowedMemory);
                services.AddSingleton<IOptions<DeploymentPolicyOptions>>(Options.Create(new DeploymentPolicyOptions { Rules = policy }));
            }))
        {
            await TurnMemoryOn(world);
            var host = world.StartHost();
            var runId = await world.Post(await world.NewThread(), "Policy forbids memory service.", through: host);
            (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
            await Task.Delay(150, RunWorld.Token);

            disallowedMemory.Recalls.Should().BeEmpty();
            disallowedMemory.Remembers.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Recalled_facts_reach_every_model_call_and_never_instructions_or_logs()
    {
        var memory = new FakeMemoryProvider();
        var script = new ScriptedModel(
            [new TextOutput("First answer.")],
            [new ToolCallOutput("c1", "lookup", "{}")],
            [new TextOutput("Second answer.")]);
        var tools = new TestTools().Add("lookup", ToolSensitivity.Read, """{"ok":true}""");
        await using var world = await RunWorld.Create(
            postgres,
            script,
            tools,
            everyHost: services => UseMemoryProvider(services, memory));
        await TurnMemoryOn(world);
        await world.GrantTo(world.BotId, "lookup", GrantEffect.Allow);
        var host = world.StartHost();

        var firstThread = await world.NewThread();
        var firstRun = await world.Post(firstThread, "My favorite color is cerulean.", through: host);
        (await world.WaitForStatus(firstRun, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        await WaitFor(() => memory.Remembers.Count > 0);

        var secondThread = await world.NewThread();
        var secondRun = await world.Post(secondThread, "What color should I pick?", through: host);
        (await world.WaitForStatus(secondRun, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        world.Model.Requests.Should().HaveCount(3);
        foreach (var request in world.Model.Requests.Skip(1))
        {
            var recalled = request.Messages
                .Where(message => Text(message).Contains("[What your memory service knows about this person]", StringComparison.Ordinal))
                .ToList();
            recalled.Should().ContainSingle().Which.Role.Should().Be("user");
            Text(recalled[0]).Should().Contain("My favorite color is cerulean.");
            request.Options.Instructions.Should().NotContain("cerulean");
        }

        AssertNothingLeakedInLogs(host.Log, "cerulean", "What color should I pick?", memory.ApiKey);
    }

    #endregion

    #region Forgetting And Hourly Retry

    [Fact]
    public async Task Deleting_a_conversation_and_removing_a_person_reach_the_provider_and_refused_forgets_retry_on_the_hourly_pass()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero));
        var refuse = true;
        List<bool> transactionsOpen = [];
        var memory = new FakeMemoryProvider
        {
            BeforeCall = _ => transactionsOpen.Add(TenantScope.IsOpenInCurrentFlow),
            FailForgetConversation = _ => refuse ? MemoryProviderException.FromStatus(503) : null,
            FailForgetPerson = _ => refuse ? MemoryProviderException.FromStatus(503) : null,
        };
        await using var world = await RunWorld.Create(
            postgres,
            new ScriptedModel([new TextOutput("Hello.")]),
            everyHost: services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                UseMemoryProvider(services, memory);
            });
        await TurnMemoryOn(world);
        var host = world.IdleHost();
        var member = await world.AddMember(MembershipRole.Member);
        var threadToDelete = await world.NewThread();
        var threadToDeleteImmediately = await world.NewThread();

        // First: provider refuses both forgets. Both rows stay pending for the hourly retry pass.
        var deletedRefused = await DeleteThread(host, world.SpaceId, world.UserId, threadToDelete);
        var removedRefused = await RemoveMember(host, world.SpaceId, world.UserId, member);

        deletedRefused.Should().BeTrue();
        removedRefused.Should().BeTrue();
        memory.ForgottenConversations.Should().ContainSingle(scope => scope.ThreadId == threadToDelete);
        memory.ForgottenPeople.Should().ContainSingle(scope => scope.PersonId == member && scope.SpaceId == world.SpaceId);
        (await world.Read(db => db.MemoryForgetRequests.AsNoTracking().CountAsync(RunWorld.Token))).Should().Be(2);

        // Next hourly pass while provider now accepts: both are retried and cleared.
        refuse = false;
        clock.Advance(MemoryForgetRetry.Interval);
        var retried = await host.Services.GetRequiredService<MemoryForgetRetry>().RetryDue(RunWorld.Token);

        retried.Should().Be(2);
        (await world.Read(db => db.MemoryForgetRequests.AsNoTracking().CountAsync(RunWorld.Token))).Should().Be(0);

        // Direct delete when provider accepts immediately: no pending row remains.
        var deletedAccepted = await DeleteThread(host, world.SpaceId, world.UserId, threadToDeleteImmediately);
        deletedAccepted.Should().BeTrue();
        (await world.Read(db => db.MemoryForgetRequests.AsNoTracking().CountAsync(RunWorld.Token))).Should().Be(0);
        transactionsOpen.Should().NotBeEmpty().And.OnlyContain(open => !open, "no tenant transaction is open during any forget call");
    }

    #endregion

    #region Private Methods

    private static void UseMemoryProvider(
        IServiceCollection services,
        FakeMemoryProvider provider,
        int recallMaxCharacters = MemoryOptions.DefaultRecallMaxCharacters,
        int recallTimeoutSeconds = MemoryOptions.DefaultRecallTimeoutSeconds)
    {
        services.AddSingleton<IMemoryProvider>(provider);
        services.Configure<MemoryOptions>(options =>
        {
            options.Provider = provider.Kind;
            options.RecallMaxCharacters = recallMaxCharacters;
            options.RecallTimeoutSeconds = recallTimeoutSeconds;
        });
    }

    private static Task<bool> TurnMemoryOn(RunWorld world) =>
        world.Read(async db =>
        {
            var bot = await db.Bots.SingleAsync(found => found.Id == world.BotId, RunWorld.Token);
            bot.MemorySettings = bot.MemorySettings with { Enabled = true };
            return true;
        });

    private static async Task<bool> DeleteThread(EngineHost host, Guid spaceId, Guid userId, Guid threadId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        return await scope.ServiceProvider.GetRequiredService<ThreadService>().Delete(threadId, userId, RunWorld.Token);
    }

    private static async Task<bool> RemoveMember(EngineHost host, Guid spaceId, Guid actorUserId, Guid memberUserId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AccountService>().RemoveMember(spaceId, actorUserId, memberUserId, RunWorld.Token);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();

        while (!condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException("Timed out waiting for the memory provider call.");
            }

            await Task.Delay(50, RunWorld.Token);
        }
    }

    private static string Text(ModelChatMessage message) =>
        string.Join("\n", message.Contents.OfType<ModelText>().Select(part => part.Text));

    private static void AssertNothingLeakedInLogs(HostLog log, params string[] forbidden)
    {
        var fullLog = log.Tail();

        foreach (var secret in forbidden)
        {
            fullLog.Should().NotContain(secret, "no message text, recalled fact, or key may appear in any log line");
        }
    }

    #endregion
}
