namespace Drover.Engine.Tests.Runs;

/// <summary>
/// What a thread has for a person: a run of theirs waiting for them, their newest run failed, or a reply they have
/// not seen. Each comes from stored runs, messages, and the person's own read marker, against a real database.
/// </summary>
public sealed class RunAttentionTests(PostgresServer postgres)
{
    #region Private Fields

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    #endregion

    #region New

    [Fact]
    public async Task A_reply_the_person_has_not_seen_reads_new_until_they_open_the_thread_on_any_screen()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("Hello back.")]));
        world.StartHost();
        var threadId = await world.NewThread();
        using var signals = await world.WithService<UserSignalHub, UserSignalSubscription>(hub => Task.FromResult(hub.Subscribe(world.SpaceId, world.UserId)));

        await world.WaitForStatus(await world.Post(threadId, "Hello"), RunStatus.Completed);
        var unseen = await AttentionOf(world, threadId, world.UserId);
        (await world.WithService<ThreadService, bool>(threads => threads.MarkSeen(threadId, world.UserId, RunWorld.Token))).Should().BeTrue();
        var seen = await AttentionOf(world, threadId, world.UserId);

        unseen.Should().Be(new ThreadAttention(false, false, true, null));
        seen.Should().Be(new ThreadAttention(false, false, false, null));
        Drain(signals).Should().Contain(new UserSignal(UserSignalKinds.ThreadSeen, threadId, null), "every screen of the person hears it was seen");
    }

    [Fact]
    public async Task The_persons_own_messages_are_never_new_to_them()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("Done.")]));
        var threadId = await world.NewThread();

        await world.PostRaw(threadId, "Only my words so far");

        (await AttentionOf(world, threadId, world.UserId)).HasNew.Should().BeFalse();
    }

    #endregion

    #region Failed

    [Fact]
    public async Task A_failed_run_reads_failed_with_its_cause_until_seen_and_stays_the_last_failure_until_a_retry_succeeds()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("Second time lucky.")]));
        world.Model.FailNext = ModelProviderException.FromStatus(402);
        world.StartHost();
        var threadId = await world.NewThread();
        var failedId = await world.Post(threadId, "Hello");
        await world.WaitForStatus(failedId, RunStatus.Failed);

        var failed = await AttentionOf(world, threadId, world.UserId);
        await world.WithService<ThreadService, bool>(threads => threads.MarkSeen(threadId, world.UserId, RunWorld.Token));
        var seen = await AttentionOf(world, threadId, world.UserId);
        var retry = await world.WithService<RunService, RunRetry>(runs => runs.Retry(failedId, world.UserId, RunWorld.Token));
        await world.WaitForStatus(retry.Run!.Id, RunStatus.Completed);
        var after = await AttentionOf(world, threadId, world.UserId);

        failed.Failed.Should().BeTrue();
        failed.LastFailure.Should().Be(new RunFailure(
            failedId,
            FailureCauses.BudgetReached,
            "out_of_credit: The provider says the account is out of credit."));
        seen.Failed.Should().BeFalse("the person has opened the thread since");
        seen.LastFailure!.RunId.Should().Be(failedId, "the conversation still ends with the failure");
        retry.Outcome.Should().Be(RetryOutcome.Queued);
        retry.Run.TriggerMessageId.Should().Be((await world.ReadRuns(threadId))[0].TriggerMessageId, "a retry answers the same message");
        after.Failed.Should().BeFalse();
        after.LastFailure.Should().BeNull("a later run succeeded");
        after.HasNew.Should().BeTrue();
    }

    [Fact]
    public async Task Only_the_runs_own_person_may_retry_it_and_only_while_nothing_came_after_it()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("Fine now.")]));
        world.Model.FailNext = ModelProviderException.FromStatus(503);
        world.StartHost();
        var member = await world.AddMember(MembershipRole.Member);
        var threadId = await world.NewSharedThread();
        var failedId = await world.Post(threadId, "Hello");
        await world.WaitForStatus(failedId, RunStatus.Failed);

        var byOther = await RetryAs(world, failedId, member);
        var later = await world.Post(threadId, "Again");
        await world.WaitForStatus(later, RunStatus.Completed);
        var stale = await RetryAs(world, failedId, world.UserId);

        byOther.Outcome.Should().Be(RetryOutcome.Forbidden);
        stale.Outcome.Should().Be(RetryOutcome.NotRetryable, "a later run came after the failure");
    }

    [Theory]
    [InlineData("unreachable", FailureCauses.ModelUnreachable)]
    [InlineData("invalid_key", FailureCauses.ModelUnreachable)]
    [InlineData("model_unavailable", FailureCauses.ModelUnreachable)]
    [InlineData("rejected", FailureCauses.ModelRefused)]
    [InlineData("retry_later", FailureCauses.ModelRefused)]
    [InlineData("out_of_credit", FailureCauses.BudgetReached)]
    [InlineData("step_limit", FailureCauses.BudgetReached)]
    [InlineData("stopped_by_rule", FailureCauses.StoppedByRule)]
    [InlineData("blocked_by_rule", FailureCauses.StoppedByRule)]
    [InlineData("lease_lost", FailureCauses.ServerRestarted)]
    [InlineData("internal", FailureCauses.Unknown)]
    [InlineData(null, FailureCauses.Unknown)]
    public void A_failure_is_put_down_to_a_cause_a_person_can_act_on(string? code, string cause) =>
        Attentions.Cause(code).Should().Be(cause);

    #endregion

    #region Waiting

    [Fact]
    public async Task A_run_waiting_for_the_persons_approval_reads_waiting_until_they_answer_and_the_answer_is_signalled()
    {
        var tools = new TestTools().Add("test.write", ToolSensitivity.Write);
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new ToolCallOutput("c1", "test_write")], [new TextOutput("Written.")]), tools);
        await world.Grant("test.write", GrantEffect.Ask);
        world.StartHost();
        var threadId = await world.NewThread();
        var runId = await world.Post(threadId, "Write it");
        await world.WaitForStatus(runId, RunStatus.Waiting);
        using var signals = await world.WithService<UserSignalHub, UserSignalSubscription>(hub => Task.FromResult(hub.Subscribe(world.SpaceId, world.UserId)));

        var waiting = await AttentionOf(world, threadId, world.UserId);
        var call = (await world.ReadToolCalls(runId)).Single();
        (await world.Decide(call.Id, approved: true)).Outcome.Should().Be(DecisionOutcome.Recorded);
        var answered = await AttentionOf(world, threadId, world.UserId);

        waiting.WaitingForYou.Should().BeTrue();
        answered.WaitingForYou.Should().BeFalse("the call has its answer, even before a worker takes the run again");
        Drain(signals).Should().Contain(new UserSignal(UserSignalKinds.ThreadChanged, threadId, runId));
        await world.WaitForStatus(runId, RunStatus.Completed);
    }

    #endregion

    #region Two Members

    [Fact]
    public async Task One_members_runs_and_marker_never_show_in_another_members_attention()
    {
        var tools = new TestTools().Add("test.write", ToolSensitivity.Write);
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("For you.")], [new ToolCallOutput("c1", "test_write")]), tools);
        await world.Grant("test.write", GrantEffect.Ask);
        world.StartHost();
        var member = await world.AddMember(MembershipRole.Member);
        var threadId = await world.NewSharedThread();

        await world.WaitForStatus(await world.Post(threadId, "Hello"), RunStatus.Completed);
        var membersRun = (await world.PostRaw(threadId, "Write it", asUser: member)).Run!.Id;
        await world.WaitForStatus(membersRun, RunStatus.Waiting);
        await MarkSeenAs(world, threadId, member);

        var mine = await AttentionOf(world, threadId, world.UserId);
        var theirs = await AttentionOf(world, threadId, member);

        mine.Should().Be(new ThreadAttention(false, false, true, null), "the member's waiting run and marker are theirs alone");
        theirs.Should().Be(new ThreadAttention(true, false, false, null), "the reply to the owner is not new to the member");
    }

    #endregion

    #region Private Methods

    private static async Task<ThreadAttention> AttentionOf(RunWorld world, Guid threadId, Guid userId) =>
        (await world.TransactAs(world.SpaceId, userId, services =>
            services.GetRequiredService<ThreadService>().FindThread(threadId, userId, RunWorld.Token)))!.Attention!;

    private static Task<RunRetry> RetryAs(RunWorld world, Guid runId, Guid userId) =>
        world.WithServiceAs<RunService, RunRetry>(userId, runs => runs.Retry(runId, userId, RunWorld.Token));

    private static Task<bool> MarkSeenAs(RunWorld world, Guid threadId, Guid userId) =>
        world.WithServiceAs<ThreadService, bool>(userId, threads => threads.MarkSeen(threadId, userId, RunWorld.Token));

    private static List<UserSignal> Drain(UserSignalSubscription subscription)
    {
        List<UserSignal> read = [];
        while (subscription.Signals.TryRead(out var signal))
        {
            read.Add(signal);
        }

        return read;
    }

    #endregion
}
