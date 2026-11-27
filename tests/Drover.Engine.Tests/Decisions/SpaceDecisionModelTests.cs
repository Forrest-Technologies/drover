namespace Drover.Engine.Tests.Decisions;

/// <summary>
/// The space's decision model reaches a run only when it is ready, is found again before every check, and is tested
/// with no tenant scope open. Real PostgreSQL, the real run engine, a scripted provider.
/// </summary>
public sealed class SpaceDecisionModelTests(PostgresServer postgres)
{
    #region Private Fields

    private const string AskFirst = "Always ask me before sending an email.";

    #endregion

    #region Handed To A Run

    [Theory]
    [InlineData(ConnectionStatus.Ready, true)]
    [InlineData(ConnectionStatus.Pending, false)]
    [InlineData(ConnectionStatus.Failed, false)]
    public async Task A_run_is_handed_the_spaces_decision_model_only_when_it_is_ready(ConnectionStatus status, bool asked)
    {
        var provider = new ScriptedDecisionProvider();
        provider.Model.Applies = 0.05;
        await using var world = await World(provider);
        await Connect(world, status);

        var runId = await world.Post(await world.NewThread(), "Send it");

        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed, RunStatus.Waiting);
        provider.Model.Requests.Should().HaveCount(asked ? 1 : 0);
        world.Tools.Count("email.send").Should().Be(asked ? 1 : 0, "a rule nobody could check goes to a person");
        run.Status.Should().Be(asked ? RunStatus.Completed : RunStatus.Waiting);
    }

    [Fact]
    public async Task A_space_with_no_decision_model_hands_a_run_none()
    {
        var provider = new ScriptedDecisionProvider();
        await using var world = await World(provider);

        var runId = await world.Post(await world.NewThread(), "Send it");

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed, RunStatus.Waiting)).Status.Should().Be(RunStatus.Waiting);
        provider.Model.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task After_a_disconnect_the_very_next_check_of_a_run_already_going_is_unsure()
    {
        var provider = new ScriptedDecisionProvider();
        var tools = new TestTools().Add("email.send", ToolSensitivity.Write);
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "email_send", """{"to":"a@example.org"}""")],
            [new ToolCallOutput("c2", "email_send", """{"to":"b@example.org"}""")],
            [new TextOutput("Done.")]);
        await using var world = await World(provider, tools, script);
        await Connect(world, ConnectionStatus.Ready);
        provider.Model.Answer = async (request, _) =>
        {
            // The owner disconnects while the run is between its two calls.
            await world.Read(async db =>
            {
                db.DecisionConnections.Remove(await db.DecisionConnections.SingleAsync(RunWorld.Token));
                return true;
            });
            return new DecisionAnswer(request.Questions.ToDictionary(question => question.Name, _ => new DecisionResult { Yes = 0.05 }), 10);
        };

        var runId = await world.Post(await world.NewThread(), "Send both");

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed, RunStatus.Waiting)).Status.Should().Be(RunStatus.Waiting);
        provider.Model.Requests.Should().ContainSingle("the second check finds no decision model");
        tools.Count("email.send").Should().Be(1, "the second call was unsure, and waits for a person");
        var checks = (await world.ReadEvents(runId)).Where(stored => stored.Type == RunEventTypes.RuleChecked).Select(RunWorld.Data<RuleChecked>);
        checks.Select(check => check.Outcome).Should().Equal("passes", "unsure");
    }

    #endregion

    #region Testing

    [Fact]
    public async Task No_tenant_scope_is_open_during_a_test()
    {
        var provider = new ScriptedDecisionProvider();
        await using var world = await World(provider);
        await Connect(world, ConnectionStatus.Pending);
        var host = world.Hosts[^1];

        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, world.UserId);
        var tested = await scope.ServiceProvider.GetRequiredService<DecisionConnectionService>().Test(RunWorld.Token);

        tested!.Result.Succeeded.Should().BeTrue();
        tested.Connection.Status.Should().Be(ConnectionStatus.Ready);
        provider.Tests.Should().ContainSingle().Which.ScopeOpen.Should().BeFalse();
    }

    #endregion

    #region Private Methods

    // A world whose one write tool is called once, under one judged rule that stops the run when it applies.
    private async Task<RunWorld> World(ScriptedDecisionProvider provider, TestTools? tools = null, ScriptedModel? script = null)
    {
        tools ??= new TestTools().Add("email.send", ToolSensitivity.Write);
        script ??= new ScriptedModel([new ToolCallOutput("c1", "email_send")], [new TextOutput("Done.")]);
        var world = await RunWorld.Create(postgres, script, tools);
        await world.Grant("email.send", GrantEffect.Allow);
        await world.Read(db =>
        {
            db.BotRules.Add(new BotRule
            {
                BotId = world.BotId,
                Name = "Ask before mail",
                Checkpoint = RuleCheckpoint.BeforeTool,
                Effect = RuleEffect.Stop,
                Conditions = [new RuleCondition { Kind = RuleConditionKind.Judged, Statement = AskFirst }],
            });
            return Task.FromResult(true);
        });
        world.StartHost(replaceServices: services => services.AddSingleton<IDecisionModelProvider>(provider));
        return world;
    }

    private static Task<bool> Connect(RunWorld world, ConnectionStatus status) =>
        world.Read(db =>
        {
            db.DecisionConnections.Add(new DecisionConnection
            {
                Kind = ScriptedDecisionProvider.ConnectionKind,
                Address = "https://judge.example.test/api/systemone",
                Model = "judge-1",
                Status = status,
            });
            return Task.FromResult(true);
        });

    #endregion
}
