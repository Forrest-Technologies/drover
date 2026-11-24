namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// One bot turn, one compact ledger of work: which messages go on a turn, what the ledger holds and how it groups,
/// what stays a card, and what a row is called.
/// </summary>
public sealed class TurnLedgerTests
{
    #region Private Fields

    private static readonly Guid run = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a1");

    private static readonly Guid otherRun = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a2");

    #endregion

    #region Turns

    [Fact]
    public void A_runs_messages_are_one_turn_with_the_ledger_on_the_first()
    {
        IReadOnlyList<ChatMessage> messages =
        [
            Person("1", "Check the prices"),
            Bot("2", run) with { Reasoning = "Look them up.", Tools = [Call("sandbox.run", "curl -s https://example.test")] },
            Bot("3", run) with { Tools = [Call("sandbox.run", "python3 parse.py")] },
            Bot("4", run, "Here are the prices."),
        ];

        var turns = messages.InTurns();

        turns.Select(message => message.ContinuesTurn).Should().Equal(false, false, true, true);
        turns[1].Ledger.Should().NotBeNull();
        turns[1].Ledger!.Select(TurnLedger.Label).Should().Equal("Reasoned", "Ran a command", "Ran a command");
        turns[3].Ledger.Should().BeNull("the ledger stands once, at the turn's start");
        ChatRows.Shape(turns[1], compact: false).Should().Be(ChatEntryShape.Bot, "the turn's first message carries the head even without words");
        ChatRows.Shape(turns[3], compact: false).Should().Be(ChatEntryShape.Continued);
    }

    [Fact]
    public void A_person_between_or_another_run_starts_a_new_turn()
    {
        IReadOnlyList<ChatMessage> messages =
        [
            Bot("1", run, "First."),
            Person("2", "And?"),
            Bot("3", run, "Same run, after a person."),
            Bot("4", otherRun, "Another run."),
        ];

        messages.InTurns().Select(message => message.ContinuesTurn).Should().Equal(false, false, false, false);
    }

    [Fact]
    public void A_reply_written_again_is_its_own_turn()
    {
        var versions = new VersionSwitch(2, 2, Guid.CreateVersion7(), null);
        IReadOnlyList<ChatMessage> messages =
        [
            Person("1", "Plan it"),
            Bot("2", otherRun, "The second try.") with { Versions = versions, Tools = [Call("sandbox.run", "ls")] },
        ];

        var turns = messages.InTurns();

        turns[1].ContinuesTurn.Should().BeFalse();
        turns[1].Ledger.Should().ContainSingle();
    }

    [Fact]
    public void A_turn_without_work_has_no_ledger_and_messages_without_a_run_are_left_as_they_are()
    {
        IReadOnlyList<ChatMessage> messages = [Bot("1", run, "Just words."), new ChatMessage("2", false, "Older words.")];

        var turns = messages.InTurns();

        turns.Should().OnlyContain(message => message.Ledger == null && !message.ContinuesTurn);
    }

    #endregion

    #region What Folds

    [Fact]
    public void Calls_that_need_a_person_hand_work_over_or_keep_a_note_stay_cards()
    {
        var waiting = Call("sandbox.run", "rm -rf build") with { Phase = ToolPhase.WaitingForYou };
        var note = Call("memory.save", "remember") with { NoteId = Guid.CreateVersion7() };
        var plain = Call("sandbox.run", "ls");

        TurnLedger.Folds(waiting).Should().BeFalse("a question for a person never hides in a collapsed row");
        TurnLedger.Folds(note).Should().BeFalse("a kept note offers to forget it");
        TurnLedger.Folds(plain).Should().BeTrue();

        ChatMessage[] messages = [Bot("1", run) with { Tools = [plain, waiting, note] }];

        var turns = messages.InTurns();
        turns[0].Ledger.Should().ContainSingle().Which.Calls.Should().Equal(plain);
    }

    #endregion

    #region Grouping

    [Fact]
    public void Three_calls_of_one_tool_in_a_row_are_one_step_and_two_stay_apart()
    {
        var three = TurnLedger.Steps([Bot("1", run) with { Tools = [Call("sandbox.run", "a"), Call("sandbox.run", "b"), Call("sandbox.run", "c")] }]);
        var two = TurnLedger.Steps([Bot("1", run) with { Tools = [Call("sandbox.run", "a"), Call("sandbox.run", "b"), Call("sandbox.read_file", "x")] }]);

        three.Should().ContainSingle();
        TurnLedger.Label(three[0]).Should().Be("Ran 3 commands");
        two.Should().HaveCount(3);
    }

