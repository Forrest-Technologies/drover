namespace Drover.Engine.Tests.Rules;

/// <summary>
/// Rules written as a sentence inside a real run, against a real database and the real agent runtime, with a
/// scripted decision model. The engine asks once per checkpoint, decides in code, and fails toward a person: until a
/// tool can pause for a judged rule, a call a person would be asked about is refused and says approval is needed.
/// </summary>
public sealed class JudgedChecksTests(PostgresServer postgres)
{
    #region Private Fields

    private const string AskFirst = "Always ask me before sending an email.";

    private const string VerifyFirst = "Always verify the invoice total matches the purchase order before paying it.";

    #endregion

    #region One Check

    [Fact]
    public async Task One_request_carries_every_rule_due()
    {
        var tools = new TestTools().Add("email.send", ToolSensitivity.Write);
        var script = new ScriptedModel([new ToolCallOutput("c1", "email_send", """{"to":"sam@example.org"}""")], [new TextOutput("Sent.")]);
        var judge = new ScriptedDecisionModel { Applies = 0.05 };
        await using var world = await RunWorld.Create(postgres, script, tools);
        await GrantEveryTool(world);
        var ask = await AddRule(world, "Ask before mail", RuleCheckpoint.BeforeTool, RuleEffect.Ask, Kind(ToolSensitivity.Write), Sentence(AskFirst));
        var stop = await AddRule(world, "Stop before mail", RuleCheckpoint.BeforeTool, RuleEffect.Stop, Sentence("Never send mail at night."));
        world.StartHost(replaceServices: judge.InEverySpace());

        var runId = await world.Post(await world.NewThread(), "Send it");

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        tools.Count("email.send").Should().Be(1, "both rules pass, so the call runs");
        var request = judge.Requests.Should().ContainSingle("every rule due at one checkpoint goes out in one request").Subject;
        request.Questions.Select(question => question.Name).Should().Equal("rule1.applies", "rule1.met", "rule2.applies", "rule2.met");
        request.Questions[0].Text.Should().Contain(AskFirst);
        request.State["action"].Should().Contain("email.send").And.Contain("write");
        var checks = Checked(await world.ReadEvents(runId));
        checks.Select(check => check.RuleId).Should().BeEquivalentTo([ask, stop]);
        checks.Should().OnlyContain(check => check.Outcome == "passes" && check.Checkpoint == "before_tool" && check.ToolCallId != null);
    }

    [Fact]
    public async Task A_call_is_decided_once()
    {
        var tools = new TestTools().Add("email.send", ToolSensitivity.Write);
        var script = new ScriptedModel([new ToolCallOutput("c1", "email_send")], [new TextOutput("Sent.")]);
        var judge = new ScriptedDecisionModel { Applies = 0.05 };
        await using var world = await RunWorld.Create(postgres, script, tools);
        await GrantEveryTool(world);
        await AddRule(world, "Ask before mail", RuleCheckpoint.BeforeTool, RuleEffect.Ask, Sentence(AskFirst));
        world.StartHost(replaceServices: judge.InEverySpace());

        var runId = await world.Post(await world.NewThread(), "Send it");

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        judge.Requests.Should().ContainSingle("the call is read where it is written and again at the gate, and asked about once");
    }

