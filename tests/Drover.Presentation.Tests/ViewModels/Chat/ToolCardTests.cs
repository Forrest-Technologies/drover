namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// Tool calls in words and as cards: names, one-line arguments, every state, motion, and what is read out. The cases
/// are the web client's, asked of the card's view model in place of its markup.
/// </summary>
public sealed class ToolCardTests
{
    #region Private Fields

    private static readonly Guid callId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000c1");

    private static readonly Guid noteId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000d1");

    #endregion

    #region Words

    [Theory]
    [InlineData("sandbox.run", """{"command":"npm   test\n --watch=false"}""", "Running a command", "Ran a command", "npm test --watch=false")]
    [InlineData("sandbox.write_file", """{"path":"/home/user/notes.md","content":"x"}""", "Writing a file", "Wrote a file", "/home/user/notes.md")]
    [InlineData("sandbox.read_file", """{"path":"/etc/hosts"}""", "Reading a file", "Read a file", "/etc/hosts")]
    [InlineData("computer.screenshot", "{}", "Taking a screenshot", "Took a screenshot", "")]
    [InlineData("computer.click", """{"x":120,"y":340}""", "Clicking", "Clicked", "120, 340")]
    [InlineData("computer.key", """{"keys":["ctrl","l"]}""", "Pressing keys", "Pressed keys", "ctrl+l")]
    [InlineData("computer.scroll", """{"direction":"down","amount":3}""", "Scrolling", "Scrolled", "down 3")]
    [InlineData("time.now", """{"time_zone":"Europe/Berlin"}""", "Checking the time", "Checked the time", "Europe/Berlin")]
    [InlineData("crew.add", """{"name":"Sample scout","description":"Finds  sources\nfor a question.","instructions":"Sample"}""", "Making a bot", "Made a bot", "Sample scout: Finds sources for a question.")]
    [InlineData("crew_add", """{"name":"Sample scout","description":"","instructions":"Sample"}""", "Making a bot", "Made a bot", "Sample scout")]
    [InlineData("crew.remove", """{"bot_id":"0196f1a0-0000-7000-8000-000000000001"}""", "Removing a bot", "Removed a bot", "0196f1a0-0000-7000-8000-000000000001")]
    [InlineData("issues.create", """{"title":"Broken link","labels":["docs"]}""", "Create issue", "Create issue", """{"title":"Broken link","labels":["docs"]}""")]
    public void A_call_is_named_in_plain_words_with_its_arguments_on_one_line(string tool, string arguments, string doing, string done, string summary)
    {
        var running = Call(tool, arguments, "running");

        running.ToCard().Label.Should().Be(doing);
        (running with { State = "completed" }).ToCard().Label.Should().Be(done);
        running.ToCard().Summary.Should().Be(summary);
    }

    [Fact]
    public void A_stored_call_part_names_the_tool_as_the_model_saw_it_and_still_reads_in_words()
    {
        var part = new PartView("tool_call", ToolCallId: callId, ToolName: "sandbox_run", Arguments: """{"command":"ls"}""");
        var result = new PartView("tool_result", ToolCallId: callId, Result: """{"exit_code":2,"stdout":"","stderr":"no such file"}""", IsError: true);

        var card = ToolCards.FromParts(part, result).ToCard(result);

        card.Should().Match<ToolCard>(shown => shown.Label == "Ran a command" && shown.Summary == "ls" && shown.Phase == ToolPhase.Failed && shown.ExitCode == 2);
        card.Output.Should().Equal([new ToolOutputPiece("stderr", "no such file")], "a stored command's output is read from its result");
        card.Result.Should().BeNull("the output and the exit code say all the result holds, so it is not shown again as JSON");
    }

