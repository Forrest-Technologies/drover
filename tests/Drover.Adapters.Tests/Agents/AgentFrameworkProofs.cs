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
        string sessionId;
        await using (var first = postgres.NewServices().BuildServiceProvider())
        {
            var manager = CheckpointManager.CreateJson(new PostgresCheckpointStore(Database(first)));
            var started = await InProcessExecution.RunStreamingAsync(ReviewWorkflow.Build(), "draft", manager, cancellationToken: TestContext.Current.CancellationToken);
            sessionId = started.SessionId;
            var asked = await NextRequest(started);
            asked.Request.TryGetDataAs<ReviewRequest>(out var firstAsk).Should().BeTrue();
            firstAsk!.Draft.Should().Be("draft, written");

            // Killed while the request is pending: the response is never sent in this process.
        }

        await using var second = postgres.NewServices().BuildServiceProvider();
        var resumedManager = CheckpointManager.CreateJson(new PostgresCheckpointStore(Database(second)));
        var latest = await resumedManager.GetLatestCheckpointAsync(sessionId, TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("No checkpoint was stored for the first process's run.");
        await using var resumed = await InProcessExecution.ResumeStreamingAsync(ReviewWorkflow.Build(), latest, resumedManager, TestContext.Current.CancellationToken);

        var raisedAgain = await NextRequest(resumed);
        raisedAgain.Request.TryGetDataAs<ReviewRequest>(out var secondAsk).Should().BeTrue();
        secondAsk!.Draft.Should().Be("draft, written");

        await resumed.SendResponseAsync(raisedAgain.Request.CreateResponse(true));
        (await NextOutput(resumed)).Should().Be("published: draft, written");
    }

    #endregion

    #region Proof 5: Governed Agents Inside The Orchestration Builders

    [Theory]
    [InlineData("sequential")]
    [InlineData("concurrent")]
    [InlineData("handoff")]
    public async Task A_governed_agent_is_honored_inside_the_orchestration_builder(string pattern)
    {
        var governor = new ScriptedGovernor(new ScriptedTools());
        var parentRunId = Guid.CreateVersion7();
        var first = Governed("first", governor, parentRunId, pattern == "handoff"
            ? new ScriptedModel([new ToolCallOutput("h1", $"{HandoffWorkflowBuilder.FunctionPrefix}1")])
            : new ScriptedModel([new TextOutput("from first")]));
        var second = Governed("second", governor, parentRunId, new ScriptedModel([new TextOutput("from second")]));
        var workflow = pattern switch
        {
            "sequential" => AgentWorkflowBuilder.BuildSequential([first, second]),
            "concurrent" => AgentWorkflowBuilder.BuildConcurrent([first, second]),
            _ => AgentWorkflowBuilder.CreateHandoffBuilderWith(first).WithHandoffs(first, [second]).Build(),
        };

        await using var run = await InProcessExecution.RunStreamingAsync(
            workflow,
            new List<ChatMessage> { new(ChatRole.User, "Work on this") },
            cancellationToken: TestContext.Current.CancellationToken);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
        await RunUntilIdle(run);

        governor.Delegations.Select(delegation => delegation.AgentName).Should().BeEquivalentTo(["first", "second"]);
        governor.Delegations.Should().OnlyContain(delegation => delegation.ParentRunId == parentRunId);
        governor.Completions.Should().HaveCount(2).And.OnlyContain(completion => completion.Outcome == ChildRunOutcome.Completed);
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

    #endregion

    #region Private Methods

    private RunDatabase Database(IServiceProvider provider) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), world.SpaceId, world.UserId);

    private static GovernedAgent Governed(string name, IRunGovernor governor, Guid parentRunId, ScriptedModel model) =>
        new(new ScriptedChatClient(model).AsAIAgent(name: name, description: $"The {name} agent."), governor, parentRunId);

    private async Task RunOnce(RunExecution run, ScriptedModel model)
    {
        await using var host = new AgentRuntimeHost(postgres, model);
        await Drain(host.Runtime.Execute(run, new ScriptedGovernor(new ScriptedTools()), TestContext.Current.CancellationToken));
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