    [Fact]
    public void A_group_takes_the_worst_state_among_its_calls()
    {
        var step = new LedgerStep([Call("sandbox.run", "a"), Call("sandbox.run", "b") with { Phase = ToolPhase.Failed, CommandError = "No such file" }, Call("sandbox.run", "c")], null, false);

        TurnLedger.StateOf(step).Should().Be(LedgerState.Failed);
        TurnLedger.FailureLine(step).Should().Be("No such file");
    }

    [Theory]
    [InlineData(ToolPhase.Done, LedgerState.Done)]
    [InlineData(ToolPhase.Starting, LedgerState.Running)]
    [InlineData(ToolPhase.Approved, LedgerState.Running)]
    [InlineData(ToolPhase.Running, LedgerState.Running)]
    [InlineData(ToolPhase.WaitingForYou, LedgerState.Running)]
    [InlineData(ToolPhase.Failed, LedgerState.Failed)]
    [InlineData(ToolPhase.Denied, LedgerState.Stopped)]
    [InlineData(ToolPhase.NotAllowed, LedgerState.Stopped)]
    [InlineData(ToolPhase.Interrupted, LedgerState.Stopped)]
    [InlineData(ToolPhase.Stopped, LedgerState.Stopped)]
    public void Every_phase_has_a_state(ToolPhase phase, LedgerState state) =>
        TurnLedger.StateOf(Call("sandbox.run", "a") with { Phase = phase }).Should().Be(state);

    #endregion

    #region Names

    [Fact]
    public void A_row_is_named_by_its_label_summary_state_and_time()
    {
        var step = new LedgerStep([Call("sandbox.run", "curl -s https://example.test") with { DurationMs = 1_200 }], null, false);

        TurnLedger.RowName(step).Should().Be("Ran a command, curl -s https://example.test, done, 1.2 seconds");
        TurnLedger.Duration(step).Should().Be("1.2 s");
    }

    [Fact]
    public void A_row_without_a_known_time_leaves_it_out()
    {
        var step = new LedgerStep([Call("sandbox.run", "ls")], null, false);

        TurnLedger.Duration(step).Should().BeNull("a time the run did not report is left out, never shown as zero");
        TurnLedger.RowName(step).Should().Be("Ran a command, ls, done");
    }

    [Fact]
    public void The_ledger_is_named_by_its_count_of_steps_with_each_call_of_a_group_counted()
    {
        IReadOnlyList<LedgerStep> steps =
        [
            LedgerStep.Thought("1", "Think", thinking: false),
            new([Call("sandbox.run", "a"), Call("sandbox.run", "b"), Call("sandbox.run", "c")], null, false),
        ];

        TurnLedger.ListName(steps).Should().Be("Work in this reply, 4 steps");
    }

    [Theory]
    [InlineData(400L, "0.4 s")]
    [InlineData(38_000L, "38 s")]
    [InlineData(125_000L, "2 min 5 s")]
    public void A_time_is_short_on_the_row(long ms, string shown) =>
        TurnLedger.Duration(new LedgerStep([Call("sandbox.run", "a") with { DurationMs = ms }], null, false)).Should().Be(shown);

    [Fact]
    public void Reasoning_is_a_quiet_row_that_says_it_is_thinking_while_it_arrives()
    {
        TurnLedger.Label(LedgerStep.Thought("1", "so far", thinking: true)).Should().Be("Thinking");
        TurnLedger.StateOf(LedgerStep.Thought("1", "so far", thinking: true)).Should().Be(LedgerState.Running);
        TurnLedger.Label(LedgerStep.Thought("1", "done", thinking: false)).Should().Be("Reasoned");
    }

    #endregion

    #region Private Methods

    private static ChatMessage Person(string key, string text) => new(key, true, text);

    private static ChatMessage Bot(string key, Guid runId, string text = "") => new(key, false, text) { RunId = runId };

    private static ToolCard Call(string tool, string summary) =>
        new(Guid.CreateVersion7(), ToolPhase.Done, tool is "sandbox.run" ? "Ran a command" : "Did a thing", "do a thing", summary, "{}", true, null, null, [])
        {
            Tool = tool,
        };

    #endregion
}