    [Theory]
    [InlineData("requested", ToolPhase.Starting, "Starting")]
    [InlineData("awaiting_approval", ToolPhase.WaitingForYou, "Waiting for you")]
    [InlineData("approved", ToolPhase.Approved, "Approved")]
    [InlineData("running", ToolPhase.Running, "Running")]
    [InlineData("completed", ToolPhase.Done, "Done")]
    [InlineData("failed", ToolPhase.Failed, "Failed")]
    [InlineData("denied", ToolPhase.Denied, "You declined")]
    [InlineData("refused", ToolPhase.NotAllowed, "Not allowed")]
    [InlineData("interrupted", ToolPhase.Interrupted, "Interrupted, outcome unknown")]
    [InlineData("cancelled", ToolPhase.Stopped, "Stopped")]
    public void Every_state_of_a_call_is_said_in_words(string state, ToolPhase phase, string words)
    {
        var card = Call("sandbox.run", """{"command":"make"}""", state).ToCard();

        card.Phase.Should().Be(phase);
        card.State.Should().Be(words);
        card.IsLive.Should().Be(phase is ToolPhase.Starting or ToolPhase.Approved or ToolPhase.Running);
    }

    #endregion

    #region Cards

    [Theory]
    [InlineData("completed", """{"exit_code":0,"stdout":"first\nsecond"}""", "first…", null)]
    [InlineData("failed", """{"error":"The sandbox provider refused this request."}""", null, "The sandbox provider refused this request.")]
    public void A_command_card_shows_the_result_at_a_glance(string state, string result, string? preview, string? error)
    {
        var call = Call("sandbox_run", """{"command":"sample command"}""", state) with { Preview = result };

        var card = Shown(call.ToCard(), "Atlas");

        card.Open.Should().BeFalse();
        card.PreviewLine.Should().Be(preview);
        card.ErrorLine.Should().Be(error);
    }

