namespace Drover.Client.Tests;

/// <summary>
/// The reducer against event logs recorded from the run engine: one run that used a tool and was restarted in the
/// middle of a model call, and one that waited for an approval and was then cancelled.
/// </summary>
public sealed class RunReducerTests
{
    #region Private Fields

    private const string Restart = "run-with-tool-and-restart.json";

    private const string Approval = "run-waiting-for-approval-then-cancelled.json";

    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    #endregion

    #region Order And Replay

    [Theory]
    [InlineData(Restart)]
    [InlineData(Approval)]
    public void Any_prefix_of_a_log_and_then_the_rest_ends_in_the_same_state_as_the_whole_log_at_once(string fixture)
    {
        var log = Load(fixture);
        var whole = Snapshot(RunReducer.Apply(Start(log), log));

        for (var split = 0; split <= log.Count; split++)
        {
            var resumed = RunReducer.Apply(RunReducer.Apply(Start(log), log.Take(split)), log.Skip(split));

            Snapshot(resumed).Should().Be(whole, $"a client that disconnected after event {split} and resumed must end where one that never left does");
        }
    }

    [Theory]
    [InlineData(Restart)]
    [InlineData(Approval)]
    public void An_event_delivered_twice_changes_nothing_the_second_time(string fixture)
    {
        var log = Load(fixture);
        var whole = Snapshot(RunReducer.Apply(Start(log), log));

        for (var split = 1; split <= log.Count; split++)
        {
            // The reconnect replays from the start: everything up to the split arrives a second time.
            var replayed = RunReducer.Apply(RunReducer.Apply(Start(log), log.Take(split)), log);

            Snapshot(replayed).Should().Be(whole);
        }
    }

    [Fact]
    public void Events_of_another_run_in_the_tree_move_the_cursor_and_change_nothing_else()
    {
        var log = Load(Restart);
        var view = RunReducer.Apply(Start(log), log);
        var foreign = log[^1] with { Seq = log[^1].Seq + 1, RunId = Guid.CreateVersion7(), Type = RunEventTypes.RunFailed };

        var after = RunReducer.Apply(view, foreign);

        after.Should().Be(view with { LastSeq = foreign.Seq });
    }

    #endregion

    #region What The View Shows

    [Fact]
    public void A_restarted_run_ends_with_its_stored_messages_and_none_of_the_discarded_output()
    {
        var log = Load(Restart);

        var view = RunReducer.Apply(Start(log), log);

        view.Status.Should().Be("completed");
        view.IsActive.Should().BeFalse();
        view.Attempt.Should().Be(1);
        view.LastSeq.Should().Be(log[^1].Seq);
        view.Usage.Should().Be(new UsageTotals(20, 10, 0, 80m));
        view.Messages.Should().HaveCount(2).And.OnlyContain(message => !message.IsStreaming && message.Role == "assistant");
        view.Messages[0].Parts.Select(part => part.Kind).Should().Equal("text", "tool_call");
        view.Messages[0].Parts[0].Text.Should().Be("Let me look.");
        view.Messages[1].Parts.Should().ContainSingle().Which.Text.Should().Be("It is 42.");
        view.Messages.SelectMany(message => message.Parts).Should().NotContain(part => part.Text != null && part.Text.Contains("Partial"));
        view.ToolCalls.Should().ContainSingle().Which.Should().Match<ToolCallView>(tool =>
            tool.ToolName == "test.lookup" && tool.State == "completed" && !tool.IsError && tool.Preview.Contains("42") && tool.Effect == "allow");
        view.ToolCalls[0].ToolCallId.Should().Be(view.Messages[0].Parts[1].ToolCallId!.Value, "the tool card and the message part name the same call");
    }

    [Fact]
    public void Partial_output_is_shown_while_it_streams_and_dropped_when_its_step_is_discarded()
    {
        var log = Load(Restart);
        var discard = log.FindIndex(stored => stored.Type == RunEventTypes.StepDiscarded);

        var before = RunReducer.Apply(Start(log), log.Take(discard));
        var after = RunReducer.Apply(before, log[discard]);

        before.Messages.Should().HaveCount(2);
        before.Messages[^1].Should().Match<MessageView>(message => message.IsStreaming && message.Parts.Single().Text == "Partial ");
        after.Messages.Should().ContainSingle("the cut-off step's message was never stored, so it is gone");
        after.Messages[0].IsStreaming.Should().BeFalse();
    }

