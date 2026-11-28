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
    public async Task A_root_reply_based_on_a_tainted_child_is_not_remembered()
    {
        var memory = new FakeMemoryProvider();
        await using var world = await RunWorld.Create(
            postgres,
            new ScriptedModel(),
            everyHost: services => UseMemoryProvider(services, memory));
        await TurnMemoryOn(world);
        var childBotId = await world.NewBotBeneath(world.BotId, "Researcher", "Fetches untrusted pages.");
        world.Server.Offer("fetch", "Fetch an untrusted page.", readOnly: true, """{"body":"untrusted delegated text"}""");
        var connection = await world.Connect("Untrusted", TrustLevel.Untrusted);
        var toolName = await world.Read(db => db.Tools.AsNoTracking()
            .Where(tool => tool.ToolConnectionId == connection.Id)
            .Select(tool => tool.Name)
            .SingleAsync(RunWorld.Token));
        await world.Grant(DelegateTool.Name, GrantEffect.Allow);
        await world.Grant(toolName, GrantEffect.Allow);
        await world.GrantTo(childBotId, toolName, GrantEffect.Allow);
        world.Script.Then(
            [new ToolCallOutput("d1", DelegateTool.Name, JsonSerializer.Serialize(new { bot_id = childBotId, task = "Fetch that page." }))],
            [new ToolCallOutput("c1", "untrusted__fetch", "{}")],
            [new TextOutput("Child read untrusted page.")],
            [new TextOutput("Parent reply repeating untrusted child content.")]);
        var host = world.StartHost();
        var thread = await world.NewThread();

        var rootRunId = await world.Post(thread, "Have Researcher check that site.", through: host);
        var rootRun = await world.WaitForStatus(rootRunId, RunStatus.Completed, RunStatus.Failed);
        await WaitFor(() => memory.Remembers.Count > 0);

        rootRun.Status.Should().Be(RunStatus.Completed, rootRun.Error?.Message);
        var childRun = (await world.ReadTree(rootRunId)).Should().ContainSingle(run => run.Id != rootRunId).Subject;
        childRun.Tainted.Should().BeTrue();
        var recorded = memory.Remembers.Should().ContainSingle().Subject;
        recorded.Turns.Should().ContainSingle();
        recorded.Turns[0].Speaker.Should().Be(MemorySpeaker.Person);
        recorded.Turns[0].Text.Should().Be("Have Researcher check that site.");
    }

    [Fact]
    public async Task Scheduled_prompts_are_not_sent_as_person_authored_memory()
    {
        var memory = new FakeMemoryProvider();
        await using var world = await RunWorld.Create(
            postgres,
            new ScriptedModel([new TextOutput("Morning briefing complete.")]),
            everyHost: services => UseMemoryProvider(services, memory));
        await TurnMemoryOn(world);
        var host = world.StartHost();
        BotSchedule schedule;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, world.UserId);
            await using var tx = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(RunWorld.Token);
            var created = await scope.ServiceProvider.GetRequiredService<ScheduleService>().Create(
                world.BotId,
                new ScheduleEdit(
                    "0 9 * * *",
                    "UTC",
                    "Automated morning briefing prompt never authored by person.",
                    ScheduleThreadMode.NewThread,
                    true),
                world.UserId,
                RunWorld.Token);
            created.Problem.Should().BeNull();
            schedule = created.Schedule!;
            await tx.Commit(RunWorld.Token);
        }

        ScheduleTrial trial;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, world.UserId);
            trial = await scope.ServiceProvider.GetRequiredService<ScheduleFiring>()
                .RunOnce(world.BotId, schedule.Id, world.UserId, RunWorld.Token);
        }

        trial.Problem.Should().BeNull();
        var run = await world.WaitForStatus(trial.RunId!.Value, RunStatus.Completed, RunStatus.Failed);
        await WaitFor(() => memory.Remembers.Count > 0);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        memory.Recalls.Should().ContainSingle().Which.Request.LatestMessage.Should().BeNull();
        var recorded = memory.Remembers.Should().ContainSingle().Subject;
        recorded.Turns.Should().NotContain(turn => turn.Speaker == MemorySpeaker.Person);
        recorded.Turns.Should().NotContain(turn => turn.Text.Contains("Automated morning briefing prompt", StringComparison.Ordinal));
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
    public async Task A_provider_that_ignores_cancellation_in_Recall_still_times_out()
    {
        var memory = new FakeMemoryProvider
        {
            OnRecall = (_, _, _) => new TaskCompletionSource<MemoryRecall?>().Task,
        };
        await using var world = await RunWorld.Create(
            postgres,
            new ScriptedModel([new TextOutput("Answered despite ignored token.")]),
            everyHost: services => UseMemoryProvider(services, memory, recallTimeoutSeconds: 1));
        await TurnMemoryOn(world);
        var host = world.StartHost();
        var thread = await world.NewThread();

        var runId = await world.Post(thread, "Hello while provider ignores cancellation.", through: host);
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        var unavailable = (await world.ReadEvents(runId))
            .Where(stored => stored.Type == RunEventTypes.MemoryServiceUnavailable)
            .ToList();
        unavailable.Should().ContainSingle();
        RunWorld.Data<MemoryServiceUnavailable>(unavailable[0]).Reason.Should().Be("timeout");
    }

    [Fact]
    public async Task A_hanging_provider_in_Remember_times_out_and_still_frees_the_worker()
    {
        var hangFirst = true;
        var memory = new FakeMemoryProvider
        {
            OnRemember = (_, _, _) => hangFirst
                ? new TaskCompletionSource().Task
                : Task.CompletedTask,
        };
        await using var world = await RunWorld.Create(
            postgres,
            new ScriptedModel(
                [new TextOutput("First reply.")],
                [new TextOutput("Second reply.")]),
            everyHost: services => UseMemoryProvider(services, memory, rememberTimeoutSeconds: 1));
        await TurnMemoryOn(world);
        var host = world.StartHost();
        var thread = await world.NewThread();

        var firstRunId = await world.Post(thread, "First message while remember hangs.", through: host);
        var firstRun = await world.WaitForStatus(firstRunId, RunStatus.Completed, RunStatus.Failed);
        firstRun.Status.Should().Be(RunStatus.Completed, firstRun.Error?.Message);
        await WaitFor(() => memory.Remembers.Count == 1);

        // Once the bounded remember times out, the worker is free to finish the next run promptly.
        hangFirst = false;
        var secondRunId = await world.Post(thread, "Second message after timeout.", through: host);
        var secondRun = await world.WaitForStatus(secondRunId, RunStatus.Completed, RunStatus.Failed);
        await WaitFor(() => memory.Remembers.Count == 2);

        secondRun.Status.Should().Be(RunStatus.Completed, secondRun.Error?.Message);
        AssertNothingLeakedInLogs(host.Log, "First message while remember hangs.", "First reply.", memory.ApiKey);
    }

    [Fact]
    public async Task Regenerating_a_reply_does_not_send_the_persons_message_again()
    {
        var memory = new FakeMemoryProvider();
        await using var world = await RunWorld.Create(
            postgres,
            new ScriptedModel(
                [new TextOutput("Initial reply.")],
                [new TextOutput("Regenerated reply.")]),
            everyHost: services => UseMemoryProvider(services, memory));
        await TurnMemoryOn(world);
        var host = world.StartHost();
        var thread = await world.NewThread();

        var firstRunId = await world.Post(thread, "Tell me about oolong.", through: host);
        (await world.WaitForStatus(firstRunId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        await WaitFor(() => memory.Remembers.Count == 1);

        var replyId = await world.Read(db => db.Messages.AsNoTracking()
            .Where(message => message.ThreadId == thread && message.Role == MessageRole.Assistant && message.ShelvedAt == null)
            .Select(message => message.Id)
            .SingleAsync(RunWorld.Token));

        VersionChange regenerated;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, world.UserId);
            regenerated = await scope.ServiceProvider.GetRequiredService<RunService>()
                .Regenerate(thread, replyId, world.UserId, RunWorld.Token);
        }

        regenerated.Outcome.Should().Be(VersionOutcome.Queued);
        (await world.WaitForStatus(regenerated.Run!.Id, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        await WaitFor(() => memory.Remembers.Count == 2);

        memory.Remembers[1].Turns.Should().ContainSingle();
        memory.Remembers[1].Turns[0].Speaker.Should().Be(MemorySpeaker.Bot);
        memory.Remembers[1].Turns[0].Text.Should().Be("Regenerated reply.");
    }

    [Fact]
    public void MemoryOptionsValidator_fails_for_unmatched_provider_when_allowed_and_ignores_when_disallowed()
    {
        var allowedValidator = new MemoryOptionsValidator(
            [new FakeMemoryProvider()],
            Options.Create(new DeploymentPolicyOptions { Rules = DeploymentPolicy.SelfHosted }),
            NullLogger<MemoryOptionsValidator>.Instance);

        allowedValidator.Validate(null, new MemoryOptions { Provider = "unknown_provider" }).Failed.Should().BeTrue();
        allowedValidator.Validate(null, new MemoryOptions { Provider = FakeMemoryProvider.DefaultKind }).Succeeded.Should().BeTrue();
        allowedValidator.Validate(null, new MemoryOptions { RememberTimeoutSeconds = 0 }).Failed.Should().BeTrue();

        var disallowedValidator = new MemoryOptionsValidator(
            [],
            Options.Create(new DeploymentPolicyOptions { Rules = DeploymentPolicy.Hosted }),
            NullLogger<MemoryOptionsValidator>.Instance);

        disallowedValidator.Validate(null, new MemoryOptions { Provider = "unknown_provider" }).Succeeded.Should().BeTrue();
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

    [Fact]
    public async Task A_forget_recorded_while_the_provider_is_unavailable_is_sent_once_it_is_back()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero));
        await using var world = await RunWorld.Create(
            postgres,
            new ScriptedModel([new TextOutput("Hello.")]),
            everyHost: services => services.AddSingleton<TimeProvider>(clock));
        var member = await world.AddMember(MembershipRole.Member);
        var threadToDelete = await world.NewThread();

        // No provider is active on this host, yet AllowMemoryService is true so forgets are still recorded durably.
        var offlineHost = world.IdleHost();
        (await DeleteThread(offlineHost, world.SpaceId, world.UserId, threadToDelete)).Should().BeTrue();
        (await RemoveMember(offlineHost, world.SpaceId, world.UserId, member)).Should().BeTrue();
        (await world.Read(db => db.MemoryForgetRequests.AsNoTracking().CountAsync(RunWorld.Token))).Should().Be(2);

        // Once a host with the provider configured comes back, the retry pass sends both forgets and drains the table.
        var memory = new FakeMemoryProvider();
        var onlineHost = world.IdleHost(services => UseMemoryProvider(services, memory));
        var retried = await onlineHost.Services.GetRequiredService<MemoryForgetRetry>().RetryDue(RunWorld.Token);

        retried.Should().Be(2);
        memory.ForgottenConversations.Should().ContainSingle(scope => scope.ThreadId == threadToDelete);
        memory.ForgottenPeople.Should().ContainSingle(scope => scope.PersonId == member && scope.SpaceId == world.SpaceId);
        (await world.Read(db => db.MemoryForgetRequests.AsNoTracking().CountAsync(RunWorld.Token))).Should().Be(0);
    }

    [Fact]
    public async Task Deleting_a_conversation_removes_runs_delegation_child_threads_and_note_source_links()
    {
        var memory = new FakeMemoryProvider();
        await using var world = await RunWorld.Create(
            postgres,
            new ScriptedModel(),
            everyHost: services => UseMemoryProvider(services, memory));
        await TurnMemoryOn(world);
        var childBotId = await world.NewBotBeneath(world.BotId, "Helper", "Helps look things up.");
        await world.Grant(DelegateTool.Name, GrantEffect.Allow);
        await world.Grant("memory.save", GrantEffect.Allow);
        world.Script.Then(
            [new ToolCallOutput("d1", DelegateTool.Name, JsonSerializer.Serialize(new { bot_id = childBotId, task = "Check tea." }))],
            [new TextOutput("Child checked tea.")],
            [new ToolCallOutput("m1", "memory_save", """{"text":"Likes oolong tea."}""")],
            [new TextOutput("Saved your tea preference.")]);
        var host = world.StartHost();
        var threadId = await world.NewThread();

        var rootRunId = await world.Post(threadId, "Remember my tea preference.", through: host);
        var rootRun = await world.WaitForStatus(rootRunId, RunStatus.Completed, RunStatus.Failed);
        rootRun.Status.Should().Be(RunStatus.Completed, rootRun.Error?.Message);
        await WaitFor(() => memory.Remembers.Count > 0);

        var tree = await world.ReadTree(rootRunId);
        var childRun = tree.Should().ContainSingle(run => run.Id != rootRunId).Subject;
        Guid[] allThreadIds = [threadId, childRun.ThreadId];
        Guid[] allRunIds = [.. tree.Select(run => run.Id)];

        // Shared conversations cannot be deleted through this private-only hook.
        await world.AddMember(MembershipRole.Member);
        var sharedThread = await world.NewSharedThread();
        (await DeleteThread(host, world.SpaceId, world.UserId, sharedThread)).Should().BeFalse();

        // Deleting the private conversation removes all runs, delegation child threads, messages, events, and clears note sources.
        (await DeleteThread(host, world.SpaceId, world.UserId, threadId)).Should().BeTrue();

        (await world.Read(db => db.Threads.AsNoTracking().CountAsync(thread => allThreadIds.Contains(thread.Id), RunWorld.Token))).Should().Be(0);
        (await world.Read(db => db.Messages.AsNoTracking().CountAsync(message => allThreadIds.Contains(message.ThreadId), RunWorld.Token))).Should().Be(0);
        (await world.Read(db => db.Runs.AsNoTracking().CountAsync(run => allRunIds.Contains(run.Id), RunWorld.Token))).Should().Be(0);
        (await world.Read(db => db.RunEvents.AsNoTracking().CountAsync(stored => allRunIds.Contains(stored.RootRunId), RunWorld.Token))).Should().Be(0);
        (await world.Read(db => db.ToolCalls.AsNoTracking().CountAsync(call => allThreadIds.Contains(call.ThreadId) || allRunIds.Contains(call.RunId), RunWorld.Token))).Should().Be(0);
        (await world.Read(db => db.UsageRecords.AsNoTracking().CountAsync(record => allRunIds.Contains(record.RunId), RunWorld.Token))).Should().Be(0);
        (await world.Read(db => db.AgentSessions.AsNoTracking().CountAsync(session => allThreadIds.Contains(session.ThreadId), RunWorld.Token))).Should().Be(0);

        var notes = await world.Read(db => db.Memories.AsNoTracking().ToListAsync(RunWorld.Token));
        var savedNote = notes.Should().ContainSingle().Subject;
        savedNote.Text.Should().Be("Likes oolong tea.");
        savedNote.SourceThreadId.Should().BeNull();
        savedNote.SourceMessageId.Should().BeNull();
    }

    [Fact]
    public async Task Two_owners_removing_each_other_at_once_leave_one_owner()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("Hello.")]));
        var secondOwner = await world.AddMember(MembershipRole.Owner);
        var host = world.IdleHost();

        var firstRemovesSecond = Task.Run(() => RemoveMember(host, world.SpaceId, world.UserId, secondOwner), RunWorld.Token);
        var secondRemovesFirst = Task.Run(() => RemoveMember(host, world.SpaceId, secondOwner, world.UserId), RunWorld.Token);
        var results = await Task.WhenAll(firstRemovesSecond, secondRemovesFirst);

        results.Count(removed => removed).Should().Be(1, "exactly one concurrent owner removal must succeed");
        var remainingOwners = await world.Read(db => db.Memberships.AsNoTracking()
            .CountAsync(membership => membership.SpaceId == world.SpaceId && membership.Role == MembershipRole.Owner, RunWorld.Token));
        remainingOwners.Should().Be(1, "concurrent removals must never leave a space without an owner");
    }

    [Fact]
    public async Task A_late_remember_cannot_restore_forgotten_conversation_or_person_data()
    {
        var conversationRememberStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseConversationRemember = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var personRememberStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePersonRemember = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Guid? conversationThreadId = null;
        Guid? memberUserId = null;
        var memory = new FakeMemoryProvider
        {
            OnRemember = async (scope, _, _) =>
            {
                if (scope.ThreadId == conversationThreadId)
                {
                    conversationRememberStarted.TrySetResult();
                    await releaseConversationRemember.Task;
                }
                else if (scope.PersonId == memberUserId)
                {
                    personRememberStarted.TrySetResult();
                    await releasePersonRemember.Task;
                }
            },
        };
        await using var world = await RunWorld.Create(
            postgres,
            new ScriptedModel(
                [new TextOutput("Remembered conversation answer.")],
                [new TextOutput("Remembered member answer.")]),
            everyHost: services => UseMemoryProvider(services, memory, rememberTimeoutSeconds: 1));
        await TurnMemoryOn(world);
        var host = world.StartHost();

        // Part 1: conversation deleted while an earlier Remember write hangs past WaitAsync timeout.
        var threadId = await world.NewThread();
        conversationThreadId = threadId;
        var conversationRunId = await world.Post(threadId, "Deleted conversation secret 77b1.", through: host);
        (await world.WaitForStatus(conversationRunId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        await conversationRememberStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), RunWorld.Token);
        await Task.Delay(1200, RunWorld.Token); // Let Remember's 1s WaitAsync time out while write stays in flight.

        (await DeleteThread(host, world.SpaceId, world.UserId, threadId)).Should().BeTrue();
        (await world.Read(db => db.MemoryForgetRequests.AsNoTracking().CountAsync(RunWorld.Token))).Should().Be(0);

        // Now the late Remember finishes and writes into the provider; post-remember reconciliation must re-forget it.
        releaseConversationRemember.TrySetResult();
        await WaitFor(() => memory.ForgottenConversations.Count(scope => scope.ThreadId == threadId) >= 2);
        var conversationScope = new MemoryScope(world.SpaceId, world.BotId, world.UserId, threadId);
        var recalledAfterDelete = await memory.Recall(conversationScope, new MemoryRecallRequest("Anything?", 4000), RunWorld.Token);
        string.Join("\n", recalledAfterDelete?.Facts ?? []).Should().NotContain("77b1");
        (await world.Read(db => db.MemoryForgetRequests.AsNoTracking().CountAsync(RunWorld.Token))).Should().Be(0);

        // Part 2: member removed while their earlier Remember write hangs past WaitAsync timeout.
        var member = await world.AddMember(MembershipRole.Member);
        memberUserId = member;
        Guid memberThreadId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, member);
            await using var tx = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(RunWorld.Token);
            memberThreadId = (await scope.ServiceProvider.GetRequiredService<ThreadService>().Create(world.BotId, null, member, RunWorld.Token))!.Id;
            await tx.Commit(RunWorld.Token);
        }

        var memberPost = await world.PostRaw(memberThreadId, "Removed member secret 88c2.", through: host, asUser: member);
        memberPost.Problem.Should().Be(PostProblem.None, memberPost.Detail);
        var memberRun = await world.WaitForStatus(memberPost.Run!.Id, RunStatus.Completed, RunStatus.Failed);
        memberRun.Status.Should().Be(RunStatus.Completed, memberRun.Error?.Message);
        await personRememberStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), RunWorld.Token);
        await Task.Delay(1200, RunWorld.Token); // Let Remember's 1s WaitAsync time out while write stays in flight.

        (await RemoveMember(host, world.SpaceId, world.UserId, member)).Should().BeTrue();
        (await world.Read(db => db.MemoryForgetRequests.AsNoTracking().CountAsync(RunWorld.Token))).Should().Be(0);

        // Now the late Remember finishes; post-remember reconciliation must re-forget the removed person.
        releasePersonRemember.TrySetResult();
        await WaitFor(() => memory.ForgottenPeople.Count(scope => scope.PersonId == member && scope.SpaceId == world.SpaceId) >= 2);
        var memberScope = new MemoryScope(world.SpaceId, world.BotId, member, memberThreadId);
        var recalledAfterRemoval = await memory.Recall(memberScope, new MemoryRecallRequest("Anything?", 4000), RunWorld.Token);
        string.Join("\n", recalledAfterRemoval?.Facts ?? []).Should().NotContain("88c2");
        (await world.Read(db => db.MemoryForgetRequests.AsNoTracking().CountAsync(RunWorld.Token))).Should().Be(0);

        // Also: a removed member's completed run reaching MemoryFeed.Remember directly must send nothing.
        var countBefore = memory.Remembers.Count;
        var feed = host.Services.GetRequiredService<MemoryFeed>();
        var scopes = host.Services.GetRequiredService<RunScopes>();
        await feed.Remember(scopes.For(world.SpaceId, member), memberRun, RunWorld.Token);
        memory.Remembers.Count.Should().Be(countBefore);
    }

    #endregion

    #region Private Methods

    private static void UseMemoryProvider(
        IServiceCollection services,
        FakeMemoryProvider provider,
        int recallMaxCharacters = MemoryOptions.DefaultRecallMaxCharacters,
        int recallTimeoutSeconds = MemoryOptions.DefaultRecallTimeoutSeconds,
        int rememberTimeoutSeconds = MemoryOptions.DefaultRememberTimeoutSeconds)
    {
        services.AddSingleton<IMemoryProvider>(provider);
        services.Configure<MemoryOptions>(options =>
        {
            options.Provider = provider.Kind;
            options.RecallMaxCharacters = recallMaxCharacters;
            options.RecallTimeoutSeconds = recallTimeoutSeconds;
            options.RememberTimeoutSeconds = rememberTimeoutSeconds;
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
