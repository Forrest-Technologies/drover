namespace Drover.Engine.Tests.Runs;

/// <summary>
/// The run engine end to end: the real dispatcher, executor, and agent runtime over PostgreSQL, against the scripted
/// model. Each test is one acceptance criterion of the run engine.
/// </summary>
public sealed class RunEngineTests(PostgresServer postgres)
{
    #region A Complete Run

    [Fact]
    public async Task A_run_completes_and_its_messages_tool_calls_usage_and_events_are_stored()
    {
        var tools = new TestTools().Add("test.lookup", ToolSensitivity.Read, """{"answer":42}""");
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "test_lookup")],
            [new TextOutput("It is "), new TextOutput("42.")]);
        await using var world = await RunWorld.Create(postgres, script, tools);
        world.StartHost();
        var threadId = await world.NewThread();

        var runId = await world.Post(threadId, "What is the answer?");
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        run.FinishedAt.Should().NotBeNull();
        run.LeaseOwner.Should().BeNull();
        RunWorld.Summaries(await world.ReadMessages(threadId)).Should().Equal(
            "user: What is the answer?",
            "assistant: call c1 test_lookup",
            """tool: result c1 {"answer":42}""",
            "assistant: It is 42.");
        (await world.ReadToolCalls(runId)).Should().ContainSingle(call => call.Status == ToolCallStatus.Completed && call.ToolName == "test_lookup");
        tools.Count("test.lookup").Should().Be(1);
        script.Requests[1].Should().Contain(new SeenMessage("tool", """c1 = {"answer":42}"""));

        var usage = await world.ReadUsage(runId);
        usage.Should().HaveCount(2).And.OnlyContain(record => record.InputTokens == 10 && record.OutputTokens == 5 && record.Cost == 40m);
        run.Usage.Should().Be(new RunUsage { InputTokens = 20, OutputTokens = 10, Cost = 80m });
        run.SubtreeUsage.Should().Be(run.Usage);

        var events = await world.ReadEvents(runId);
        events.Select(stored => stored.Seq).Should().Equal(Enumerable.Range(1, events.Count).Select(number => (long)number));
        run.LastEventSeq.Should().Be(events.Count);
        events.Where(stored => stored.Type != RunEventTypes.MessageDelta).Select(stored => stored.Type).Should().Equal(
            RunEventTypes.RunQueued,
            RunEventTypes.RunStarted,
            RunEventTypes.StepStarted,
            RunEventTypes.ToolRequested,
            RunEventTypes.UsageRecorded,
            RunEventTypes.MessageCompleted,
            RunEventTypes.ToolStarted,
            RunEventTypes.ToolCompleted,
            RunEventTypes.StepStarted,
            RunEventTypes.UsageRecorded,
            RunEventTypes.MessageCompleted,
            RunEventTypes.RunCompleted);
    }

    [Fact]
    public async Task Deltas_and_the_completed_message_carry_the_id_the_message_is_stored_under()
    {
        var script = new ScriptedModel([new TextOutput("Hel"), new TextOutput("lo")]);
        await using var world = await RunWorld.Create(postgres, script);
        world.StartHost();
        var threadId = await world.NewThread();

        var runId = await world.Post(threadId, "Say hello");
        await world.WaitForStatus(runId, RunStatus.Completed);

        var events = await world.ReadEvents(runId);
        var step = RunWorld.Data<StepChanged>(events.Single(stored => stored.Type == RunEventTypes.StepStarted));
        var deltas = events.Where(stored => stored.Type == RunEventTypes.MessageDelta).Select(RunWorld.Data<MessageDelta>).ToList();
        var completed = RunWorld.Data<MessageCompleted>(events.Single(stored => stored.Type == RunEventTypes.MessageCompleted));
        var stored = (await world.ReadMessages(threadId)).Single(message => message.Role == MessageRole.Assistant);

        deltas.Should().NotBeEmpty().And.OnlyContain(delta => delta.MessageId == step.MessageId && delta.Kind == "text" && delta.PartIndex == 0);
        string.Concat(deltas.Select(delta => delta.Text)).Should().Be("Hello");
        completed.Message.Id.Should().Be(step.MessageId);
        stored.Id.Should().Be(step.MessageId);
        completed.Message.Parts.Should().ContainSingle(part => part.Kind == "text" && part.Text == "Hello");
        var finished = RunWorld.Data<RunStatusChanged>(events[^1]);
        finished.Should().Match<RunStatusChanged>(status => status.Status == "completed" && status.Usage!.Input == 10 && status.Usage.Output == 5);
    }

    [Fact]
    public async Task No_transaction_is_open_during_a_model_call_or_a_tool_call()
    {
        var tools = new TestTools().Add("test.lookup", ToolSensitivity.Read).Add("test.write", ToolSensitivity.Write);
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "test_lookup"), new ToolCallOutput("c2", "test_write")],
            [new TextOutput("Done.")]);
        await using var world = await RunWorld.Create(postgres, script, tools);
        await world.Grant("test.write", GrantEffect.Allow);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Do both");
        await world.WaitForStatus(runId, RunStatus.Completed);

        world.Model.TransactionWasOpen.Should().HaveCount(2).And.OnlyContain(open => !open, "the hook in the model provider saw no open transaction");
        tools.TransactionWasOpen.Should().HaveCount(2).And.OnlyContain(open => !open, "the hook in the tools saw no open transaction");
    }

    #endregion

    #region Restart Recovery

    [Fact]
    public async Task Killing_the_worker_mid_model_call_and_starting_a_new_host_finishes_the_run_without_repeating_a_finished_model_call()
    {
        var tools = new TestTools().Add("test.lookup", ToolSensitivity.Read, """{"answer":42}""");
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "test_lookup")],
            [new TextOutput("Partial "), new PauseOutput()],
            [new TextOutput("It is 42.")]);
        await using var world = await RunWorld.Create(postgres, script, tools);
        var first = world.StartHost();
        var threadId = await world.NewThread();
        var runId = await world.Post(threadId, "What is the answer?");
        await script.Paused.WaitAsync(TimeSpan.FromSeconds(30), RunWorld.Token);

        // A watching client has seen the start of the answer by the time the process dies.
        await world.WaitForEvent(runId, stored => stored.Type == RunEventTypes.MessageDelta);
        var deadWorker = first.WorkerId;

        await first.Kill();
        world.StartHost();
        await world.ExpireLeaseOf(deadWorker, runId);
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        run.Attempt.Should().Be(1, "taking over a lost lease is another attempt");
        script.Requests.Should().HaveCount(3);
        script.Requests.Count(request => request.Count == 1).Should().Be(1, "the first model call finished and is never repeated");
        script.Requests[2].Should().Contain(new SeenMessage("tool", """c1 = {"answer":42}"""));
        tools.Count("test.lookup").Should().Be(1, "a tool that finished does not run again");
        RunWorld.Summaries(await world.ReadMessages(threadId)).Should().Equal(
            "user: What is the answer?",
            "assistant: call c1 test_lookup",
            """tool: result c1 {"answer":42}""",
            "assistant: It is 42.");

        var events = await world.ReadEvents(runId);
        events.Select(stored => stored.Seq).Should().Equal(Enumerable.Range(1, events.Count).Select(number => (long)number));
        var cut = RunWorld.Data<StepChanged>(events.Single(stored => stored.Type == RunEventTypes.StepDiscarded));
        cut.Step.Should().Be(2, "the second model call was the one cut off");
        events.Where(stored => stored.Type == RunEventTypes.MessageDelta).Select(RunWorld.Data<MessageDelta>)
            .Should().Contain(delta => delta.MessageId == cut.MessageId && delta.Text == "Partial ");
        events.Count(stored => stored.Type == RunEventTypes.RunStarted).Should().Be(2);
        events.Select(stored => stored.Type).Should().ContainInOrder(
            RunEventTypes.StepDiscarded, RunEventTypes.StepStarted, RunEventTypes.MessageCompleted, RunEventTypes.RunCompleted);
        (await world.ReadUsage(runId)).Should().HaveCount(2, "the call that was cut off recorded no usage");
    }

    [Fact]
    public async Task Killing_during_a_side_effecting_tool_marks_the_call_interrupted_and_the_model_is_told_the_outcome_is_unknown()
    {
        var tools = new TestTools().AddPausing("test.write", ToolSensitivity.Write);
        var script = new ScriptedModel([new ToolCallOutput("c1", "test_write")], [new TextOutput("It may have been written.")]);
        await using var world = await RunWorld.Create(postgres, script, tools);
        await world.Grant("test.write", GrantEffect.Allow);
        var first = world.StartHost();
        var runId = await world.Post(await world.NewThread(), "Write the file");
        await tools.Paused.WaitAsync(TimeSpan.FromSeconds(30), RunWorld.Token);
        var deadWorker = first.WorkerId;

        await first.Kill();
        world.StartHost();
        await world.ExpireLeaseOf(deadWorker, runId);
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        tools.Count("test.write").Should().Be(1, "a tool that may have side effects is never repeated silently");
        script.Requests[1].Should().Contain(new SeenMessage("tool", "c1 = \"interrupted, outcome unknown\""));
        (await world.ReadToolCalls(runId)).Should().ContainSingle(call => call.Status == ToolCallStatus.Interrupted);
        var events = await world.ReadEvents(runId);
        events.Where(stored => stored.Type == RunEventTypes.ToolCompleted).Select(RunWorld.Data<ToolCompleted>)
            .Should().ContainSingle().Which.Outcome.Should().Be("interrupted");
        events.Should().NotContain(stored => stored.Type == RunEventTypes.StepDiscarded, "the model call that asked for the tool had finished");
    }

    [Fact]
    public async Task Killing_during_a_read_only_tool_runs_the_call_again()
    {
        var tools = new TestTools().AddPausingOnce("test.lookup", ToolSensitivity.Read, """{"answer":42}""");
        var script = new ScriptedModel([new ToolCallOutput("c1", "test_lookup")], [new TextOutput("It is 42.")]);
        await using var world = await RunWorld.Create(postgres, script, tools);
        var first = world.StartHost();
        var runId = await world.Post(await world.NewThread(), "Look it up");
        await tools.Paused.WaitAsync(TimeSpan.FromSeconds(30), RunWorld.Token);
        var deadWorker = first.WorkerId;

        await first.Kill();
        world.StartHost();
        await world.ExpireLeaseOf(deadWorker, runId);
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        tools.Count("test.lookup").Should().Be(2, "a read-only tool is retried");
        script.Requests.Should().HaveCount(2, "the model call that asked for the tool is not repeated");
        script.Requests[1].Should().Contain(new SeenMessage("tool", """c1 = {"answer":42}"""));
        (await world.ReadToolCalls(runId)).Should().ContainSingle(call => call.Status == ToolCallStatus.Completed);
        var events = await world.ReadEvents(runId);
        events.Count(stored => stored.Type == RunEventTypes.ToolStarted).Should().Be(2);
        events.Where(stored => stored.Type == RunEventTypes.ToolCompleted).Select(RunWorld.Data<ToolCompleted>)
            .Should().ContainSingle().Which.Outcome.Should().Be("completed");
    }

    [Fact]
    public async Task A_run_taken_over_three_times_fails_with_lease_lost()
    {
        var script = new ScriptedModel([new PauseOutput()], [new PauseOutput()], [new PauseOutput()], [new TextOutput("Never said.")]);
        await using var world = await RunWorld.Create(postgres, script);
        var runId = await world.Post(await world.NewThread(), "Hang");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var host = world.StartHost();
            var worker = host.WorkerId;
            var watch = Stopwatch.StartNew();

            while (script.Requests.Count <= attempt)
            {
                (watch.Elapsed < TimeSpan.FromSeconds(30)).Should().BeTrue("the host should reach its model call");
                await Task.Delay(25, RunWorld.Token);
            }

            await host.Kill();
            await world.Read(db => db.Database.ExecuteSqlAsync(
                $"UPDATE runs SET lease_expires_at = now() - interval '1 second' WHERE id = {runId} AND lease_owner = {worker}",
                RunWorld.Token));
        }

        world.StartHost();
        var run = await world.WaitForStatus(runId, RunStatus.Failed, RunStatus.Completed);

        run.Status.Should().Be(RunStatus.Failed);
        run.StatusReason.Should().Be(RunReasons.LeaseLost);
        run.Attempt.Should().Be(3);
        script.Requests.Should().HaveCount(3, "the fourth host does not start the run again");
        (await world.ReadEvents(runId))[^1].Type.Should().Be(RunEventTypes.RunFailed);
    }

    [Fact]
    public async Task A_server_that_stops_gracefully_releases_its_run_and_the_next_one_takes_it_over_at_once()
    {
        var script = new ScriptedModel([new TextOutput("Partial "), new PauseOutput()], [new TextOutput("Whole.")]);
        await using var world = await RunWorld.Create(postgres, script);
        var first = world.StartHost();
        var threadId = await world.NewThread();
        var runId = await world.Post(threadId, "Go");
        await script.Paused.WaitAsync(TimeSpan.FromSeconds(30), RunWorld.Token);
        await world.WaitForEvent(runId, stored => stored.Type == RunEventTypes.MessageDelta);

        await first.DisposeAsync();
        world.StartHost();
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed, "no lease had to be expired by hand: the stopping server released it");
        RunWorld.Summaries(await world.ReadMessages(threadId)).Should().Equal("user: Go", "assistant: Whole.");
        (await world.ReadEvents(runId)).Should().Contain(stored => stored.Type == RunEventTypes.StepDiscarded);
    }

    #endregion

    #region Cancel

    [Fact]
    public async Task Cancel_during_streaming_stops_the_run_within_two_seconds_and_writes_run_cancelled()
    {
        var script = new ScriptedModel([new TextOutput("Once upon"), new PauseOutput()]);
        await using var world = await RunWorld.Create(postgres, script);
        world.StartHost(options => options.WatchInterval = TimeSpan.FromSeconds(1));
        var other = world.StartHost(options => options.Workers = 0);
        var threadId = await world.NewThread();
        var runId = await world.Post(threadId, "Tell a long story");
        await script.Paused.WaitAsync(TimeSpan.FromSeconds(30), RunWorld.Token);

        // Through another server: the worker learns of it from the run row, with nothing in memory to tell it.
        var watch = Stopwatch.StartNew();
        (await world.Cancel(runId, other)).Outcome.Should().Be(CancelOutcome.Requested);
        var run = await world.WaitForStatus(runId, RunStatus.Cancelled, RunStatus.Completed, RunStatus.Failed);
        watch.Stop();

        run.Status.Should().Be(RunStatus.Cancelled);
        run.StatusReason.Should().Be(RunReasons.CancelledByUser);
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        script.Requests.Should().HaveCount(1);
        var events = await world.ReadEvents(runId);
        events.TakeLast(2).Select(stored => stored.Type).Should().Equal(RunEventTypes.StepDiscarded, RunEventTypes.RunCancelled);
        RunWorld.Summaries(await world.ReadMessages(threadId)).Should().Equal("user: Tell a long story");
    }

    [Fact]
    public async Task Cancel_on_the_server_that_runs_it_stops_the_run_at_once()
    {
        var script = new ScriptedModel([new TextOutput("Once upon"), new PauseOutput()]);
        await using var world = await RunWorld.Create(postgres, script);
        var host = world.StartHost(options => options.WatchInterval = TimeSpan.FromMinutes(5));
        var runId = await world.Post(await world.NewThread(), "Tell a long story");
        await script.Paused.WaitAsync(TimeSpan.FromSeconds(30), RunWorld.Token);

        await world.Cancel(runId, host);
        var run = await world.WaitForStatus(runId, RunStatus.Cancelled, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Cancelled, "the in-process signal reached the run long before its watch interval");
    }

    [Fact]
    public async Task A_queued_run_is_cancelled_directly_and_never_starts()
    {
        var script = new ScriptedModel([new TextOutput("Never said.")]);
        await using var world = await RunWorld.Create(postgres, script);
        var idle = world.StartHost(options => options.Workers = 0);
        var runId = await world.Post(await world.NewThread(), "Wait in line");

        var cancel = await world.Cancel(runId, idle);

        cancel.Run!.Status.Should().Be(RunStatus.Cancelled);
        (await world.ReadEvents(runId)).Select(stored => stored.Type).Should().Equal(RunEventTypes.RunQueued, RunEventTypes.RunCancelled);
        world.StartHost();
        await Task.Delay(500, RunWorld.Token);
        script.Requests.Should().BeEmpty();
    }

    #endregion

    #region One Run Per Thread

    [Fact]
    public async Task A_thread_runs_one_root_run_at_a_time_and_a_message_sent_meanwhile_is_kept_for_the_next()
    {
        var script = new ScriptedModel([new PauseOutput()], [new TextOutput("Second answer.")]);
        await using var world = await RunWorld.Create(postgres, script);
        var host = world.StartHost();
        var threadId = await world.NewThread();
        var first = await world.Post(threadId, "First");
        await script.Paused.WaitAsync(TimeSpan.FromSeconds(30), RunWorld.Token);

        var early = await world.PostRaw(threadId, "Second, while the first is answered", steer: false);

        early.Problem.Should().Be(PostProblem.None, "a message is never refused for arriving while the bot works");
        early.Run!.Id.Should().Be(first, "the thread still has one run, and it is the first");
        early.Message!.Position.Should().BeNull("the message is stored, and is not part of the conversation yet");
        (await world.ReadRuns(threadId)).Should().ContainSingle();
        await world.Cancel(first, host);
        await world.WaitForStatus(first, RunStatus.Cancelled);
        var runs = await world.ReadRuns(threadId);
        runs.Should().HaveCount(2, "the run that ended queued the run for the message that was waiting");
        (await world.WaitForStatus(runs[1].Id, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        RunWorld.Summaries(await world.ReadMessages(threadId))
            .Should().Equal("user: First", "user: Second, while the first is answered", "assistant: Second answer.");
    }

    #endregion

    #region Events

    [Fact]
    public async Task Events_are_gapless_and_in_commit_order_with_several_runs_writing_at_once()
    {
        const int Runs = 6;
        IReadOnlyList<ModelOutput>[] turns =
        [
            .. Enumerable.Range(0, Runs).Select(_ => (IReadOnlyList<ModelOutput>)
                [.. Enumerable.Range(0, 40).Select(chunk => new TextOutput($"chunk {chunk} {new string('x', 300)} "))]),
        ];
        var script = new ScriptedModel(turns);
        await using var world = await RunWorld.Create(postgres, script);
        var host = world.StartHost();
        var hub = host.Services.GetRequiredService<RunEventHub>();
        List<Guid> threads = [];

        for (var index = 0; index < Runs; index++)
        {
            threads.Add(await world.NewThread());
        }

        // Each run is watched live from before it exists, the way a client that reconnects at once would watch it.
        var posted = await Task.WhenAll(threads.Select(async threadId =>
        {
            var post = await world.PostRaw(threadId, "Write a lot", host);
            return post.Run!.Id;
        }));
        var live = posted.ToDictionary(runId => runId, runId => hub.Subscribe(runId));

        foreach (var runId in posted)
        {
            (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        }

        foreach (var runId in posted)
        {
            var events = await world.ReadEvents(runId);
            events.Select(stored => stored.Seq).Should().Equal(Enumerable.Range(1, events.Count).Select(number => (long)number), "the sequence of each root run is gapless");
            events.Select(stored => stored.CreatedAt).Should().BeInAscendingOrder("a later number was committed after an earlier one");
            events.Count(stored => stored.Type == RunEventTypes.MessageDelta).Should().BeGreaterThan(1, "the output was long enough for several chunks");
            events.Where(stored => stored.Type == RunEventTypes.MessageDelta).Select(RunWorld.Data<MessageDelta>)
                .Should().OnlyContain(delta => delta.Text.Length < 512 + 320, "a chunk is flushed once it reaches the size limit");

            List<RunEventResponse> seen = [];

            while (live[runId].Events.TryRead(out var published))
            {
                seen.Add(published);
            }

            seen.Should().NotBeEmpty();
            seen.Select(published => published.Seq).Should().BeInAscendingOrder("live subscribers get events in the order they committed");
            seen.Select(published => (published.Seq, published.Type, published.Data, published.CreatedAt)).Should().Equal(
                events.Where(stored => stored.Seq >= seen[0].Seq).Select(stored => (stored.Seq, stored.Type, stored.Data, stored.CreatedAt)),
                "live and replay are the same bytes");
            live[runId].Dispose();
        }
    }

    #endregion

    #region Grants

    [Fact]
    public async Task A_tool_with_no_grant_is_not_in_the_tool_list_sent_to_the_model()
    {
        var tools = new TestTools().Add("test.lookup", ToolSensitivity.Read).Add("test.write", ToolSensitivity.Write);
        var script = new ScriptedModel([new ToolCallOutput("c1", "test_write")], [new TextOutput("I cannot.")]);
        await using var world = await RunWorld.Create(postgres, script, tools);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Write it anyway");
        await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        world.Model.ToolsOffered[0].Should().BeEquivalentTo(["test_lookup", "time_now"], "only the granted tools are sent to the model");
        tools.Count("test.write").Should().Be(0, "a tool the model was never given does not run when the model names it anyway");
    }

    [Fact]
    public async Task A_call_to_a_tool_whose_grant_is_gone_is_refused_at_the_gate()
    {
        var tools = new TestTools().Add("test.write", ToolSensitivity.Write);
        var script = new ScriptedModel([new ToolCallOutput("c1", "test_write")], [new TextOutput("I was not allowed.")]);
        await using var world = await RunWorld.Create(postgres, script, tools);
        await world.Grant("test.write", GrantEffect.Allow);

        // The grant is there when the run assembles its tools and gone when the model makes the call.
        world.Model.BeforeCall = call => call == 0 ? world.Revoke("test.write") : Task.CompletedTask;
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Write it");
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        world.Model.ToolsOffered[0].Should().Contain("test_write", "the tool was granted when the run started");
        tools.Count("test.write").Should().Be(0, "the gate reads the grant again when the call is made");
        script.Requests[1].Should().Contain(message => message.Role == "tool" && message.Summary.Contains("no grant", StringComparison.Ordinal));
        (await world.ReadToolCalls(runId)).Should().ContainSingle(call => call.Status == ToolCallStatus.Failed && call.IsError);
        (await world.ReadEvents(runId)).Where(stored => stored.Type == RunEventTypes.ToolCompleted).Select(RunWorld.Data<ToolCompleted>)
            .Should().ContainSingle().Which.Outcome.Should().Be("refused");
    }

    [Fact]
    public async Task A_tool_granted_as_ask_makes_the_run_wait_for_approval()
    {
        var tools = new TestTools().Add("test.write", ToolSensitivity.Write);
        var script = new ScriptedModel([new ToolCallOutput("c1", "test_write")]);
        await using var world = await RunWorld.Create(postgres, script, tools);
        await world.Grant("test.write", GrantEffect.Ask);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Write it");
        var run = await world.WaitForStatus(runId, RunStatus.Waiting, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Waiting, run.Error?.Message);
        run.StatusReason.Should().Be(RunReasons.AwaitingApproval);
        run.LeaseOwner.Should().BeNull("no worker holds a waiting run");
        tools.Count("test.write").Should().Be(0);
        (await world.ReadToolCalls(runId)).Should().ContainSingle(call => call.Status == ToolCallStatus.PendingApproval);
        var events = await world.ReadEvents(runId);
        events.Select(stored => stored.Type).Should().ContainInOrder(
            RunEventTypes.ToolRequested, RunEventTypes.ToolApprovalRequested, RunEventTypes.MessageCompleted, RunEventTypes.RunWaiting);
        RunWorld.Data<ToolRequested>(events.Single(stored => stored.Type == RunEventTypes.ToolRequested)).Effect.Should().Be("ask");
    }

    #endregion

    #region Limits And Failures

    [Fact]
    public async Task A_run_that_reaches_the_step_limit_fails_with_step_limit()
    {
        var tools = new TestTools().Add("test.lookup", ToolSensitivity.Read);
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "test_lookup")],
            [new ToolCallOutput("c2", "test_lookup")],
            [new ToolCallOutput("c3", "test_lookup")]);
        await using var world = await RunWorld.Create(postgres, script, tools);
        world.StartHost(options => options.StepLimit = 2);

        var runId = await world.Post(await world.NewThread(), "Loop");
        var run = await world.WaitForStatus(runId, RunStatus.Failed, RunStatus.Completed);

        run.Status.Should().Be(RunStatus.Failed);
        run.StatusReason.Should().Be(RunReasons.StepLimit);
        script.Requests.Should().HaveCount(2, "the third model call is never made");
        (await world.ReadEvents(runId))[^1].Type.Should().Be(RunEventTypes.RunFailed);
    }

    [Fact]
    public async Task A_provider_failure_fails_the_run_with_the_providers_reason_in_plain_words()
    {
        var script = new ScriptedModel([new TextOutput("Never said.")]);
        await using var world = await RunWorld.Create(postgres, script);
        world.Model.FailNext = ModelProviderException.FromStatus(402);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Hello");
        var run = await world.WaitForStatus(runId, RunStatus.Failed, RunStatus.Completed);

        run.Status.Should().Be(RunStatus.Failed);
        run.Error.Should().Be(new RunError("out_of_credit", "The provider says the account is out of credit."));
        var events = await world.ReadEvents(runId);
        events.TakeLast(2).Select(stored => stored.Type).Should().Equal(RunEventTypes.StepDiscarded, RunEventTypes.RunFailed);
        RunWorld.Data<RunStatusChanged>(events[^1]).Error!.Kind.Should().Be("out_of_credit");
    }

    #endregion

    #region Redaction

    [Fact]
    public async Task A_revealed_secret_is_redacted_from_tool_arguments_and_results_before_they_are_stored_or_emitted()
    {
        var key = RunWorld.NewModelKey();
        var tools = new TestTools().Add("test.echo", ToolSensitivity.Read, (arguments, _) => Task.FromResult(arguments));
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "test_echo", $$"""{"note":"the key is {{key}}"}""")],
            [new TextOutput("Done.")]);
        await using var leaky = await RunWorld.Create(postgres, script, tools, key);
        leaky.StartHost();

        var threadId = await leaky.NewThread();
        var runId = await leaky.Post(threadId, "Echo it");
        (await leaky.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        var leakedKey = leaky.ModelKey;
        leaky.Model.KeysSeen.Should().Contain(leakedKey, "the provider is the only place the key goes");
        var call = (await leaky.ReadToolCalls(runId)).Single();
        call.Arguments.GetRawText().Should().NotContain(leakedKey).And.Contain(Redactor.Mark);
        call.Result!.Value.GetRawText().Should().NotContain(leakedKey).And.Contain(Redactor.Mark);
        (await leaky.ReadEvents(runId)).Should().OnlyContain(stored => !stored.Data.Contains(leakedKey));
        (await leaky.ReadMessages(threadId)).Should().OnlyContain(message => !JsonSerializer.Serialize(message.Parts).Contains(leakedKey));
        script.Requests[1].Should().OnlyContain(message => !message.Summary.Contains(leakedKey), "the result the model sees is redacted too");
    }

    #endregion

    #region Tenancy

    [Fact]
    public async Task A_worker_in_space_As_scope_cannot_read_space_Bs_run_even_when_handed_its_id()
    {
        var script = new ScriptedModel([new TextOutput("Never said.")]);
        await using var world = await RunWorld.Create(postgres, script);
        var host = world.StartHost(options => options.Workers = 0);
        var runId = await world.Post(await world.NewThread(), "Private to this space");
        const string Worker = "a-worker";
        await world.Read(db => db.Database.ExecuteSqlAsync(
            $"UPDATE runs SET status = 'running', lease_owner = {Worker}, lease_expires_at = now() + interval '45 seconds' WHERE id = {runId}",
            RunWorld.Token));
        var (otherSpace, otherUser) = await SeedOtherSpace(world);

        // The run is leased to this very worker. Only the space is wrong.
        await host.Services.GetRequiredService<RunExecutor>().Execute(new ClaimedRun(runId, otherSpace, otherUser), Worker, RunWorld.Token);
        var seenFromOtherSpace = await world.TransactAs(otherSpace, otherUser, async services =>
        {
            var run = await services.GetRequiredService<RunRepository>().FindRun(runId, RunWorld.Token);
            var events = await services.GetRequiredService<RunEventRepository>().ListEvents(runId, 0, 100, RunWorld.Token);
            return (run, events);
        });

        seenFromOtherSpace.run.Should().BeNull();
        seenFromOtherSpace.events.Should().BeEmpty();
        script.Requests.Should().BeEmpty("the model was never called");
        var untouched = await world.ReadRun(runId);
        untouched.Should().Match<Run>(run => run.Status == RunStatus.Running && run.LeaseOwner == Worker);
        (await world.ReadEvents(runId)).Select(stored => stored.Type).Should().Equal(RunEventTypes.RunQueued);
    }

    private static async Task<(Guid SpaceId, Guid UserId)> SeedOtherSpace(RunWorld world)
    {
        var spaceId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        await world.TransactAs(spaceId, userId, services =>
        {
            var db = services.GetRequiredService<DroverDbContext>();
            db.Users.Add(new User
            {
                Id = userId,
                UserName = $"user-{userId:N}",
                NormalizedUserName = $"USER-{userId:N}".ToUpperInvariant(),
                Email = $"{userId:N}@example.test",
                NormalizedEmail = $"{userId:N}@EXAMPLE.TEST".ToUpperInvariant(),
                DisplayName = "Someone else",
            });
            db.Spaces.Add(new Space { Id = spaceId, Kind = SpaceKind.Personal, Name = "Other", Slug = $"other-{spaceId:N}" });
            db.Memberships.Add(new Membership { UserId = userId, Role = MembershipRole.Owner });
            return Task.FromResult(true);
        });
        return (spaceId, userId);
    }

    #endregion
}