    [Fact]
    public void Text_grows_as_deltas_arrive_and_the_stored_message_replaces_it()
    {
        var log = Load(Restart);
        var firstDelta = log.FindIndex(stored => stored.Type == RunEventTypes.MessageDelta);
        var completed = log.FindIndex(stored => stored.Type == RunEventTypes.MessageCompleted);

        var streaming = RunReducer.Apply(Start(log), log.Take(firstDelta + 1));
        var stored = RunReducer.Apply(streaming, log.Skip(firstDelta + 1).Take(completed - firstDelta));

        streaming.Status.Should().Be("running");
        streaming.Messages.Should().ContainSingle().Which.Should().Match<MessageView>(message =>
            message.IsStreaming && message.Step == 1 && message.Parts.Single().Kind == "text" && message.Parts.Single().Text == "Let me look.");
        stored.Messages.Should().ContainSingle().Which.Should().Match<MessageView>(message =>
            !message.IsStreaming && message.Step == 1 && message.Id == streaming.Messages[0].Id);
    }

    [Fact]
    public void A_tool_that_needs_approval_shows_as_waiting_and_the_run_as_waiting_then_cancelled()
    {
        var log = Load(Approval);
        var waiting = log.FindIndex(stored => stored.Type == RunEventTypes.RunWaiting);

        var paused = RunReducer.Apply(Start(log), log.Take(waiting + 1));
        var ended = RunReducer.Apply(paused, log.Skip(waiting + 1));

        paused.Status.Should().Be("waiting");
        paused.Reason.Should().Be("awaiting_approval");
        paused.IsActive.Should().BeTrue();
        paused.ToolCalls.Should().ContainSingle().Which.Should().Match<ToolCallView>(tool =>
            tool.State == "awaiting_approval" && tool.Effect == "ask" && tool.RequestId != null && tool.ToolName == "test.write");
        ended.Status.Should().Be("cancelled");
        ended.Reason.Should().Be("cancelled_by_user");
        ended.IsActive.Should().BeFalse();
    }

    [Fact]
    public void An_event_type_this_client_does_not_know_is_skipped()
    {
        var log = Load(Approval);
        var view = RunReducer.Apply(Start(log), log);
        var unknown = log[^1] with { Seq = log[^1].Seq + 1, Type = "something.new", Data = "{}" };

        RunReducer.Apply(view, unknown).Should().Be(view with { LastSeq = unknown.Seq });
    }

    #endregion

    #region Timeline

    [Fact]
    public void The_timeline_lists_stored_messages_then_what_the_run_has_not_stored_yet_each_once()
    {
        var log = Load(Restart);
        var discard = log.FindIndex(stored => stored.Type == RunEventTypes.StepDiscarded);
        var view = RunReducer.Apply(Start(log), log.Take(discard));
        var userMessage = new MessageResponse(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, "user", null, [new MessagePartResponse("text") { Text = "What is the answer?" }], DateTimeOffset.UnixEpoch);
        var storedReply = RunEventTypes.Read<MessageCompleted>(log.First(stored => stored.Type == RunEventTypes.MessageCompleted)).Message;

        var timeline = ChatTimeline.Merge([storedReply, userMessage], view);

        timeline.Select(message => message.Role).Should().Equal("user", "assistant", "assistant");
        timeline.Select(message => message.Id).Should().OnlyHaveUniqueItems("a message the run completed and the thread stored appears once");
        timeline[^1].IsStreaming.Should().BeTrue();
        ChatTimeline.Merge([userMessage], null).Should().ContainSingle();
    }

    #endregion

    #region Private Methods

    private static List<RunEventResponse> Load(string fixture) =>
        JsonSerializer.Deserialize<List<RunEventResponse>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)), json)!;

    private static RunView Start(List<RunEventResponse> log) => RunView.Start(log[0].RunId);

    /// <summary>The whole view as text, so two views are compared by everything in them.</summary>
    private static string Snapshot(RunView view) => JsonSerializer.Serialize(view, json);

    #endregion
}