    [Theory]
    [InlineData("running", "Running a command", "Running", BadgeKind.Live, null)]
    [InlineData("completed", "Ran a command", "Done", BadgeKind.Ok, "DvOk")]
    [InlineData("failed", "Ran a command", "Failed", BadgeKind.Danger, "DvDanger")]
    [InlineData("interrupted", "Ran a command", "Interrupted, outcome unknown", BadgeKind.Warn, "DvWarn")]
    [InlineData("denied", "Run a command", "You declined", BadgeKind.Neutral, null)]
    [InlineData("cancelled", "Ran a command", "Stopped", BadgeKind.Neutral, null)]
    public void A_card_names_the_call_says_its_state_in_words_and_moves_only_while_the_tool_runs(
        string state,
        string label,
        string words,
        BadgeKind badge,
        string? edge)
    {
        var card = Shown(Call("sandbox.run", """{"command":"make build"}""", state).ToCard());

        card.IsAsk.Should().BeFalse();
        card.Open.Should().BeFalse();
        card.Card.Label.Should().Be(label);
        card.Card.State.Should().Be(words);
        card.StateKind.Should().Be(badge);
        card.Summary.Should().Be("make build");
        card.Running.Should().Be(state is "running", "the traveling line is the running state and nothing else");
        card.EndedEdge.Should().Be(edge);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_nonzero_command_shows_failure_and_stderr_live_and_after_reopening(bool stored)
    {
        var arguments = """{"command":"sample command"}""";
        var output = """{"exit_code":7,"stdout":"started","stderr":"could not finish"}""";
        var part = new PartView("tool_call", ToolCallId: callId, ToolName: "sandbox_run", Arguments: arguments);
        var result = new PartView("tool_result", ToolCallId: callId, Result: output);
        var call = stored ? ToolCards.FromParts(part, result) : Call("sandbox.run", arguments, "completed") with
        {
            Preview = output,
            Pieces = [new("stdout", "started"), new("stderr", "could not finish")],
        };
        var card = Shown(call.ToCard(stored ? result : null));

        card.StateKind.Should().Be(BadgeKind.Danger);
        card.Card.State.Should().Be("Failed");
        card.PreviewLine.Should().Be("could not finish…");
        card.Open = true;
        card.PreviewLine.Should().BeNull("an open card shows the output itself");
        card.ExitLine.Should().Be("Exit code 7");
        string.Concat(card.Card.Output.Select(piece => piece.Text)).Should().Contain("started").And.Contain("could not finish");
        card.Card.Output.Should().Contain(piece => piece.Stream == "stderr" && piece.Text.Contains("could not finish"), "error output is marked in words");
    }

    [Fact]
    public void A_command_that_times_out_shows_failure_when_the_tool_returned_an_outcome()
    {
        var call = Call("sandbox.run", """{"command":"sample command"}""", "completed") with
        {
            Preview = """{"exit_code":null,"error":"The command timed out."}""",
        };
        var card = Shown(call.ToCard());

        card.StateKind.Should().Be(BadgeKind.Danger);
        card.Card.State.Should().Be("Failed");
        card.ErrorLine.Should().Be("The command timed out.");
    }

    [Fact]
    public void Opening_a_card_shows_its_arguments_and_its_result_with_the_exit_code()
    {
        var call = Call("sandbox.run", """{"command":"make"}""", "completed") with { Preview = """{"exit_code":0,"stdout":"ok"}""" };
        var card = Shown(call.ToCard());
        var redraws = 0;
        card.PropertyChanged += (_, _) => redraws++;

        card.Open = true;

        redraws.Should().Be(1, "the card is drawn again when it opens");
        card.PartTitles.Should().Equal("Arguments", "Output", "Result");
        card.ExitLine.Should().Be("Exit code 0");
        string.Concat(card.Card.Output.Select(piece => piece.Text)).Should().Be("ok");
    }

    [Fact]
    public void A_screenshot_card_says_the_bot_took_one_and_shows_no_picture()
    {
        var call = Call("computer.screenshot", "{}", "completed") with { Preview = """{"screenshot":"attached","width":1024,"height":768}""" };
        var card = Shown(call.ToCard());

        card.Card.Label.Should().Be("Took a screenshot");
        card.Summary.Should().BeNull();
        card.Open = true;
        card.PartTitles.Should().BeSubsetOf(["Arguments", "Output", "Result"], "the card holds words about the picture, never the picture");
    }

    [Fact]
    public void A_bot_asking_to_make_a_bot_shows_every_detail_of_it_under_its_own_name_in_one_card()
    {
        var call = Call(
            "crew.add",
            """{"name":"Sample scout","description":"Finds sources.","instructions":"Line one.\nLine two."}""",
            "awaiting_approval") with
        {
            Details = [new(ToolApprovalRequested.ModelDetail, "sample-model"), new(ToolApprovalRequested.BeneathDetail, "Sample lead")],
        };

        var card = Shown(call.ToCard(), "Sample lead");

        card.IsAsk.Should().BeTrue();
        card.AskTitle.Should().Be("Sample lead asks to make a bot");
        card.Card.Facts.Select(fact => fact.Name).Should().Equal("Name", "What it is for", "Instructions", "Model", "Works beneath");
        card.Card.Facts.Select(fact => fact.Value).Should().Equal("Sample scout", "Finds sources.", "Line one.\nLine two.", "sample-model", "Sample lead");
        card.AskShowsArguments.Should().BeFalse("the details are said in words, not left as JSON");
        new[] { ToolCallCardViewModel.Approve, ToolCallCardViewModel.Deny }.Should().Equal("Approve", "Deny");
    }

    [Fact]
    public void A_bot_asking_to_remove_a_bot_says_what_happens_to_the_bots_beneath_it()
    {
        var call = Call("crew.remove", """{"bot_id":"0196f1a0-0000-7000-8000-000000000001","beneath":"keep"}""", "awaiting_approval");

        var facts = call.ToCard().Facts;

        facts.Should().Equal(new ToolFact("The bots beneath it", "Kept, and moved up"));
    }

    [Fact]
    public async Task A_call_waiting_for_an_answer_asks_in_plain_words_and_shows_every_argument_the_summary_leaves_out()
    {
        List<(Guid, bool)> answers = [];
        var call = Call("sandbox.write_file", """{"path":"/home/user/run.sh","content":"rm -rf build"}""", "awaiting_approval");
        var card = Shown(call.ToCard());
        card.Decide = (id, approved) =>
        {
            answers.Add((id, approved));
            return Task.FromResult<string?>(null);
        };

        card.AskTitle.Should().Be("Sample bot asks to write a file");
        card.Card.State.Should().Be("Waiting for you");
        card.Summary.Should().Be("/home/user/run.sh");
        card.AskShowsArguments.Should().BeTrue();
        card.Card.Arguments.Should().Contain("rm -rf build", "a person sees everything they approve");
        card.Deciding.Should().BeFalse();

        await card.Answer(approved: false);

        answers.Should().Equal((callId, false));
        card.Deciding.Should().BeTrue("the buttons stay off once the answer is sent");
    }

    [Fact]
    public async Task An_answer_that_was_not_sent_says_why_and_can_be_given_again()
    {
        var card = Shown(Call("sandbox.run", """{"command":"ls"}""", "awaiting_approval").ToCard());
        card.Decide = (_, _) => Task.FromResult<string?>("This request was answered already.");

        await card.Answer(approved: false);

        card.Problem.Should().Be("This request was answered already.");
        card.Deciding.Should().BeFalse("a failed answer can be given again");
    }

    [Fact]
    public async Task A_card_that_stopped_waiting_forgets_the_answer_that_was_on_its_way()
    {
        var card = Shown(Call("sandbox.run", """{"command":"ls"}""", "awaiting_approval").ToCard());
        card.Decide = (_, _) => Task.FromResult<string?>(null);
        await card.Answer(approved: true);

        card.Card = Call("sandbox.run", """{"command":"ls"}""", "running").ToCard();
        card.ParametersSet();

        card.Deciding.Should().BeFalse();
        card.IsAsk.Should().BeFalse();
        card.Running.Should().BeTrue("a line travels around the card while the tool runs");
    }

    [Fact]
    public async Task Answering_a_question_hands_the_focus_to_the_card_that_takes_its_place()
    {
        var card = Shown(Call("sandbox.run", """{"command":"ls"}""", "awaiting_approval").ToCard());
        card.Decide = (_, _) => Task.FromResult<string?>(null);

        card.TakeFocusForAnswer().Should().BeFalse("nothing moves while the question still stands");
        await card.Answer(approved: true);
        card.IsAsk.Should().BeTrue("the card changes only when the run's log says what happened");
        card.TakeFocusForAnswer().Should().BeFalse("the answer button keeps the focus until the question goes");

        card.Card = Call("sandbox.run", """{"command":"ls"}""", "running").ToCard();
        card.ParametersSet();

        card.IsAsk.Should().BeFalse();
        card.TakeFocusForAnswer().Should().BeTrue("the focus moves to the card that replaced the question");
        card.TakeFocusForAnswer().Should().BeFalse("once");
    }

    [Fact]
    public async Task An_answer_that_was_not_sent_leaves_the_focus_with_the_question()
    {
        var card = Shown(Call("sandbox.run", """{"command":"ls"}""", "awaiting_approval").ToCard());
        card.Decide = (_, _) => Task.FromResult<string?>("This request was answered already.");

        await card.Answer(approved: true);
        card.Card = Call("sandbox.run", """{"command":"ls"}""", "running").ToCard();
        card.ParametersSet();

        card.TakeFocusForAnswer().Should().BeFalse("a refused answer never sent the focus away");
    }

    #endregion

    #region Announcements

    [Fact]
    public void A_call_is_read_out_once_for_each_change_of_state_and_never_for_its_output()
    {
        var announcer = new ToolAnnouncer();
        var running = Call("sandbox.run", """{"command":"make"}""", "running");
        ToolCallView[] steps =
        [
            running with { State = "requested" },
            running,
            running with { Pieces = [new("stdout", "one\n")] },
            running with { Pieces = [new("stdout", "one\ntwo\n")] },
            running with { Pieces = [new("stdout", "one\ntwo\n"), new("stderr", "warn\n")] },
            running with { State = "completed", Pieces = [new("stdout", "one\ntwo\n"), new("stderr", "warn\n")] },
        ];

        var heard = steps.Select(step => announcer.Next([step.ToCard()], "Sample bot")).Where(sentence => sentence is not null).ToList();

        heard.Should().Equal("Running a command.", "Ran a command. Done.");
    }

    [Fact]
    public void Calls_that_had_ended_before_the_list_was_shown_are_not_read_out()
    {
        var announcer = new ToolAnnouncer();
        var done = Call("sandbox.run", "{}", "completed").ToCard();
        var waiting = (Call("sandbox.run", "{}", "awaiting_approval") with { ToolCallId = Guid.CreateVersion7() }).ToCard();

        announcer.Next([done], "Sample bot").Should().BeNull();
        announcer.Next([done], "Sample bot").Should().BeNull();
        announcer.Next([done, waiting], "Sample bot").Should().Be("Sample bot asks to run a command. Waiting for you.");
        announcer.Next([done, waiting], "Sample bot").Should().BeNull("nothing changed");
    }

    #endregion

    #region Notes

    [Fact]
    public async Task A_saved_note_reads_as_the_note_and_offers_one_button_named_for_it_that_forgets_it()
    {
        (Guid Id, string Text)? forgot = null;
        var card = NoteCard((id, text) =>
        {
            forgot = (id, text);
            return Task.FromResult(new ForgetOutcome(true));
        });

        card.Card.Label.Should().Be("Remembered: Prefers tea.");
        card.Note.Should().Be(ToolNoteState.Offered);
        ToolCallCardViewModel.ForgetLabel.Should().Be("Forget");
        card.ForgetName.Should().Be("Forget the note: Prefers tea.");
        await card.ForgetNote();
        card.TakeFocusForLine().Should().BeFalse("the line is not there until the conversation says the note is gone");
        card.Forgotten = true;

        forgot.Should().Be((noteId, "Prefers tea."), "the note is removed only while it says what the card shows");
        card.Note.Should().Be(ToolNoteState.Forgotten, "the button is replaced once the note is gone");
        MemoryWords.Forgotten.Should().Be("Forgotten. The note is no longer kept.");
        card.TakeFocusForLine().Should().BeTrue("the focus moves to the line that took the button's place");
        card.TakeFocusForLine().Should().BeFalse("once");

        card.Forgotten = false;
        card.Note.Should().Be(ToolNoteState.Offered, "once undone, the note can be forgotten again");
    }

    [Fact]
    public async Task A_note_changed_since_its_card_is_left_alone_and_the_card_says_where_to_see_it()
    {
        var card = NoteCard((_, _) => Task.FromResult(new ForgetOutcome(false, Changed: true)));

        await card.ForgetNote();

        card.Note.Should().Be(ToolNoteState.ChangedSince);
        card.ChangedSinceLine.Should().Be("This note has changed since Sample bot saved it, so it was not forgotten.");
        ToolCallCardViewModel.OpenMemory.Should().Be("Open Memory to see it");
        card.MemoryHref.Should().Be("/talk/sample/memory");
        card.TakeFocusForLine().Should().BeTrue();
    }

    [Fact]
    public async Task A_note_that_could_not_be_forgotten_keeps_its_button_and_says_why()
    {
        var card = NoteCard((_, _) => Task.FromResult(new ForgetOutcome(false, Problem: "The note was not forgotten. The server failed to answer.")));

        await card.ForgetNote();

        card.Note.Should().Be(ToolNoteState.Offered);
        card.ForgetProblem.Should().Contain("The note was not forgotten.");
        card.TakeFocusForLine().Should().BeFalse("the button keeps the focus");
    }

    [Fact]
    public void A_changed_note_shows_before_and_after_and_a_forgotten_one_names_the_note()
    {
        var changed = (Call("memory.change", $$"""{"note_id":"{{noteId}}","text":"Prefers green tea."}""", "completed") with
        {
            Preview = $$"""{"note":{"id":"{{noteId}}","text":"Prefers green tea."},"before":"Prefers tea."}""",
        }).ToCard();
        var forgotten = (Call("memory.forget", $$"""{"note_id":"{{noteId}}"}""", "completed") with
        {
            Preview = $$"""{"removed":"{{noteId}}","text":"Prefers tea."}""",
        }).ToCard();

        changed.Label.Should().Be("Updated a note");
        changed.Facts.Should().Equal(new ToolFact("Before", "Prefers tea."), new ToolFact("After", "Prefers green tea."));
        changed.NoteId.Should().BeNull("only a saved note is offered to forget");
        forgotten.Label.Should().Be("Forgot: Prefers tea.");
        var shown = Shown(changed);
        shown.Forget = (_, _) => Task.FromResult(new ForgetOutcome(true));
        shown.Open = true;
        shown.Card.Label.Should().Be("Updated a note");
        shown.ShowsFacts.Should().BeTrue();
        shown.Card.Facts.Select(fact => fact.Name).Should().Equal("Before", "After");
        shown.Note.Should().Be(ToolNoteState.None);
    }

    [Theory]
    [InlineData("running")]
    [InlineData("failed")]
    public void A_note_call_that_has_not_finished_well_reads_as_the_call_and_offers_nothing(string state)
    {
        var card = (Call("memory.save", """{"text":"Prefers tea."}""", state) with
        {
            Preview = $$$"""{"note":{"id":"{{{noteId}}}","text":"Prefers tea."}}""",
        }).ToCard();

        card.Label.Should().Be(state is "running" ? "Saving a note" : "Saved a note");
        card.NoteId.Should().BeNull();
    }

    [Theory]
    [InlineData("delegate", "The answer, in plain text.")]
    [InlineData("memory.save", "Not JSON at all.")]
    public void A_finished_call_whose_result_is_plain_text_still_makes_a_card(string tool, string result)
    {
        var card = (Call(tool, """{"text":"Prefers tea."}""", "completed") with { Preview = result }).ToCard();

        card.NoteId.Should().BeNull();
        card.Phase.Should().Be(ToolPhase.Done);
    }

    [Fact]
    public void A_cut_off_result_still_names_the_note_from_the_arguments()
    {
        var card = (Call("memory.save", """{"text":"Prefers tea."}""", "completed") with
        {
            Preview = $$"""{"note":{"id":"{{noteId}}","text":"Prefers t""",
        }).ToCard();

        card.Label.Should().Be("Remembered: Prefers tea.");
        card.NoteId.Should().BeNull("the note's id is known only once the whole result is");
    }

    #endregion

    #region Private Methods

    private static ToolCallView Call(string tool, string arguments, string state) =>
        new(callId, tool, tool is "issues.create" ? "Create issue" : tool, arguments, "allow", state, IsError: state is "failed");

    private static ToolCallCardViewModel NoteCard(Func<Guid, string, Task<ForgetOutcome>> forget)
    {
        var call = Call("memory.save", """{"text":"Prefers tea."}""", "completed") with
        {
            Preview = $$$"""{"note":{"id":"{{{noteId}}}","text":"Prefers tea."}}""",
        };
        var card = Shown(call.ToCard());
        card.MemoryHref = "/talk/sample/memory";
        card.Forget = forget;
        return card;
    }

    private static ToolCallCardViewModel Shown(ToolCard card, string botName = "Sample bot")
    {
        var model = new ToolCallCardViewModel { Card = card, BotName = botName };
        model.ParametersSet();
        return model;
    }

    #endregion
}
