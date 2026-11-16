namespace Drover.Engine.Tests.Sandboxes;

/// <summary>
/// A bot asks a person to take over its screen, over PostgreSQL and the real run engine, against a provider in memory:
/// the run waits like an approval and holds the screen, a restart keeps the wait, each answer resumes it once with a
/// result the bot reads, no bot that shares the screen acts on it meanwhile, and nothing the person types on the screen
/// reaches a run event, a tool result, the model, or the audit log.
/// </summary>
public sealed class ScreenTakeoverTests(PostgresServer postgres)
{
    #region Private Fields

    private const string Asking = """{"reason":"Sign in to the mail site with your passkey."}""";

    #endregion

    #region The Wait

    [Fact]
    public async Task Asking_for_a_person_puts_the_run_in_waiting_with_the_screen_held_and_the_reason_on_the_call()
    {
        var script = new ScriptedModel([new ToolCallOutput("c1", "computer_handover", Asking)], [new TextOutput("Never said.")]);
        await using var world = await TakeoverWorld(ComputerToolsTests.DesktopProvider(), script);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Read my mail");
        var run = await world.WaitForStatus(runId, RunStatus.Waiting, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Waiting);
        run.StatusReason.Should().Be(RunReasons.AwaitingTakeover);
        var call = (await world.ReadToolCalls(runId)).Single();
        call.Status.Should().Be(ToolCallStatus.PendingApproval, "the grant allows it, and the call waits for a person all the same");
        call.Arguments.GetProperty("reason").GetString().Should().Be("Sign in to the mail site with your passkey.");
        (await world.ReadEvents(runId)).Should().ContainSingle(stored => stored.Type == RunEventTypes.ToolApprovalRequested);
        script.Requests.Should().HaveCount(1, "nothing goes on until the person answers");
    }

