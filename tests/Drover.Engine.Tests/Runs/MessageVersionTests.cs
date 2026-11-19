namespace Drover.Engine.Tests.Runs;

/// <summary>
/// Versions of a conversation: a person asks for another reply or edits their message, and switches between the
/// versions that made. Each is tested against a real database and the real agent runtime, and what the model was
/// given is read back from the scripted model.
/// </summary>
public sealed class MessageVersionTests(PostgresServer postgres)
{
    #region Regenerate

    [Fact]
    public async Task Another_reply_answers_the_same_message_from_its_history_and_the_earlier_reply_stays_reachable()
    {
        var script = new ScriptedModel([new TextOutput("Paris.")], [new TextOutput("Paris, on the Seine.")]);
        await using var world = await RunWorld.Create(postgres, script);
        world.StartHost();
        var threadId = await world.NewThread();
        var firstRun = await world.Post(threadId, "Capital of France?");
        await world.WaitForStatus(firstRun, RunStatus.Completed);
        var firstReply = (await Shown(world, threadId)).Single(message => message.Role is MessageRole.Assistant);

        var change = await RegenerateAs(world, threadId, firstReply.Id, world.UserId);
        change.Outcome.Should().Be(VersionOutcome.Queued);
        await world.WaitForStatus(change.Run!.Id, RunStatus.Completed);

        RunWorld.Summaries(await Shown(world, threadId)).Should().Equal("user: Capital of France?", "assistant: Paris, on the Seine.");
        script.Requests[1].Select(seen => seen.Summary).Should().Equal(
            ["Capital of France?"],
            "the new reply is written from the history before the earlier one, never from it");
        change.Run.TriggerMessageId.Should().Be(change.Message!.Id);
        var secondReply = (await Shown(world, threadId)).Single(message => message.Role is MessageRole.Assistant);
        var versions = await Versions(world, threadId);
        versions[firstReply.Id].Should().Equal(firstReply.Id, secondReply.Id);
        versions[secondReply.Id].Should().Equal(firstReply.Id, secondReply.Id);
        (await world.ReadMessages(threadId)).Single(message => message.Id == firstReply.Id).ShelvedAt.Should().NotBeNull("the earlier reply is kept, not deleted");
    }