    [Fact]
    public async Task An_ask_rule_that_applies_does_not_let_the_call_run_without_a_person()
    {
        var tools = new TestTools().Add("email.send", ToolSensitivity.Write);
        var script = new ScriptedModel([new ToolCallOutput("c1", "email_send")], [new TextOutput("I need your approval.")]);
        var judge = new ScriptedDecisionModel { Applies = 0.92 };
        await using var world = await RunWorld.Create(postgres, script, tools);
        await GrantEveryTool(world);
        await AddRule(world, "Ask before mail", RuleCheckpoint.BeforeTool, RuleEffect.Ask, Sentence(AskFirst));
        world.StartHost(replaceServices: judge.InEverySpace());

        var runId = await world.Post(await world.NewThread(), "Send it");

        await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed, RunStatus.Waiting);
        tools.Count("email.send").Should().Be(0);
        var events = await world.ReadEvents(runId);
        Checked(events).Should().ContainSingle().Which.Probability.Should().Be(0.92);
        Fired(events).Should().ContainSingle().Which.Effect.Should().Be("ask");
    }

    #endregion

    #region Holding The Bot To The Order Of Work

    [Fact]
    public async Task A_block_rule_whose_requirement_was_done_lets_the_call_run()
    {
        var tools = new TestTools().Add("invoice.pay", ToolSensitivity.Critical);
        var script = new ScriptedModel([new ToolCallOutput("c1", "invoice_pay")], [new TextOutput("Paid.")]);
        var judge = new ScriptedDecisionModel { Applies = 0.97, Met = 0.91 };
        await using var world = await RunWorld.Create(postgres, script, tools);
        await GrantEveryTool(world);
        await AddRule(world, "Verify first", RuleCheckpoint.BeforeTool, RuleEffect.Block, Sentence(VerifyFirst));
        world.StartHost(replaceServices: judge.InEverySpace());

        var runId = await world.Post(await world.NewThread(), "Pay it");

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        tools.Count("invoice.pay").Should().Be(1);
        Fired(await world.ReadEvents(runId)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_block_rule_whose_requirement_was_not_done_refuses_and_tells_the_bot_the_sentence()
    {
        var tools = new TestTools().Add("invoice.pay", ToolSensitivity.Critical);
        var script = new ScriptedModel([new ToolCallOutput("c1", "invoice_pay")], [new TextOutput("I will check the order first.")]);
        var judge = new ScriptedDecisionModel { Applies = 0.97, Met = 0.10 };
        await using var world = await RunWorld.Create(postgres, script, tools);
        await GrantEveryTool(world);
        await AddRule(world, "Verify first", RuleCheckpoint.BeforeTool, RuleEffect.Block, Sentence(VerifyFirst));
        world.StartHost(replaceServices: judge.InEverySpace());

        var runId = await world.Post(await world.NewThread(), "Pay it");

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        tools.Count("invoice.pay").Should().Be(0);
        script.Requests[1][^1].Summary.Should().Contain(VerifyFirst, "the bot is told the rule so it can do the missing step");
        Fired(await world.ReadEvents(runId)).Should().ContainSingle().Which.Effect.Should().Be("block");
    }

    #endregion

    #region Fail Closed

    [Fact]
    public async Task A_timeout_is_unsure()
    {
        var judge = new ScriptedDecisionModel { Answer = async (_, cancellationToken) => { await Task.Delay(Timeout.Infinite, cancellationToken); return null!; } };
        await using var world = await AskedAbout(judge, policy => policy with { MaxJudgedRuleSeconds = 1 });

        await ExpectAPersonIsAsked(world, "failed");
    }

    [Fact]
    public async Task A_provider_failure_is_unsure()
    {
        var judge = new ScriptedDecisionModel { Answer = (_, _) => throw ModelProviderException.FromStatus(503) };
        await using var world = await AskedAbout(judge);

        await ExpectAPersonIsAsked(world, "failed");
    }

    [Fact]
    public async Task An_unreadable_answer_is_unsure()
    {
        var judge = new ScriptedDecisionModel
        {
            Answer = (_, _) => Task.FromResult(new DecisionAnswer(new Dictionary<string, DecisionResult> { ["rule1.applies"] = new() { Yes = 1.7 } }, 10)),
        };
        await using var world = await AskedAbout(judge);

        await ExpectAPersonIsAsked(world, "failed");
    }

    [Fact]
    public async Task Between_the_lines_is_unsure()
    {
        var judge = new ScriptedDecisionModel { Applies = 0.61, Met = 1.0 };
        await using var world = await AskedAbout(judge);

        var check = await ExpectAPersonIsAsked(world, "unsure");
        check!.Probability.Should().Be(0.61);
    }

    [Fact]
    public async Task Past_the_budget_is_unsure()
    {
        var tools = new TestTools().Add("invoice.pay", ToolSensitivity.Critical);
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "invoice_pay", """{"invoice":"A"}""")],
            [new ToolCallOutput("c2", "invoice_pay", """{"invoice":"B"}""")],
            [new TextOutput("Done.")]);
        var judge = new ScriptedDecisionModel { Applies = 0.03 };
        await using var world = await RunWorld.Create(postgres, script, tools);
        await GrantEveryTool(world);
        await world.SetRunLimits(new RunLimits { MaxJudgedChecks = 1 });
        await AddRule(world, "Verify first", RuleCheckpoint.BeforeTool, RuleEffect.Stop, Sentence(VerifyFirst));
        world.StartHost(replaceServices: judge.InEverySpace());

        var runId = await world.Post(await world.NewThread(), "Pay both");

        await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);
        judge.Requests.Should().ContainSingle("the bot's limit is one check a run");
        tools.Count("invoice.pay").Should().Be(1, "the second call is unsure, and goes to a person");
        Checked(await world.ReadEvents(runId)).Select(check => check.Outcome).Should().Equal("passes", "unsure");
        (await world.ReadRun(runId)).Usage.JudgedChecks.Should().Be(1);
    }

    [Fact]
    public async Task Before_a_reply_unsure_holds_the_reply_back()
    {
        var script = new ScriptedModel([new TextOutput("I promise it ships tomorrow.")]);
        var judge = new ScriptedDecisionModel { Applies = 0.5 };
        await using var world = await RunWorld.Create(postgres, script);
        await AddRule(world, "No promises", RuleCheckpoint.BeforeReply, RuleEffect.Block, Sentence("Never promise a delivery date."));
        world.StartHost(replaceServices: judge.InEverySpace());
        var threadId = await world.NewThread();

        var runId = await world.Post(threadId, "When does it ship?");

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        var summaries = RunWorld.Summaries(await world.ReadMessages(threadId));
        summaries[^1].Should().Contain("No promises").And.NotContain("tomorrow");
        (await world.ReadEvents(runId)).Should().OnlyContain(stored => !stored.Data.Contains("tomorrow"));
        judge.Requests.Should().ContainSingle().Which.State["action"].Should().Contain("tomorrow");
    }

    #endregion

    #region What Is Sent

    [Fact]
    public async Task Holds_no_tenant_scope_during_the_check()
    {
        var judge = new ScriptedDecisionModel { Applies = 0.05 };
        await using var world = await AskedAbout(judge);
        var runId = await world.Post(await world.NewThread(), "Send it");

        await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        judge.ScopeOpen.Should().ContainSingle().Which.Should().BeFalse();
    }

    [Fact]
    public async Task The_request_is_redacted_and_bounded()
    {
        var key = RunWorld.NewModelKey();
        var tools = new TestTools().Add("email.send", ToolSensitivity.Write, $$"""{"sent":true,"echo":"{{key}}","pad":"{{new string('x', 30_000)}}"}""");
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "email_send", $$"""{"body":"{{key}}"}""")],
            [new ToolCallOutput("c2", "email_send", """{"body":"again"}""")],
            [new TextOutput("Sent.")]);
        var judge = new ScriptedDecisionModel { Applies = 0.05 };
        await using var world = await RunWorld.Create(postgres, script, tools, modelKey: key);
        await GrantEveryTool(world);
        await AddRule(world, "Ask before mail", RuleCheckpoint.BeforeTool, RuleEffect.Ask, Sentence(AskFirst));
        var log = new CapturedLog();
        world.StartHost(replaceServices: services =>
        {
            judge.InEverySpace()(services);
            services.AddSingleton<ILoggerProvider>(log);
        });

        var runId = await world.Post(await world.NewThread(), $"Send this: {key} {new string('y', 20_000)}");

        await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);
        judge.Requests.Should().HaveCount(2);
        judge.Requests.SelectMany(request => request.State.Values.Concat(request.Questions.Select(question => question.Text)))
            .Should().OnlyContain(text => !text.Contains(key), "nothing the run's redactor knows leaves the server");
        var last = judge.Requests[^1].State;
        last["person"].Length.Should().BeLessThanOrEqualTo(JudgedChecks.MaxPersonCharacters);
        last["evidence"].Length.Should().BeLessThanOrEqualTo(JudgedChecks.MaxEvidenceCharacters);
        last["evidence"].Should().Contain("email.send");
        last["action"].Length.Should().BeLessThanOrEqualTo(JudgedChecks.MaxActionCharacters);
        (await world.ReadEvents(runId)).Where(stored => stored.Type == RunEventTypes.RuleChecked)
            .Should().OnlyContain(stored => !stored.Data.Contains(AskFirst) && !stored.Data.Contains("again"), "a check is written down without anything that was sent");
        log.Lines.Should().Contain(line => line.Contains("checked in run"))
            .And.OnlyContain(line => !line.Contains(AskFirst) && !line.Contains(key) && !line.Contains("again"), "no log line holds the checked text");
    }

    [Fact]
    public async Task Usage_is_recorded_and_no_step_is_counted()
    {
        var judge = new ScriptedDecisionModel { Applies = 0.05, InputTokens = 345 };
        await using var world = await AskedAbout(judge);
        var runId = await world.Post(await world.NewThread(), "Send it");

        await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        var usage = await world.ReadUsage(runId);
        var check = usage.Should().ContainSingle(record => record.Model == "scripted-judge").Subject;
        check.InputTokens.Should().Be(345);
        check.ModelConnectionId.Should().BeNull();
        check.Cost.Should().BeNull("no price is known for the decision model");
        check.RootRunId.Should().Be(runId);
        (await world.ReadEvents(runId)).Count(stored => stored.Type == RunEventTypes.StepStarted)
            .Should().Be(world.Script.Requests.Count, "a rule check is not a step");
    }

    #endregion

    #region What Is Never Asked

    [Fact]
    public async Task A_bot_with_no_judged_rule_makes_no_call()
    {
        var tools = new TestTools().Add("email.send", ToolSensitivity.Write);
        var script = new ScriptedModel([new ToolCallOutput("c1", "email_send")], [new TextOutput("Sent.")]);
        var judge = new ScriptedDecisionModel();
        await using var world = await RunWorld.Create(postgres, script, tools);
        await GrantEveryTool(world);
        await AddRule(world, "No lookups", RuleCheckpoint.BeforeTool, RuleEffect.Block, new RuleCondition { Kind = RuleConditionKind.ToolIs, Tools = ["test.lookup"] });
        world.StartHost(replaceServices: judge.InEverySpace());

        var runId = await world.Post(await world.NewThread(), "Send it");

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        judge.Requests.Should().BeEmpty();
        Checked(await world.ReadEvents(runId)).Should().BeEmpty();
    }

    [Fact]
    public async Task With_no_decision_model_a_judged_rule_makes_no_call_and_is_unsure()
    {
        var tools = new TestTools().Add("email.send", ToolSensitivity.Write);
        var script = new ScriptedModel([new ToolCallOutput("c1", "email_send")], [new TextOutput("I could not send it.")]);
        await using var world = await RunWorld.Create(postgres, script, tools);
        await GrantEveryTool(world);
        await AddRule(world, "Ask before mail", RuleCheckpoint.BeforeTool, RuleEffect.Ask, Sentence(AskFirst));
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Send it");

        await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);
        tools.Count("email.send").Should().Be(0);
        Checked(await world.ReadEvents(runId)).Should().ContainSingle().Which.Outcome.Should().Be("unsure");
        (await world.ReadUsage(runId)).Should().OnlyContain(record => record.ModelConnectionId == world.ConnectionId, "no check was made, so none is recorded");
    }

    [Fact]
    public async Task A_refused_call_is_never_checked()
    {
        var tools = new TestTools().Add("email.send", ToolSensitivity.Write).Add("test.lookup", ToolSensitivity.Read);
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "email_send")],
            [new ToolCallOutput("c2", "not_a_tool")],
            [new ToolCallOutput("c3", "test_lookup")],
            [new TextOutput("Nothing ran.")]);
        var judge = new ScriptedDecisionModel { Applies = 0.05 };
        await using var world = await RunWorld.Create(postgres, script, tools);
        await GrantEveryTool(world);
        await world.Revoke("test.lookup");
        await AddRule(world, "No mail", RuleCheckpoint.BeforeTool, RuleEffect.Block, new RuleCondition { Kind = RuleConditionKind.ToolIs, Tools = ["email.send"] });
        await AddRule(world, "Ask first", RuleCheckpoint.BeforeTool, RuleEffect.Ask, Sentence(AskFirst));
        world.StartHost(replaceServices: judge.InEverySpace());

        var runId = await world.Post(await world.NewThread(), "Try everything");

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        judge.Requests.Should().BeEmpty("a hard rule's block, a tool the bot does not have, and a grant that refuses are decided first");
    }

    [Fact]
    public async Task With_judged_rules_off_a_stored_one_is_never_sent()
    {
        var judge = new ScriptedDecisionModel { Applies = 0.05 };
        await using var world = await AskedAbout(judge, policy => policy with { AllowJudgedRules = false });

        await ExpectAPersonIsAsked(world, outcome: null);
        judge.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_child_bot_is_checked_against_its_ancestors_judged_rules_ancestors_first()
    {
        var tools = new TestTools().Add("email.send", ToolSensitivity.Write);
        var script = new ScriptedModel([new ToolCallOutput("c1", "email_send")], [new TextOutput("Sent.")]);
        var judge = new ScriptedDecisionModel { Applies = 0.05 };
        await using var world = await RunWorld.Create(postgres, script, tools);
        await GrantEveryTool(world);
        await AddRule(world, "Parent's rule", RuleCheckpoint.BeforeTool, RuleEffect.Ask, Sentence("The parent's sentence."));
        var childId = await world.NewBotBeneath(world.BotId, "Child");
        await world.GrantTo(childId, "email.send", GrantEffect.Allow);
        await AddRule(world, "Child's rule", RuleCheckpoint.BeforeTool, RuleEffect.Ask, childId, Sentence("The child's sentence."));
        world.StartHost(replaceServices: judge.InEverySpace());
        var threadId = (await world.Transact(services => services.GetRequiredService<ThreadService>().Create(childId, null, world.UserId, RunWorld.Token)))!.Id;

        var runId = await world.Post(threadId, "Send it");

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        var questions = judge.Requests.Should().ContainSingle().Subject.Questions;
        questions[0].Text.Should().Contain("The parent's sentence.");
        questions[2].Text.Should().Contain("The child's sentence.");
    }

    #endregion

    #region Private Methods

    // A world whose one write tool is called once, under one judged rule that asks.
    private async Task<RunWorld> AskedAbout(ScriptedDecisionModel judge, Func<DeploymentPolicy, DeploymentPolicy>? policy = null)
    {
        var tools = new TestTools().Add("email.send", ToolSensitivity.Write);
        var script = new ScriptedModel([new ToolCallOutput("c1", "email_send")], [new TextOutput("Done.")]);
        var world = await RunWorld.Create(postgres, script, tools);
        await GrantEveryTool(world);
        await AddRule(world, "Ask before mail", RuleCheckpoint.BeforeTool, RuleEffect.Stop, Sentence(AskFirst));
        world.StartHost(replaceServices: services =>
        {
            judge.InEverySpace()(services);

            if (policy is not null)
            {
                services.AddSingleton<IOptions<DeploymentPolicyOptions>>(Options.Create(new DeploymentPolicyOptions { Rules = policy(DeploymentPolicy.SelfHosted) }));
            }
        });
        return world;
    }

    // The rule stops the run when it applies; unsure, a person is asked instead, so the run goes on and the tool does not run.
    private static async Task<RuleChecked?> ExpectAPersonIsAsked(RunWorld world, string? outcome)
    {
        var runId = await world.Post(await world.NewThread(), "Send it");

        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed, RunStatus.Waiting);
        run.Error?.Code.Should().NotBe(RunRules.StoppedByRule, "a rule nobody is sure of never stops the run");
        world.Tools.Count("email.send").Should().Be(0, "nothing runs before a person says yes");
        var events = await world.ReadEvents(runId);
        Fired(events).Should().ContainSingle().Which.Effect.Should().Be("ask");
        var checks = Checked(events);

        if (outcome is null)
        {
            checks.Should().BeEmpty("no check was made");
            return null;
        }

        var check = checks.Should().ContainSingle().Subject;
        check.Outcome.Should().Be(outcome);
        return check;
    }

    // A new bot is granted only read tools; the tools a judged rule is about are granted as a person would.
    private static async Task GrantEveryTool(RunWorld world)
    {
        foreach (var tool in world.Tools.All.Where(tool => tool.Sensitivity is not ToolSensitivity.Read))
        {
            await world.Grant(tool.Name, GrantEffect.Allow);
        }
    }

    private static Task<Guid> AddRule(RunWorld world, string name, RuleCheckpoint checkpoint, RuleEffect effect, params RuleCondition[] conditions) =>
        AddRule(world, name, checkpoint, effect, world.BotId, conditions);

    private static Task<Guid> AddRule(RunWorld world, string name, RuleCheckpoint checkpoint, RuleEffect effect, Guid botId, params RuleCondition[] conditions) =>
        world.Read(async db =>
        {
            RuleValidator.FindProblem(name, checkpoint, effect, conditions).Should().BeNull("the test's rule is one a person could save");
            var position = await db.BotRules.CountAsync(rule => rule.BotId == botId, RunWorld.Token);
            var rule = new BotRule { BotId = botId, Name = name, Position = position, Checkpoint = checkpoint, Effect = effect, Conditions = [.. conditions] };
            db.BotRules.Add(rule);
            return rule.Id;
        });

    private static RuleCondition Sentence(string statement) => new() { Kind = RuleConditionKind.Judged, Statement = statement };

    private static RuleCondition Kind(ToolSensitivity kind) => new() { Kind = RuleConditionKind.ToolKind, ToolKinds = [kind] };

    private static List<RuleChecked> Checked(IReadOnlyList<RunEventResponse> events) =>
        [.. events.Where(stored => stored.Type == RunEventTypes.RuleChecked).Select(RunWorld.Data<RuleChecked>)];

    private static List<RuleFired> Fired(IReadOnlyList<RunEventResponse> events) =>
        [.. events.Where(stored => stored.Type == RunEventTypes.RuleFired).Select(RunWorld.Data<RuleFired>)];

    #endregion

    #region Captured Log

    /// <summary>Keeps every line the host logs, so a test can say what never appears in one.</summary>
    private sealed class CapturedLog : ILoggerProvider, ILogger
    {
        #region Private Fields

        private readonly ConcurrentQueue<string> lines = new();

        #endregion

        #region Properties

        public IReadOnlyList<string> Lines => [.. lines];

        #endregion

        #region Interface Implementations

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue($"{formatter(state, exception)} {exception}");

        public void Dispose()
        {
        }

        #endregion
    }

    #endregion
}