    [Fact]
    public async Task Done_resumes_the_run_once_with_a_result_that_says_so_and_the_screen_is_free_again()
    {
        var script = new ScriptedModel([new ToolCallOutput("c1", "computer_handover", Asking)], [new TextOutput("Signed in.")]);
        await using var world = await TakeoverWorld(ComputerToolsTests.DesktopProvider(), script);
        world.StartHost();
        var runId = await world.Post(await world.NewThread(), "Read my mail");
        await world.WaitForStatus(runId, RunStatus.Waiting, RunStatus.Completed, RunStatus.Failed);
        var call = (await world.ReadToolCalls(runId)).Single();

        (await HandBack(world, call.Id, done: true)).Outcome.Should().Be(DecisionOutcome.Recorded);
        (await world.ReadRun(runId)).StatusReason.Should().Be(RunReasons.AwaitingApproval, "the screen is free once the answer is stored");
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed);
        script.Requests.Should().HaveCount(2);
        var result = ToolResult(script.Requests[1], "c1");
        result.GetProperty("done").GetBoolean().Should().BeTrue();
        result.GetProperty("note").GetString().Should().Contain("Take a screenshot");
        (await world.ReadToolCalls(runId)).Single().Status.Should().Be(ToolCallStatus.Completed);
        (await HandBack(world, call.Id, done: false)).Outcome.Should().Be(DecisionOutcome.NotWaiting, "an answer is given once");
    }

    [Fact]
    public async Task Could_not_resumes_the_run_with_the_persons_line_as_the_result()
    {
        var script = new ScriptedModel([new ToolCallOutput("c1", "computer_handover", Asking)], [new TextOutput("Understood.")]);
        await using var world = await TakeoverWorld(ComputerToolsTests.DesktopProvider(), script);
        world.StartHost();
        var runId = await world.Post(await world.NewThread(), "Read my mail");
        await world.WaitForStatus(runId, RunStatus.Waiting, RunStatus.Completed, RunStatus.Failed);
        var call = (await world.ReadToolCalls(runId)).Single();

        (await HandBack(world, call.Id, done: false, note: "  The site wants a phone I do not have.  ")).Outcome.Should().Be(DecisionOutcome.Recorded);
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed);
        var result = ToolResult(script.Requests[1], "c1");
        result.GetProperty("done").GetBoolean().Should().BeFalse();
        result.GetProperty("note").GetString().Should().Be("The person could not do it.");
        result.GetProperty("said").GetString().Should().Be("The site wants a phone I do not have.");
        (await world.ReadToolCalls(runId)).Single().Status.Should().Be(ToolCallStatus.Denied);
    }

    [Fact]
    public async Task A_restart_while_waiting_keeps_the_wait_and_the_answer_is_not_asked_again()
    {
        var script = new ScriptedModel([new ToolCallOutput("c1", "computer_handover", Asking)], [new TextOutput("Signed in.")]);
        await using var world = await TakeoverWorld(ComputerToolsTests.DesktopProvider(), script);
        var first = world.StartHost();
        var runId = await world.Post(await world.NewThread(), "Read my mail");
        await world.WaitForStatus(runId, RunStatus.Waiting, RunStatus.Completed, RunStatus.Failed);
        await first.Kill();

        var kept = await world.ReadRun(runId);
        kept.Status.Should().Be(RunStatus.Waiting);
        kept.StatusReason.Should().Be(RunReasons.AwaitingTakeover);
        var call = (await world.ReadToolCalls(runId)).Single();
        call.Status.Should().Be(ToolCallStatus.PendingApproval);

        (await HandBack(world, call.Id, done: true)).Outcome.Should().Be(DecisionOutcome.Recorded);
        world.StartHost();
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed);
        script.Requests.Should().HaveCount(2, "the run resumes once");
        var events = await world.ReadEvents(runId);
        events.Count(stored => stored.Type == RunEventTypes.ToolApprovalRequested).Should().Be(1, "nobody is asked a second time");
        events.Count(stored => stored.Type == RunEventTypes.ToolApproved).Should().Be(1);
        events.Where(stored => stored.Type == RunEventTypes.ToolCompleted).Select(RunWorld.Data<ToolCompleted>)
            .Should().ContainSingle().Which.Outcome.Should().Be("completed");
    }

    #endregion

    #region The Hold

    [Fact]
    public async Task While_a_person_has_the_screen_another_members_run_of_the_bot_is_refused_and_after_it_is_handed_back_it_is_not()
    {
        var provider = ComputerToolsTests.DesktopProvider();
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "computer_handover", Asking)],
            [new ToolCallOutput("c2", "computer_click", """{"x":10,"y":20}""")],
            [new TextOutput("I waited.")],
            [new TextOutput("Signed in.")],
            [new ToolCallOutput("c3", "computer_click", """{"x":10,"y":20}""")],
            [new TextOutput("Clicked.")]);
        await using var world = await TakeoverWorld(provider, script);
        var member = await world.AddMember(MembershipRole.Member);
        world.StartHost();
        var asking = await world.Post(await world.NewThread(), "Read my mail");
        await world.WaitForStatus(asking, RunStatus.Waiting, RunStatus.Completed, RunStatus.Failed);
        var memberThread = await NewThreadOf(world, member);
        provider.Commands.Clear();

        // The other member's run cannot see the asking run's private call: the hold is read from the shared run rows.
        var during = await PostAs(world, memberThread, member);
        (await world.WaitForStatus(during, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        var refused = await CallOf(world, member, during);
        refused.IsError.Should().BeTrue();
        refused.Result!.Value.GetRawText().Should().Contain("A person has this bot's screen right now");
        provider.Commands.Should().NotContain(command => command.Program == "xdotool", "nothing was done on the screen");

        var call = (await world.ReadToolCalls(asking)).Single();
        (await HandBack(world, call.Id, done: true)).Outcome.Should().Be(DecisionOutcome.Recorded);
        await world.WaitForStatus(asking, RunStatus.Completed, RunStatus.Failed);
        var after = await PostAs(world, memberThread, member);
        (await world.WaitForStatus(after, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        var clicked = await CallOf(world, member, after);
        clicked.IsError.Should().BeFalse();
        provider.Commands.Should().Contain(command => command.Program == "xdotool");
    }

    [Fact]
    public async Task A_person_drives_the_screen_while_holding_it_and_nothing_they_type_is_written_anywhere()
    {
        var provider = ComputerToolsTests.DesktopProvider();
        var script = new ScriptedModel([new ToolCallOutput("c1", "computer_handover", Asking)], [new TextOutput("Signed in.")]);
        await using var world = await TakeoverWorld(provider, script);
        var host = world.StartHost();
        var runId = await world.Post(await world.NewThread(), "Read my mail");
        await world.WaitForStatus(runId, RunStatus.Waiting, RunStatus.Completed, RunStatus.Failed);
        var call = (await world.ReadToolCalls(runId)).Single();
        var typed = $"typed-{Guid.NewGuid():N}";
        var control = host.Services.GetRequiredService<SandboxControlService>();

        (await world.WithService<ApprovalService, ToolDecision>(approvals => approvals.TakeOver(call.Id, world.UserId, isSpaceAdmin: true, RunWorld.Token)))
            .Outcome.Should().Be(DecisionOutcome.Recorded);
        (await control.Screen(world.SpaceId, world.UserId, world.BotId, RunWorld.Token)).Refusal.Should().Be(SandboxControlRefusal.None);
        (await control.Input(world.SpaceId, world.UserId, world.BotId, new SandboxInputRequest("type", Text: typed), RunWorld.Token))
            .Refusal.Should().Be(SandboxControlRefusal.None, "the hold stops the bots, not the person");
        (await HandBack(world, call.Id, done: true)).Outcome.Should().Be(DecisionOutcome.Recorded);
        await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        provider.Commands.Should().Contain(command => command.Arguments.Contains(typed), "the text reached the screen");
        (await world.ReadEvents(runId)).Should().OnlyContain(stored => !stored.Data.Contains(typed));
        (await world.ReadToolCalls(runId)).Should().OnlyContain(row => !row.Arguments.GetRawText().Contains(typed) && !(row.Result ?? default).ToString().Contains(typed));
        script.Requests.SelectMany(request => request).Should().OnlyContain(seen => !seen.Summary.Contains(typed));
        var entries = await world.Read(db => db.AuditLog.AsNoTracking().Where(entry => entry.Action.StartsWith("sandbox.")).OrderBy(entry => entry.Id).ToListAsync(RunWorld.Token));
        entries.Select(entry => entry.Action).Should().Equal("sandbox.taken_over", "sandbox.input", "sandbox.handed_back");
        entries.Should().OnlyContain(entry => entry.ActorId == world.UserId && entry.TargetId == world.BotId && !entry.Data.GetRawText().Contains(typed));
        entries[^1].Data.GetProperty("outcome").GetString().Should().Be("done");
    }

    #endregion

    #region Who Answers

    [Fact]
    public async Task Only_the_runs_person_or_an_admin_who_sees_the_call_answers_and_only_owners_and_admins_take_over()
    {
        var script = new ScriptedModel([new ToolCallOutput("c1", "computer_handover", Asking)], [new TextOutput("Signed in.")]);
        await using var world = await TakeoverWorld(ComputerToolsTests.DesktopProvider(), script);
        var member = await world.AddMember(MembershipRole.Member);
        var admin = await world.AddMember(MembershipRole.Admin);
        world.StartHost();
        var shared = await world.NewSharedThread();
        var runId = await world.Post(shared, "Read my mail");
        await world.WaitForStatus(runId, RunStatus.Waiting, RunStatus.Completed, RunStatus.Failed);
        var call = (await world.ReadToolCalls(runId)).Single();

        (await HandBack(world, call.Id, done: true, asUser: member)).Outcome.Should().Be(DecisionOutcome.Forbidden, "the member sees the call and does not act for the run");
        (await world.WithServiceAs<ApprovalService, ToolDecision>(member, approvals => approvals.TakeOver(call.Id, member, isSpaceAdmin: false, RunWorld.Token)))
            .Outcome.Should().Be(DecisionOutcome.Forbidden);
        (await world.Decide(call.Id, approved: true)).Outcome.Should().Be(DecisionOutcome.WrongKind, "a request for a person is not approved, it is handed back");
        (await world.ReadToolCalls(runId)).Single().Status.Should().Be(ToolCallStatus.PendingApproval);

        (await HandBack(world, call.Id, done: true, asUser: admin, isSpaceAdmin: true)).Outcome.Should().Be(DecisionOutcome.Recorded);
        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
    }

    [Fact]
    public async Task A_private_conversations_request_is_not_found_by_an_admin_who_cannot_see_it()
    {
        var script = new ScriptedModel([new ToolCallOutput("c1", "computer_handover", Asking)], [new TextOutput("Never said.")]);
        await using var world = await TakeoverWorld(ComputerToolsTests.DesktopProvider(), script);
        var admin = await world.AddMember(MembershipRole.Admin);
        world.StartHost();
        var runId = await world.Post(await world.NewThread(), "Read my mail");
        await world.WaitForStatus(runId, RunStatus.Waiting, RunStatus.Completed, RunStatus.Failed);
        var call = (await world.ReadToolCalls(runId)).Single();

        (await HandBack(world, call.Id, done: true, asUser: admin, isSpaceAdmin: true)).Outcome.Should().Be(DecisionOutcome.NotFound);
        (await world.WithServiceAs<ApprovalService, ToolDecision>(admin, approvals => approvals.TakeOver(call.Id, admin, isSpaceAdmin: true, RunWorld.Token)))
            .Outcome.Should().Be(DecisionOutcome.NotFound);
        (await world.ReadRun(runId)).Status.Should().Be(RunStatus.Waiting);
    }

    [Fact]
    public async Task An_approval_is_not_handed_back()
    {
        var script = new ScriptedModel([new ToolCallOutput("c1", "computer_click", """{"x":10,"y":20}""")], [new TextOutput("Never said.")]);
        await using var world = await TakeoverWorld(ComputerToolsTests.DesktopProvider(), script);
        await world.Revoke("computer.click");
        await world.Grant("computer.click", GrantEffect.Ask);
        world.StartHost();
        var runId = await world.Post(await world.NewThread(), "Click");
        var run = await world.WaitForStatus(runId, RunStatus.Waiting, RunStatus.Completed, RunStatus.Failed);
        var call = (await world.ReadToolCalls(runId)).Single();

        run.StatusReason.Should().Be(RunReasons.AwaitingApproval, "an approval does not hold the screen");
        (await HandBack(world, call.Id, done: true)).Outcome.Should().Be(DecisionOutcome.WrongKind);
        (await world.WithService<ApprovalService, ToolDecision>(approvals => approvals.TakeOver(call.Id, world.UserId, isSpaceAdmin: true, RunWorld.Token)))
            .Outcome.Should().Be(DecisionOutcome.WrongKind);
    }

    #endregion

    #region Helpers

    private async Task<RunWorld> TakeoverWorld(FakeSandboxProvider provider, ScriptedModel script)
    {
        var world = await RunWorld.Create(
            postgres,
            script,
            everyHost: services =>
            {
                services.AddSingleton<ISandboxProvider>(provider);
                services.AddSandboxTools();
            });
        await world.Read(async db =>
        {
            var bot = await db.Bots.SingleAsync(found => found.Id == world.BotId, RunWorld.Token);
            bot.SandboxPolicy = SandboxPolicy.Private;
            bot.SandboxSettings = new SandboxSettings { Desktop = true };
            var model = await db.ModelCatalog.SingleAsync(RunWorld.Token);
            model.Capabilities = new ModelCapabilities { Vision = true };
            return true;
        });

        foreach (var tool in ComputerTools.Names)
        {
            await world.Grant(tool, GrantEffect.Allow);
        }

        return world;
    }

    private static Task<ToolDecision> HandBack(RunWorld world, Guid toolCallId, bool done, string? note = null, Guid? asUser = null, bool isSpaceAdmin = false) =>
        world.WithServiceAs<ApprovalService, ToolDecision>(
            asUser ?? world.UserId,
            approvals => approvals.HandBack(toolCallId, done, note, asUser ?? world.UserId, isSpaceAdmin, RunWorld.Token));

    private static async Task<Guid> NewThreadOf(RunWorld world, Guid userId) =>
        (await world.TransactAs(world.SpaceId, userId, services => services.GetRequiredService<ThreadService>().Create(world.BotId, null, userId, RunWorld.Token)))!.Id;

    private static async Task<Guid> PostAs(RunWorld world, Guid threadId, Guid userId)
    {
        var post = await world.PostRaw(threadId, "Click the button", asUser: userId);
        post.Problem.Should().Be(PostProblem.None, post.Detail);
        return post.Run!.Id;
    }

    /// <summary>The one tool call of a run in a member's private conversation, read as that member.</summary>
    private static Task<ToolCall> CallOf(RunWorld world, Guid userId, Guid runId) =>
        world.TransactAs(world.SpaceId, userId, services =>
            services.GetRequiredService<DroverDbContext>().ToolCalls.AsNoTracking().SingleAsync(call => call.RunId == runId, RunWorld.Token));

    /// <summary>The result the model was given for a call, read from what it saw.</summary>
    private static JsonElement ToolResult(IReadOnlyList<SeenMessage> seen, string callId) =>
        JsonElement.Parse(seen.Single(message => message.Role == "tool" && message.Summary.StartsWith($"{callId} = ", StringComparison.Ordinal)).Summary[$"{callId} = ".Length..]);

    #endregion
}
