namespace Drover.Adapters.Conformance.Agents;

/// <summary>
/// What every <see cref="IAgentRuntime"/> must do, against a scripted model. "Kill" always means a new host with a
/// new service provider, never a retry inside one process. This suite gates every agent framework update.
/// </summary>
public abstract class AgentRuntimeConformance
{
    #region Private Fields

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    #endregion

    #region Plain Runs

    [Fact]
    public async Task A_plain_reply_streams_text_and_is_stored()
    {
        var run = await NewRun("Say hello");
        var model = new ScriptedModel([new TextOutput("Hel"), new TextOutput("lo")]);

        var events = await Execute(model, run, new ScriptedGovernor(new ScriptedTools()));

        events.OfType<TextDelta>().Select(delta => delta.Text).Should().Equal("Hel", "lo");
        events[^1].Should().Be(new RunFinished(RunOutcome.Completed));
        Summaries(await ReadMessages(run)).Should().Equal("user: Say hello", "assistant: Hello");
    }

    [Fact]
    public async Task An_allowed_tool_runs_once_and_its_result_reaches_the_model()
    {
        var run = await NewRun("Look it up", new ToolSpec("lookup"));
        var tools = new ScriptedTools().Add("lookup", """{"answer":42}""");
        var model = new ScriptedModel([new ToolCallOutput("c1", "lookup")], [new TextOutput("It is 42")]);

        var events = await Execute(model, run, new ScriptedGovernor(tools));

        tools.Count("lookup").Should().Be(1);
        events.OfType<ToolCallRequested>().Should().ContainSingle(requested => requested.ToolName == "lookup");
        events.OfType<ToolCallFinished>().Should().ContainSingle(finished => !finished.IsError);
        model.Requests[1].Should().Contain(new SeenMessage("tool", """c1 = {"answer":42}"""));
        (await ReadToolCalls(run)).Should().ContainSingle(call => call.Status == ToolCallStatus.Completed);
        Summaries(await ReadMessages(run)).Should().Equal(
            "user: Look it up", "assistant: call c1 lookup", """tool: result c1 {"answer":42}""", "assistant: It is 42");
    }

    [Fact]
    public async Task A_refused_tool_never_runs_and_the_model_is_told_why()
    {
        var run = await NewRun("Delete everything", new ToolSpec("delete_all", ToolSensitivity.Critical));
        var tools = new ScriptedTools().Add("delete_all");
        var governor = new ScriptedGovernor(tools, new Dictionary<string, ToolGateDecision>
        {
            ["delete_all"] = ToolGateDecision.Refuse("No grant for delete_all."),
        });
        var model = new ScriptedModel([new ToolCallOutput("c1", "delete_all")], [new TextOutput("I cannot.")]);

        var events = await Execute(model, run, governor);

        tools.Count("delete_all").Should().Be(0);
        governor.Decided.Should().ContainSingle(decided => decided.ToolName == "delete_all");
        events.OfType<ToolCallFinished>().Should().ContainSingle(finished => finished.IsError);
        model.Requests[1].Should().Contain(new SeenMessage("tool", """c1 = {"error":"No grant for delete_all."}"""));
        (await ReadToolCalls(run)).Should().ContainSingle(call => call.Status == ToolCallStatus.Failed && call.IsError);
    }

    [Fact]
    public async Task Read_only_tools_requested_together_run_in_parallel()
    {
        var run = await NewRun("Compare", new ToolSpec("left"), new ToolSpec("right"));
        var bothStarted = new CountdownEvent(2);
        var tools = new ScriptedTools()
            .Add("left", (_, cancellationToken) => WaitForBoth(bothStarted, cancellationToken))
            .Add("right", (_, cancellationToken) => WaitForBoth(bothStarted, cancellationToken));
        var model = new ScriptedModel(
            [new ToolCallOutput("c1", "left"), new ToolCallOutput("c2", "right")],
            [new TextOutput("Same")]);

        var events = await Execute(model, run, new ScriptedGovernor(tools));

        events.OfType<ToolCallFinished>().Should().HaveCount(2).And.OnlyContain(finished => !finished.IsError);
        events[^1].Should().Be(new RunFinished(RunOutcome.Completed));
    }

