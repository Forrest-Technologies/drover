namespace Drover.Adapters.Tests.Agents;

/// <summary>
/// The proofs from docs/architecture/14-agent-framework.md that need the framework's own types, and the session
/// rules. Proofs 1 and 2 are in the runtime conformance suite, because they hold for any runtime.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class AgentFrameworkProofs(PostgresFixture postgres) : IAsyncLifetime
{
    #region Private Fields

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    private ServiceProvider services = null!; // set in InitializeAsync

    private AgentWorld world = null!; // set in InitializeAsync

    #endregion

    #region Interface Implementations

    public async ValueTask InitializeAsync()
    {
        services = postgres.NewServices().BuildServiceProvider();
        world = await AgentWorld.Create(services);
    }

    public ValueTask DisposeAsync() => services.DisposeAsync();

    #endregion

    #region Proof 3: The Gate Sees Every Call

    [Fact]
    public async Task The_function_middleware_sees_every_tool_call_including_one_a_harness_capability_added()
    {
        var run = await world.NewRun("Plan and look up", [new ToolSpec("lookup")]);
        var tools = new ScriptedTools().Add("lookup");
        var governor = new ScriptedGovernor(tools, new Dictionary<string, ToolGateDecision>
        {
            ["todos_get_all"] = ToolGateDecision.Refuse("No grant for todos_get_all."),
        });
        var model = new ScriptedModel(
            [new ToolCallOutput("c1", "todos_get_all"), new ToolCallOutput("c2", "lookup")],
            [new TextOutput("Done")]);
        await using var host = new AgentRuntimeHost(postgres, model, _ => [new TodoProvider()]);

        await Drain(host.Runtime.Execute(run, governor, TestContext.Current.CancellationToken));

        governor.Decided.Select(call => call.ToolName).Should().BeEquivalentTo(["todos_get_all", "lookup"]);
        governor.Decided.Single(call => call.ToolName == "todos_get_all").ToolId.Should().BeNull();
        model.Requests[1].Should().Contain(new SeenMessage("tool", """c1 = {"error":"No grant for todos_get_all."}"""));
        tools.Count("lookup").Should().Be(1);
    }

    #endregion

    #region Proof 4: A Workflow Resumes From A PostgreSQL Checkpoint

    [Fact]
    public async Task A_workflow_resumes_in_a_new_process_from_a_postgres_checkpoint_with_its_pending_request_raised_again()
    {
        var writes = new WriteCounter();
        string sessionId;
        string firstRequestId;
        CheckpointInfo firstCheckpoint;

        await using (var first = postgres.NewServices().BuildServiceProvider())
        {
            var manager = CheckpointManager.CreateJson(new PostgresCheckpointStore(Database(first), world.UserId));
            await using var started = await InProcessExecution.RunStreamingAsync(
                ReviewWorkflow.Build(writes), "draft", manager, cancellationToken: TestContext.Current.CancellationToken);
            sessionId = started.SessionId;
            var asked = await NextRequest(started);
            firstRequestId = asked.Request.RequestId;
            asked.Request.TryGetDataAs<ReviewRequest>(out var firstAsk).Should().BeTrue();
            firstAsk!.Draft.Should().Be("draft, written");
            firstCheckpoint = await NextRequestCheckpoint(started);

            // Killed while the request is pending: the response is never sent in this process.
        }

        await using var second = postgres.NewServices().BuildServiceProvider();
        var resumedManager = CheckpointManager.CreateJson(new PostgresCheckpointStore(Database(second), world.UserId));
        var latest = await resumedManager.GetLatestCheckpointAsync(sessionId, TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("No checkpoint was stored for the first process's run.");
        latest.CheckpointId.Should().Be(firstCheckpoint.CheckpointId,
            "the pending request checkpoint was committed before restart");
        await using var resumed = await InProcessExecution.ResumeStreamingAsync(
            ReviewWorkflow.Build(writes), latest, resumedManager, TestContext.Current.CancellationToken);

        var raisedAgain = await NextRequest(resumed);
        raisedAgain.Request.RequestId.Should().Be(firstRequestId, "the request comes from the checkpoint, not from a new one");
        raisedAgain.Request.TryGetDataAs<ReviewRequest>(out var secondAsk).Should().BeTrue();
        secondAsk!.Draft.Should().Be("draft, written");
        writes.Count.Should().Be(1, "the writer ran before the checkpoint and must not run again on resume");

        await resumed.SendResponseAsync(raisedAgain.Request.CreateResponse(true));
        (await NextOutput(resumed)).Should().Be("published: draft, written");
        writes.Count.Should().Be(1);
    }

    #endregion

    #region Proof 5: Governed Agents Inside The Orchestration Builders

    [Theory]
    [InlineData("sequential")]
    [InlineData("concurrent")]
    [InlineData("handoff")]
    public async Task A_governed_agent_is_honored_inside_the_orchestration_builder(string pattern)
    {
        var tools = new ScriptedTools().Add("delete_all");
        var governor = new ScriptedGovernor(tools, new Dictionary<string, ToolGateDecision>
        {
            ["delete_all"] = ToolGateDecision.Refuse("No grant for delete_all."),
        })
        {
            ReplyRule = said => said,
        };
        var parentRunId = Guid.CreateVersion7();
        var run = await TeamRun(governor);
        var first = Member("first", run, parentRunId, pattern == "handoff"
            ? new ScriptedModel([new ToolCallOutput("h1", $"{HandoffWorkflowBuilder.FunctionPrefix}1")])
            : new ScriptedModel([new TextOutput("from first")]));
        var secondModel = new ScriptedModel([new ToolCallOutput("d1", "delete_all")], [new TextOutput("I may not.")]);
        var second = Member("second", run, parentRunId, secondModel);
        var workflow = pattern switch
        {
            "sequential" => AgentWorkflowBuilder.BuildSequential([first, second]),
            "concurrent" => AgentWorkflowBuilder.BuildConcurrent([first, second]),
            _ => AgentWorkflowBuilder.CreateHandoffBuilderWith(first).WithHandoffs(first, [second]).Build(),
        };

        await using var streaming = await InProcessExecution.RunStreamingAsync(
            workflow,
            new List<ChatMessage> { new(ChatRole.User, "Work on this") },
            cancellationToken: TestContext.Current.CancellationToken);
        await streaming.TrySendMessageAsync(new TurnToken(emitEvents: true));
        await RunUntilIdle(streaming);

        governor.Delegations.Select(delegation => delegation.AgentName).Should().BeEquivalentTo(["first", "second"]);
        governor.Delegations.Should().OnlyContain(delegation => delegation.ParentRunId == parentRunId);
        governor.Completions.Should().HaveCount(2).And.OnlyContain(completion => completion.Outcome == ChildRunOutcome.Completed);

        // A member's own tool call reaches the gate inside every builder, and a refused grant holds there.
        governor.Decided.Should().ContainSingle(call => call.ToolName == "delete_all");
        tools.Count("delete_all").Should().Be(0);
        secondModel.Requests[1].Should().Contain(new SeenMessage("tool", """d1 = {"error":"No grant for delete_all."}"""));

        // A handoff is a declaration the workflow routes, never an invoked function, so it does not pass the tool
        // gate; it is governed where it lands, by the target member's GovernedAgent asking for a child run.
        governor.Decided.Should().NotContain(call => call.ToolName.StartsWith(HandoffWorkflowBuilder.FunctionPrefix, StringComparison.Ordinal));

        // So a rule on tools cannot see a handoff itself. What the member it lands on does is ruled like any other
        // agent's work: its tool calls at the gate above, and what it says before any of it is sent.
        governor.RuledReplies.Should().Contain("I may not.");
    }

    [Fact]
    public async Task A_cancelled_governed_agent_closes_its_child_run_as_cancelled()
    {
        var governor = new ScriptedGovernor(new ScriptedTools());
        var model = new ScriptedModel([new PauseOutput()]);
        var member = new GovernedAgent(new ScriptedChatClient(model).AsAIAgent(name: "slow"), governor, Guid.CreateVersion7());
        using var cancellation = new CancellationTokenSource();

        var running = member.RunAsync("Take your time", cancellationToken: cancellation.Token);
        await model.Paused.WaitAsync(patience, TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await FluentActions.Awaiting(() => running).Should().ThrowAsync<OperationCanceledException>();
        governor.Completions.Should().ContainSingle().Which.Outcome.Should().Be(ChildRunOutcome.Cancelled);
    }

    #endregion

    #region Tool Identity

    [Fact]
    public async Task A_capability_tool_named_like_a_catalog_tool_is_refused_before_the_model_sees_either()
    {
        var run = await world.NewRun("Look it up", [new ToolSpec("lookup")]);
        var tools = new ScriptedTools().Add("lookup");
        var model = new ScriptedModel([new ToolCallOutput("c1", "lookup")], [new TextOutput("Done")]);
        await using var host = new AgentRuntimeHost(postgres, model, _ => [new LookalikeCapability("lookup")]);

        var act = () => Drain(host.Runtime.Execute(run, new ScriptedGovernor(tools), TestContext.Current.CancellationToken));

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("named lookup").And.Contain("must be unique");
        model.Requests.Should().BeEmpty();
        tools.Count("lookup").Should().Be(0);
    }

    #endregion

    #region Sessions

    [Fact]
    public async Task A_session_written_by_another_framework_version_is_discarded_and_the_run_continues_from_messages()
    {
        var run = await world.NewRun("First question", []);
        await RunOnce(run, new ScriptedModel([new TextOutput("First answer")]));
        await world.Write(db => db.AgentSessions
            .Where(session => session.ThreadId == run.ThreadId)
            .ExecuteUpdateAsync(session => session.SetProperty(row => row.FrameworkVersion, "0.0.1")));

        await world.AddUserMessage(run.ThreadId, "Second question");
        var model = new ScriptedModel([new TextOutput("Second answer")]);
        await RunOnce(run, model);

        model.Requests.Single().Select(message => message.Summary).Should().Equal("First question", "First answer", "Second question");
        (await StoredVersion(run)).Should().Be(PostgresAgentSessionStoreVersion());
    }

    [Fact]
    public async Task A_session_that_no_longer_deserializes_is_discarded_and_the_run_continues_from_messages()
    {
        var run = await world.NewRun("First question", []);
        await RunOnce(run, new ScriptedModel([new TextOutput("First answer")]));
        await world.Write(db => db.Database.ExecuteSqlAsync(
            $"UPDATE agent_sessions SET state = '{{\"stateBag\": 42}}'::json WHERE thread_id = {run.ThreadId}"));

        await world.AddUserMessage(run.ThreadId, "Second question");
        var model = new ScriptedModel([new TextOutput("Second answer")]);
        await RunOnce(run, model);

        model.Requests.Single().Select(message => message.Summary).Should().Equal("First question", "First answer", "Second question");
    }

    [Fact]
    public async Task A_pending_approval_in_a_discarded_session_expires_and_history_says_why()
    {
        var run = await world.NewRun("Email the report", [new ToolSpec("send_email", ToolSensitivity.Write, RequiresApproval: true)]);
        var tools = new ScriptedTools().Add("send_email");
        var model = new ScriptedModel([new ToolCallOutput("c1", "send_email")], [new TextOutput("It expired.")]);
        var request = (await RunOnce(run, model, tools)).OfType<ApprovalRequested>().Single();
        await world.Write(db => db.Database.ExecuteSqlAsync(
            $"UPDATE agent_sessions SET state = '{{\"stateBag\": 42}}'::json WHERE thread_id = {run.ThreadId}"));

        await RunOnce(run with { Responses = [new ToolApproval(request.RequestId, request.CallId, Approved: true)] }, model, tools);

        var expired = JsonSerializer.Serialize(new { error = Drover.Agents.Sessions.PostgresAgentSessionStore.ExpiredApproval });
        tools.Count("send_email").Should().Be(0);
        model.Requests[1].Should().Contain(new SeenMessage("tool", $"c1 = {expired}"));
        (await world.Read(db => db.ToolCalls.SingleAsync(call => call.RunId == run.RunId)))
            .Status.Should().Be(ToolCallStatus.Denied);
    }

    #endregion

    #region Private Methods

    private RunDatabase Database(IServiceProvider provider) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), world.SpaceId, world.UserId);

    /// <summary>A run whose bot has a critical tool, the context every team member below governs its calls in.</summary>
    private async Task<RunContext> TeamRun(IRunGovernor governor)
    {
        var execution = await world.NewRun("Team work", [new ToolSpec("delete_all", ToolSensitivity.Critical)]);
        var thread = await world.Read(db => db.Threads.SingleAsync(found => found.Id == execution.ThreadId));
        return new RunContext(execution, governor, Database(services), thread);
    }

    /// <summary>A team member built the way the runtime builds a bot, so its tool calls pass the gate, then wrapped.</summary>
    private static GovernedAgent Member(string name, RunContext run, Guid parentRunId, ScriptedModel model)
    {
        var agent = new ScriptedChatClient(model).AsGovernedAgent(
            run,
            new ChatClientAgentOptions { Id = name, Name = name, Description = $"The {name} agent." },
            NullLoggerFactory.Instance);
        return new GovernedAgent(agent, run.Governor, parentRunId);
    }

    private async Task<IReadOnlyList<AgentEvent>> RunOnce(RunExecution run, ScriptedModel model, ScriptedTools? tools = null)
    {
        await using var host = new AgentRuntimeHost(postgres, model);
        List<AgentEvent> events = [];

        await foreach (var agentEvent in host.Runtime.Execute(run, new ScriptedGovernor(tools ?? new ScriptedTools()), TestContext.Current.CancellationToken))
        {
            events.Add(agentEvent);
        }

        return events;
    }

    private Task<string> StoredVersion(RunExecution run) =>
        world.Read(db => db.AgentSessions.Where(session => session.ThreadId == run.ThreadId).Select(session => session.FrameworkVersion).SingleAsync());

    private static string PostgresAgentSessionStoreVersion() => Drover.Agents.Sessions.PostgresAgentSessionStore.FrameworkVersion;

    private static async Task Drain(IAsyncEnumerable<AgentEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    private static async Task<RequestInfoEvent> NextRequest(StreamingRun run)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(patience);

        await foreach (var workflowEvent in run.WatchStreamAsync(timeout.Token))
        {
            FailOnError(workflowEvent);

            if (workflowEvent is RequestInfoEvent request)
            {
                return request;
            }
        }

        throw new InvalidOperationException("The workflow stopped without asking for anything.");
    }

    private static async Task<CheckpointInfo> NextRequestCheckpoint(StreamingRun run)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(patience);

        await foreach (var workflowEvent in run.WatchStreamAsync(timeout.Token))
        {
            FailOnError(workflowEvent);

            if (workflowEvent is SuperStepCompletedEvent { CompletionInfo.HasPendingRequests: true } completed)
            {
                return completed.CompletionInfo.Checkpoint
                    ?? throw new InvalidOperationException("The pending request has no committed checkpoint.");
            }
        }

        throw new InvalidOperationException("The workflow stopped before checkpointing its pending request.");
    }

    private static async Task<object?> NextOutput(StreamingRun run)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(patience);

        await foreach (var workflowEvent in run.WatchStreamAsync(timeout.Token))
        {
            FailOnError(workflowEvent);

            if (workflowEvent is WorkflowOutputEvent output)
            {
                return output.Data;
            }
        }

        return null;
    }

    private static async Task RunUntilIdle(StreamingRun run)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(patience);

        await foreach (var workflowEvent in run.WatchStreamAsync(timeout.Token))
        {
            FailOnError(workflowEvent);
        }
    }

    private static void FailOnError(WorkflowEvent workflowEvent)
    {
        if (workflowEvent is WorkflowErrorEvent error)
        {
            throw new InvalidOperationException("The workflow failed.", error.Exception);
        }

        if (workflowEvent is ExecutorFailedEvent failed)
        {
            throw new InvalidOperationException($"Executor {failed.ExecutorId} failed: {failed.Data}");
        }
    }

    #endregion
}