    [Fact]
    public async Task The_run_of_an_earlier_version_stays_in_the_threads_history_and_usage()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("One.")], [new TextOutput("Two.")]));
        world.StartHost();
        var threadId = await world.NewThread();
        var firstRun = await world.Post(threadId, "Count");
        await world.WaitForStatus(firstRun, RunStatus.Completed);
        var reply = (await Shown(world, threadId)).Single(message => message.Role is MessageRole.Assistant);

        var change = await RegenerateAs(world, threadId, reply.Id, world.UserId);
        await world.WaitForStatus(change.Run!.Id, RunStatus.Completed);

        (await world.ReadRuns(threadId)).Select(run => run.Id).Should().Equal(firstRun, change.Run.Id);
        (await world.ReadRun(firstRun)).Status.Should().Be(RunStatus.Completed);
        (await world.ReadUsage(firstRun)).Should().NotBeEmpty("what the earlier reply cost was spent and stays counted");
        (await world.ReadUsage(change.Run.Id)).Should().NotBeEmpty();
        (await world.ReadEvents(firstRun)).Should().Contain(stored => stored.Type == RunEventTypes.RunCompleted);
    }

    [Fact]
    public async Task Only_the_person_who_wrote_the_message_may_ask_again_and_only_while_nothing_works_on_the_thread()
    {
        var tools = new TestTools().AddGated("test.lookup", ToolSensitivity.Read, """{"answer":42}""");
        var script = new ScriptedModel([new TextOutput("Hi.")], [new ToolCallOutput("c1", "test_lookup")], [new TextOutput("42.")]);
        await using var world = await RunWorld.Create(postgres, script, tools);
        world.StartHost();
        var member = await world.AddMember(MembershipRole.Member);
        var threadId = await world.NewSharedThread();
        await world.WaitForStatus(await world.Post(threadId, "Hello"), RunStatus.Completed);
        var reply = (await Shown(world, threadId)).Single(message => message.Role is MessageRole.Assistant);
        var asked = (await Shown(world, threadId)).Single(message => message.Role is MessageRole.User);

        var byOther = await RegenerateAs(world, threadId, reply.Id, member);
        var editByOther = await world.WithServiceAs<RunService, VersionChange>(member, runs => runs.Edit(threadId, asked.Id, "Hi", member, RunWorld.Token));
        var working = await world.Post(threadId, "Look it up");
        await tools.Paused.WaitAsync(TimeSpan.FromSeconds(30), RunWorld.Token);
        var busy = await RegenerateAs(world, threadId, reply.Id, world.UserId);
        tools.OpenGate();
        await world.WaitForStatus(working, RunStatus.Completed);

        byOther.Outcome.Should().Be(VersionOutcome.Forbidden);
        editByOther.Outcome.Should().Be(VersionOutcome.Forbidden);
        busy.Outcome.Should().Be(VersionOutcome.Busy);
        (await world.ReadMessages(threadId)).Should().OnlyContain(message => message.ShelvedAt == null, "nothing was asked again");
    }

    #endregion

    #region Edit

    [Fact]
    public async Task Editing_the_last_message_answers_the_edit_from_the_history_before_it()
    {
        var script = new ScriptedModel([new TextOutput("Hello.")], [new TextOutput("Four.")], [new TextOutput("Six.")]);
        await using var world = await RunWorld.Create(postgres, script);
        world.StartHost();
        var threadId = await world.NewThread();
        await world.WaitForStatus(await world.Post(threadId, "Hi"), RunStatus.Completed);
        await world.WaitForStatus(await world.Post(threadId, "What is 2 + 2?"), RunStatus.Completed);
        var original = (await Shown(world, threadId)).Last(message => message.Role is MessageRole.User);

        var change = await world.WithService<RunService, VersionChange>(runs => runs.Edit(threadId, original.Id, "What is 3 + 3?", world.UserId, RunWorld.Token));
        change.Outcome.Should().Be(VersionOutcome.Queued);
        await world.WaitForStatus(change.Run!.Id, RunStatus.Completed);

        RunWorld.Summaries(await Shown(world, threadId)).Should().Equal("user: Hi", "assistant: Hello.", "user: What is 3 + 3?", "assistant: Six.");
        script.Requests[2].Select(seen => seen.Summary).Should().Equal(
            ["Hi", "Hello.", "What is 3 + 3?"],
            "the edit replaces the message it edits, and the reply to that message is not history any more");
        change.Message!.Position.Should().Be(original.Position);
        change.Message.ParentId.Should().Be(original.ParentId);
        (await Versions(world, threadId))[change.Message.Id].Should().Equal(original.Id, change.Message.Id);
    }

    [Fact]
    public async Task An_edit_keeps_the_files_sent_with_the_message_and_must_say_something()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("Done.")]));
        world.StartHost();
        var threadId = await world.NewThread();
        await world.WaitForStatus(await world.Post(threadId, "Hi"), RunStatus.Completed);
        var original = (await Shown(world, threadId)).Single(message => message.Role is MessageRole.User);

        var empty = await world.WithService<RunService, VersionChange>(runs => runs.Edit(threadId, original.Id, "  ", world.UserId, RunWorld.Token));
        var tooLong = await world.WithService<RunService, VersionChange>(runs => runs.Edit(threadId, original.Id, new string('a', RunService.MaxMessageLength + 1), world.UserId, RunWorld.Token));

        empty.Outcome.Should().Be(VersionOutcome.Invalid);
        tooLong.Outcome.Should().Be(VersionOutcome.Invalid);
    }

    #endregion

    #region Switching

    [Fact]
    public async Task Switching_versions_brings_back_what_followed_each_one_and_the_next_run_reads_the_version_shown()
    {
        var script = new ScriptedModel(
            [new TextOutput("Red.")],
            [new TextOutput("Blue.")],
            [new TextOutput("Sky.")],
            [new TextOutput("Clear.")]);
        await using var world = await RunWorld.Create(postgres, script);
        world.StartHost();
        var threadId = await world.NewThread();
        await world.WaitForStatus(await world.Post(threadId, "Pick a colour"), RunStatus.Completed);
        var red = (await Shown(world, threadId)).Single(message => message.Role is MessageRole.Assistant);
        await world.WaitForStatus((await RegenerateAs(world, threadId, red.Id, world.UserId)).Run!.Id, RunStatus.Completed);
        await world.WaitForStatus(await world.Post(threadId, "Why?"), RunStatus.Completed);
        var blue = (await Shown(world, threadId)).First(message => message.Role is MessageRole.Assistant);

        (await ShowAs(world, threadId, red.Id)).Outcome.Should().Be(VersionOutcome.Shown);
        var onRed = RunWorld.Summaries(await Shown(world, threadId));
        (await ShowAs(world, threadId, blue.Id)).Outcome.Should().Be(VersionOutcome.Shown);
        var onBlue = RunWorld.Summaries(await Shown(world, threadId));
        await world.WaitForStatus(await world.Post(threadId, "And now?"), RunStatus.Completed);

        onRed.Should().Equal("user: Pick a colour", "assistant: Red.");
        onBlue.Should().Equal("user: Pick a colour", "assistant: Blue.", "user: Why?", "assistant: Sky.");
        script.Requests[3].Select(seen => seen.Summary).Should().Equal(["Pick a colour", "Blue.", "Why?", "Sky.", "And now?"]);
    }

    [Fact]
    public async Task A_summary_that_covers_a_message_left_behind_is_dropped_so_it_is_written_again()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("One.")], [new TextOutput("Two.")]));
        world.StartHost(options => options.SummaryAfterMessages = 1_000);
        var threadId = await world.NewMainThread();
        await world.WaitForStatus(await world.Post(threadId, "Count"), RunStatus.Completed);
        var reply = (await Shown(world, threadId)).Single(message => message.Role is MessageRole.Assistant);
        await world.Read(db => db.Threads.Where(thread => thread.Id == threadId).ExecuteUpdateAsync(
            set => set.SetProperty(thread => thread.Summary, "They counted.").SetProperty(thread => thread.SummaryThroughPosition, 2),
            RunWorld.Token));

        await world.WaitForStatus((await RegenerateAs(world, threadId, reply.Id, world.UserId)).Run!.Id, RunStatus.Completed);

        var thread = await world.ReadThread(threadId);
        thread.Summary.Should().BeNull("the summary described a reply that is no longer shown");
        thread.SummaryThroughPosition.Should().BeNull();
    }

    #endregion

    #region Private Methods

    private static async Task<List<Message>> Shown(RunWorld world, Guid threadId) =>
        [.. (await world.ReadMessages(threadId)).Where(message => message is { ShelvedAt: null, Position: not null })];

    private static Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> Versions(RunWorld world, Guid threadId) =>
        world.Transact(services => services.GetRequiredService<ThreadService>().ListVersions(threadId, RunWorld.Token));

    private static Task<VersionChange> RegenerateAs(RunWorld world, Guid threadId, Guid replyId, Guid userId) =>
        world.WithServiceAs<RunService, VersionChange>(userId, runs => runs.Regenerate(threadId, replyId, userId, RunWorld.Token));

    private static Task<VersionChange> ShowAs(RunWorld world, Guid threadId, Guid messageId) =>
        world.WithService<RunService, VersionChange>(runs => runs.ShowVersion(threadId, messageId, world.UserId, RunWorld.Token));

    #endregion
}