    [Fact]
    public async Task Tools_that_write_run_in_the_order_the_model_asked_for_them()
    {
        var run = await NewRun("Two steps", new ToolSpec("first", ToolSensitivity.Write), new ToolSpec("second", ToolSensitivity.Write));
        var order = new ConcurrentQueue<string>();
        var tools = new ScriptedTools()
            .Add("first", async (_, cancellationToken) =>
            {
                await Task.Delay(200, cancellationToken);
                order.Enqueue("first");
                return new ToolOutcome(ScriptedTools.Json("{}"));
            })
            .Add("second", (_, _) =>
            {
                order.Enqueue("second");
                return Task.FromResult(new ToolOutcome(ScriptedTools.Json("{}")));
            });
        var model = new ScriptedModel(
            [new ToolCallOutput("c1", "first"), new ToolCallOutput("c2", "second")],
            [new TextOutput("Done")]);

        await Execute(model, run, new ScriptedGovernor(tools));

        order.Should().Equal("first", "second");
    }

    [Fact]
    public async Task Cancelling_mid_stream_stops_the_run_without_calling_the_model_again()
    {
        var run = await NewRun("Write a long story");
        var model = new ScriptedModel([new TextOutput("Once upon"), new PauseOutput()]);
        await using var host = StartHost(model);
        using var cancellation = new CancellationTokenSource();
        List<AgentEvent> events = [];

        var act = async () =>
        {
            await foreach (var agentEvent in host.Runtime.Execute(run, new ScriptedGovernor(new ScriptedTools()), cancellation.Token))
            {
                events.Add(agentEvent);

                if (agentEvent is TextDelta)
                {
                    await cancellation.CancelAsync();
                }
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        events.Should().ContainSingle().Which.Should().Be(new TextDelta("Once upon"));
        model.Requests.Should().HaveCount(1);
    }

    #endregion

    #region Approval

    [Fact]
    public async Task An_approval_request_survives_a_restart_and_the_tool_then_runs_exactly_once()
    {
        var run = await NewRun("Email the report", new ToolSpec("send_email", ToolSensitivity.Write, RequiresApproval: true));
        var tools = new ScriptedTools().Add("send_email", """{"sent":true}""");
        var model = new ScriptedModel([new ToolCallOutput("c1", "send_email", """{"to":"team"}""")], [new TextOutput("Sent.")]);

        IReadOnlyList<AgentEvent> asked;
        await using (var first = StartHost(model))
        {
            asked = await Collect(first, run, new ScriptedGovernor(tools));
        }

        var request = asked.OfType<ApprovalRequested>().Should().ContainSingle().Subject;
        asked[^1].Should().Be(new RunFinished(RunOutcome.Waiting));
        tools.Count("send_email").Should().Be(0);
        (await ReadToolCalls(run)).Should().ContainSingle(call => call.Status == ToolCallStatus.PendingApproval);

        await using var second = StartHost(model);
        var resumed = await Collect(
            second,
            run with { Responses = [new ToolApproval(request.RequestId, request.CallId, Approved: true)] },
            new ScriptedGovernor(tools));

        tools.Count("send_email").Should().Be(1);
        resumed[^1].Should().Be(new RunFinished(RunOutcome.Completed));
        (await ReadToolCalls(run)).Should().ContainSingle(call => call.Status == ToolCallStatus.Completed);
        model.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_denied_approval_never_runs_the_tool()
    {
        var run = await NewRun("Email the report", new ToolSpec("send_email", ToolSensitivity.Write, RequiresApproval: true));
        var tools = new ScriptedTools().Add("send_email");
        var model = new ScriptedModel([new ToolCallOutput("c1", "send_email")], [new TextOutput("Not sent.")]);

        var asked = await Execute(model, run, new ScriptedGovernor(tools));
        var request = asked.OfType<ApprovalRequested>().Single();
        var resumed = await Execute(
            model,
            run with { Responses = [new ToolApproval(request.RequestId, request.CallId, Approved: false, "Not today")] },
            new ScriptedGovernor(tools));

        tools.Count("send_email").Should().Be(0);
        resumed[^1].Should().Be(new RunFinished(RunOutcome.Completed));
        model.Requests[1].Should().Contain(message => message.Role == "tool" && message.Summary.StartsWith("c1 = ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_approval_response_for_an_unknown_request_is_rejected_and_the_tool_never_runs()
    {
        var run = await NewRun("Email the report", new ToolSpec("send_email", ToolSensitivity.Write, RequiresApproval: true));
        var tools = new ScriptedTools().Add("send_email");
        var model = new ScriptedModel([new ToolCallOutput("c1", "send_email")], [new TextOutput("Not sent.")]);

        var asked = await Execute(model, run, new ScriptedGovernor(tools));
        var request = asked.OfType<ApprovalRequested>().Single();
        var resumed = await Execute(
            model,
            run with { Responses = [new ToolApproval($"not-{request.RequestId}", request.CallId, Approved: true)] },
            new ScriptedGovernor(tools));

        tools.Count("send_email").Should().Be(0);
        resumed[^1].Should().Be(new RunFinished(RunOutcome.Completed));
        (await ReadToolCalls(run)).Should().ContainSingle(call => call.Status == ToolCallStatus.Denied);
    }

    [Fact]
    public async Task An_approved_tool_cut_off_by_a_kill_is_reported_interrupted_not_rejected()
    {
        var run = await NewRun("Email the report", new ToolSpec("send_email", ToolSensitivity.Write, RequiresApproval: true));
        var tools = new ScriptedTools().AddPausing("send_email");
        var model = new ScriptedModel([new ToolCallOutput("c1", "send_email")], [new TextOutput("It may have been sent.")]);

        var request = (await Execute(model, run, new ScriptedGovernor(tools))).OfType<ApprovalRequested>().Single();
        await Kill(model, tools.Paused, run with { Responses = [new ToolApproval(request.RequestId, request.CallId, Approved: true)] }, tools);

        await using var recovering = StartHost(model);
        var events = await Collect(recovering, run, new ScriptedGovernor(tools));

        events[^1].Should().Be(new RunFinished(RunOutcome.Completed));
        tools.Count("send_email").Should().Be(1);
        model.Requests.Should().HaveCount(2);
        model.Requests[1].Should().ContainSingle(message => message.Role == "tool")
            .Which.Should().Be(new SeenMessage("tool", "c1 = \"interrupted, outcome unknown\""));
        (await ReadToolCalls(run)).Should().ContainSingle(call => call.Status == ToolCallStatus.Interrupted);
        Summaries(await ReadMessages(run)).Should().Contain("tool: result c1 \"interrupted, outcome unknown\"");
    }

    #endregion

    #region Kill And Recover

    [Fact]
    public async Task History_saved_per_service_call_lets_a_killed_run_continue_without_repeating_a_finished_model_call()
    {
        var run = await NewRun("What is the answer?", new ToolSpec("lookup"));
        var tools = new ScriptedTools().Add("lookup", """{"answer":42}""");
        var model = new ScriptedModel(
            [new ToolCallOutput("c1", "lookup")],
            [new PauseOutput()],
            [new TextOutput("42")]);

        await Kill(model, model.Paused, run, tools);

        await using var second = StartHost(model);
        var events = await Collect(second, run, new ScriptedGovernor(tools));

        events[^1].Should().Be(new RunFinished(RunOutcome.Completed));
        model.Requests.Should().HaveCount(3);
        model.Requests.Count(request => request.Count == 1).Should().Be(1, "the first model call finished and is never repeated");
        model.Requests[2].Should().Contain(new SeenMessage("tool", """c1 = {"answer":42}"""));
        tools.Count("lookup").Should().Be(1);
        Summaries(await ReadMessages(run)).Should().Equal(
            "user: What is the answer?", "assistant: call c1 lookup", """tool: result c1 {"answer":42}""", "assistant: 42");
    }

    [Fact]
    public async Task A_tool_call_cut_off_by_a_kill_is_reported_interrupted_and_not_repeated()
    {
        var run = await NewRun("Write the file", new ToolSpec("write_file", ToolSensitivity.Write));
        var tools = new ScriptedTools().AddPausing("write_file");
        var model = new ScriptedModel([new ToolCallOutput("c1", "write_file")], [new TextOutput("It may not have been written.")]);

        await Kill(model, tools.Paused, run, tools);

        await using var second = StartHost(model);
        var events = await Collect(second, run, new ScriptedGovernor(tools));

        events[^1].Should().Be(new RunFinished(RunOutcome.Completed));
        tools.Count("write_file").Should().Be(1);
        model.Requests[1].Should().Contain(new SeenMessage("tool", "c1 = \"interrupted, outcome unknown\""));
        (await ReadToolCalls(run)).Should().ContainSingle(call => call.Status == ToolCallStatus.Interrupted);
    }

    [Fact]
    public async Task A_read_only_tool_call_cut_off_by_a_kill_runs_again_and_its_result_reaches_the_model()
    {
        var run = await NewRun("Look it up", new ToolSpec("lookup", ToolSensitivity.Read));
        var tools = new ScriptedTools().AddPausingOnce("lookup", """{"answer":42}""");
        var model = new ScriptedModel([new ToolCallOutput("c1", "lookup")], [new TextOutput("It is 42")]);

        await Kill(model, tools.Paused, run, tools);
        await using var second = StartHost(model);
        var governor = new ScriptedGovernor(tools);
        var events = await Collect(second, run, governor);

        events[^1].Should().Be(new RunFinished(RunOutcome.Completed));
        tools.Count("lookup").Should().Be(2, "a tool that only reads is safe to run again");
        governor.Decided.Should().ContainSingle(decided => decided.ToolName == "lookup", "the grant is checked again before the tool runs again");
        model.Requests.Should().HaveCount(2, "the model call that asked for the tool finished and is not repeated");
        model.Requests[1].Should().Contain(new SeenMessage("tool", """c1 = {"answer":42}"""));
        (await ReadToolCalls(run)).Should().ContainSingle(call => call.Status == ToolCallStatus.Completed);
        Summaries(await ReadMessages(run)).Should().Equal(
            "user: Look it up", "assistant: call c1 lookup", """tool: result c1 {"answer":42}""", "assistant: It is 42");
    }

    [Fact]
    public async Task A_cut_off_read_only_call_the_bot_may_no_longer_make_is_reported_interrupted()
    {
        var run = await NewRun("Look it up", new ToolSpec("lookup", ToolSensitivity.Read));
        var tools = new ScriptedTools().AddPausingOnce("lookup");
        var model = new ScriptedModel([new ToolCallOutput("c1", "lookup")], [new TextOutput("I could not finish looking.")]);

        await Kill(model, tools.Paused, run, tools);
        await using var second = StartHost(model);
        var refusing = new ScriptedGovernor(tools, new Dictionary<string, ToolGateDecision>
        {
            ["lookup"] = ToolGateDecision.Refuse("No grant for lookup."),
        });
        await Collect(second, run, refusing);

        tools.Count("lookup").Should().Be(1);
        model.Requests[1].Should().Contain(new SeenMessage("tool", "c1 = \"interrupted, outcome unknown\""));
        (await ReadToolCalls(run)).Should().ContainSingle(call => call.Status == ToolCallStatus.Interrupted);
    }

    [Fact]
    public async Task A_call_id_reused_by_a_later_run_is_repaired_from_that_runs_own_row()
    {
        var first = await NewRun("Count once", new ToolSpec("count"));
        var counted = 0;
        var tools = new ScriptedTools().Add("count", (_, _) =>
            Task.FromResult(new ToolOutcome(ScriptedTools.Json($$"""{"n":{{Interlocked.Increment(ref counted)}}}"""))));
        var model = new ScriptedModel(
            [new ToolCallOutput("c1", "count")],
            [new TextOutput("One")],
            [new ToolCallOutput("c1", "count")],
            [new PauseOutput()],
            [new TextOutput("Two")]);

        await Execute(model, first, new ScriptedGovernor(tools));
        var second = await ContinueThread(first, "Count again");
        await Kill(model, model.Paused, second, tools);

        await using var recovering = StartHost(model);
        await Collect(recovering, second, new ScriptedGovernor(tools));

        tools.Count("count").Should().Be(2);
        model.Requests[^1].Where(message => message.Role == "tool").Select(message => message.Summary)
            .Should().Equal("""c1 = {"n":1}""", """c1 = {"n":2}""");
    }

    #endregion

    #region Harness

    /// <summary>A thread with one user message, a run row, and the given tools granted, as the engine would make them.</summary>
    protected abstract Task<RunExecution> NewRun(string userText, params ToolSpec[] tools);

    /// <summary>A new run in the same thread after another user message, as the engine makes one for the next turn.</summary>
    protected abstract Task<RunExecution> ContinueThread(RunExecution previous, string userText);

    /// <summary>A new process: a new service provider, sharing only the database, the model, and the tools.</summary>
    protected abstract IRuntimeHost StartHost(ScriptedModel model);

    protected abstract Task<IReadOnlyList<Message>> ReadMessages(RunExecution run);

    protected abstract Task<IReadOnlyList<ToolCall>> ReadToolCalls(RunExecution run);

    protected async Task<IReadOnlyList<AgentEvent>> Execute(ScriptedModel model, RunExecution run, IRunGovernor governor)
    {
        await using var host = StartHost(model);
        return await Collect(host, run, governor);
    }

    protected static async Task<IReadOnlyList<AgentEvent>> Collect(IRuntimeHost host, RunExecution run, IRunGovernor governor)
    {
        List<AgentEvent> events = [];

        await foreach (var agentEvent in host.Runtime.Execute(run, governor, TestContext.Current.CancellationToken))
        {
            events.Add(agentEvent);
        }

        return events;
    }

    /// <summary>
    /// Runs on a host until <paramref name="killPoint"/> is reached, then disposes the host without cancelling the run.
    /// The abandoned run stays stuck at that point, so nothing after it happens, exactly as if the process died.
    /// </summary>
    protected async Task Kill(ScriptedModel model, Task killPoint, RunExecution run, ScriptedTools tools)
    {
        var host = StartHost(model);
        var abandoned = Collect(host, run, new ScriptedGovernor(tools));

        var reached = await Task.WhenAny(killPoint, abandoned).WaitAsync(patience, TestContext.Current.CancellationToken);
        reached.Should().BeSameAs(killPoint, "the run must reach the kill point before it can be killed there");
        await host.DisposeAsync();
    }

    protected static IReadOnlyList<string> Summaries(IReadOnlyList<Message> messages) =>
        [.. messages.Select(message => $"{message.Role.ToString().ToLowerInvariant()}: {string.Join(" | ", message.Parts.Select(Summary))}")];

    #endregion

    #region Private Methods

    private static string Summary(MessagePart part) => part switch
    {
        TextPart text => text.Text,
        ReasoningPart reasoning => $"reasoning {reasoning.Text}",
        ToolCallPart call => $"call {call.CallId} {call.ToolName}",
        ToolResultPart result => $"result {result.CallId} {JsonSerializer.Serialize(result.Result)}",
        _ => part.GetType().Name,
    };

    private static async Task<ToolOutcome> WaitForBoth(CountdownEvent bothStarted, CancellationToken cancellationToken)
    {
        bothStarted.Signal();
        var together = await Task.Run(() => bothStarted.Wait(patience, cancellationToken), cancellationToken);
        together.Should().BeTrue("both read-only tools must be running at the same time");
        return new ToolOutcome(ScriptedTools.Json("{}"));
    }

    #endregion
}
