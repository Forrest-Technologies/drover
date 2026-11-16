namespace Drover.Ui.Tests.Chat;

/// <summary>Tool calls in words and as cards: names, one-line arguments, every state, motion, and what is read out.</summary>
public sealed class ToolCardTests : BunitContext
{
    #region Private Fields

    private static readonly Guid callId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000c1");

    private static readonly Guid noteId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000d1");

    #endregion

    #region Constructors

    public ToolCardTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

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
        card.Result.Should().Contain("\"stderr\": \"no such file\"", "a stored result is shown whole and indented");
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
    [InlineData("completed", """{"exit_code":0,"stdout":"first\nsecond"}""", ".dv-tool-preview", "first…")]
    [InlineData("failed", """{"error":"The sandbox provider refused this request."}""", ".dv-tool-error",
        "The sandbox provider refused this request.")]
    public void A_command_card_shows_the_result_at_a_glance(string state, string result, string selector, string expected)
    {
        var call = Call("sandbox_run", """{"command":"sample command"}""", state) with { Preview = result };
        var card = Render<ToolCallCard>(parameters => parameters
            .Add(component => component.Card, call.ToCard())
            .Add(component => component.BotName, "Atlas"));

        card.Find(".dv-disclosure-toggle").GetAttribute("aria-expanded").Should().Be("false");
        card.Find(selector).TextContent.Should().Be(expected);
    }

    [Theory]
    [InlineData("running", "Running a command", "Running", "dv-badge-live", "dv-state-tool")]
    [InlineData("completed", "Ran a command", "Done", "dv-badge-ok", "dv-tool-ended-ok")]
    [InlineData("failed", "Ran a command", "Failed", "dv-badge-danger", "dv-tool-ended-failed")]
    [InlineData("interrupted", "Ran a command", "Interrupted, outcome unknown", "dv-badge-warn", "dv-tool-ended-unknown")]
    [InlineData("denied", "Run a command", "You declined", "dv-badge-neutral", null)]
    [InlineData("cancelled", "Ran a command", "Stopped", "dv-badge-neutral", null)]
    public void A_card_names_the_call_says_its_state_in_words_and_moves_only_while_the_tool_runs(
        string state,
        string label,
        string words,
        string badge,
        string? edge)
    {
        var card = RenderCard(Call("sandbox.run", """{"command":"make build"}""", state).ToCard());

        var toggle = card.Find("button.dv-disclosure-toggle");
        toggle.GetAttribute("aria-expanded").Should().Be("false");
        toggle.QuerySelector(".dv-disclosure-title")!.TextContent.Should().Be(label);
        toggle.QuerySelector($".{badge}")!.TextContent.Should().Be(words);
        toggle.QuerySelector(".dv-disclosure-summary")!.TextContent.Should().Be("make build");
        var frame = card.Find("section");
        frame.ClassList.Contains("dv-state-tool").Should().Be(state is "running", "the traveling line is the running state and nothing else");
        if (edge is not null)
        {
            frame.ClassList.Should().Contain(edge);
        }
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
        var card = RenderCard(call.ToCard(stored ? result : null));

        card.Find(".dv-badge-danger").TextContent.Should().Be("Failed");
        card.Find(".dv-tool-preview").TextContent.Should().Be("could not finish…");
        card.Find(".dv-disclosure-toggle").Click();
        card.Find(".dv-tool-exit").TextContent.Should().Be("Exit code 7");
        card.Find(".dv-tool-output").TextContent.Should().Contain("started").And.Contain("could not finish");
    }

    [Fact]
    public void A_command_that_times_out_shows_failure_when_the_tool_returned_an_outcome()
    {
        var call = Call("sandbox.run", """{"command":"sample command"}""", "completed") with
        {
            Preview = """{"exit_code":null,"error":"The command timed out."}""",
        };
        var card = RenderCard(call.ToCard());

        card.Find(".dv-badge-danger").TextContent.Should().Be("Failed");
        card.Find(".dv-tool-error").TextContent.Should().Be("The command timed out.");
    }

    [Fact]
    public void Opening_a_card_by_keyboard_shows_its_arguments_and_its_result_with_the_exit_code()
    {
        var call = Call("sandbox.run", """{"command":"make"}""", "completed") with { Preview = """{"exit_code":0,"stdout":"ok"}""" };
        var card = RenderCard(call.ToCard());
        var toggle = card.Find("button.dv-disclosure-toggle");

        toggle.TagName.Should().Be("BUTTON", "a native button opens with Enter and Space");
        toggle.Click();

        toggle = card.Find("button.dv-disclosure-toggle");
        toggle.GetAttribute("aria-expanded").Should().Be("true");
        card.Find($"#{toggle.GetAttribute("aria-controls")}").HasAttribute("hidden").Should().BeFalse();
        card.FindAll("h3").Select(heading => heading.TextContent).Should().Equal("Arguments", "Output", "Result");
        card.Find(".dv-tool-exit").TextContent.Should().Be("Exit code 0");
        card.Find(".dv-tool-output").TextContent.Should().Be("ok");
    }

    [Fact]
    public void A_screenshot_card_says_the_bot_took_one_and_shows_no_picture()
    {
        var call = Call("computer.screenshot", "{}", "completed") with { Preview = """{"screenshot":"attached","width":1024,"height":768}""" };
        var card = RenderCard(call.ToCard());

        card.Find(".dv-disclosure-title").TextContent.Should().Be("Took a screenshot");
        card.FindAll(".dv-disclosure-summary").Should().BeEmpty();
        card.Find("button.dv-disclosure-toggle").Click();
        card.FindAll("img").Should().BeEmpty();
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

        var card = Render<ToolCallCard>(parameters => parameters
            .Add(component => component.Card, call.ToCard())
            .Add(component => component.BotName, "Sample lead"));

        card.Find("h2").TextContent.Should().Be("Sample lead asks to make a bot");
        card.FindAll("dl.dv-tool-facts dt").Select(name => name.TextContent).Should().Equal("Name", "What it is for", "Instructions", "Model", "Works beneath");
        card.FindAll("dl.dv-tool-facts dd").Select(value => value.TextContent)
            .Should().Equal("Sample scout", "Finds sources.", "Line one.\nLine two.", "sample-model", "Sample lead");
        card.FindAll(".dv-code-body").Should().BeEmpty("the details are said in words, not left as JSON");
        card.FindAll("button.dv-button").Select(button => button.TextContent.Trim()).Should().Equal("Approve", "Deny");
    }

    [Fact]
    public void A_bot_asking_to_remove_a_bot_says_what_happens_to_the_bots_beneath_it()
    {
        var call = Call("crew.remove", """{"bot_id":"0196f1a0-0000-7000-8000-000000000001","beneath":"keep"}""", "awaiting_approval");

        var facts = call.ToCard().Facts;

        facts.Should().Equal(new ToolFact("The bots beneath it", "Kept, and moved up"));
    }

    [Fact]
    public void A_call_waiting_for_an_answer_asks_in_plain_words_and_shows_every_argument_the_summary_leaves_out()
    {
        List<(Guid, bool)> answers = [];
        var call = Call("sandbox.write_file", """{"path":"/home/user/run.sh","content":"rm -rf build"}""", "awaiting_approval");
        var card = Render<ToolCallCard>(parameters => parameters
            .Add(component => component.Card, call.ToCard())
            .Add(component => component.BotName, "Sample bot")
            .Add(component => component.Decide, (id, approved) =>
            {
                answers.Add((id, approved));
                return Task.FromResult<string?>(null);
            }));

        card.Find("h2").TextContent.Should().Be("Sample bot asks to write a file");
        card.Find(".dv-badge-warn").TextContent.Should().Be("Waiting for you");
        card.Find(".dv-tool-ask-summary").TextContent.Should().Be("/home/user/run.sh");
        card.Find(".dv-code-body").TextContent.Should().Contain("rm -rf build", "a person sees everything they approve");
        card.FindAll("button.dv-button").Select(button => button.TextContent.Trim()).Should().Equal("Approve", "Deny");

        card.FindAll("button.dv-button")[1].Click();

        answers.Should().Equal((callId, false));
        card.FindAll("button.dv-button").Should().OnlyContain(button => button.HasAttribute("disabled"));
    }

    [Fact]
    public void Calls_made_without_words_stand_in_the_bots_turn_without_a_second_head()
    {
        var list = Render<MessageList>(parameters => parameters
            .Add(component => component.Messages,
            [
                new("m1", true, "Take a look"),
                new("m2", false, string.Empty) { Tools = [Call("computer.screenshot", "{}", "completed").ToCard()] },
            ])
            .Add(component => component.BotName, "Sample bot"));

        var turn = list.FindAll("article")[1];
        turn.GetAttribute("aria-label").Should().Be("What Sample bot did");
        turn.QuerySelectorAll(".dv-message-head").Should().BeEmpty();
        turn.QuerySelector(".dv-disclosure-title")!.TextContent.Should().Be("Took a screenshot");
    }

    #endregion

    #region Announcements

    [Fact]
    public void A_call_is_read_out_once_for_each_change_of_state_and_never_for_its_output()
    {
        var running = Call("sandbox.run", """{"command":"make"}""", "running");
        List<ChatMessage> Turn(ToolCallView call) => [new ChatMessage("m1", false, "Building it.") { Tools = [call.ToCard()] }];
        var list = Render<MessageList>(parameters => parameters
            .Add(component => component.Messages, Turn(running with { State = "requested" }))
            .Add(component => component.BotName, "Sample bot"));
        List<string> heard = [list.Find("[aria-live=polite]").TextContent];

        ToolCallView[] steps =
        [
            running,
            running with { Pieces = [new("stdout", "one\n")] },
            running with { Pieces = [new("stdout", "one\ntwo\n")] },
            running with { Pieces = [new("stdout", "one\ntwo\n"), new("stderr", "warn\n")] },
            running with { State = "completed", Pieces = [new("stdout", "one\ntwo\n"), new("stderr", "warn\n")] },
        ];
        foreach (var step in steps)
        {
            list.Render(parameters => parameters.Add(component => component.Messages, Turn(step)));
            heard.Add(list.Find("[aria-live=polite]").TextContent);
        }

        heard.Distinct().Should().Equal("Building it. Running a command.", "Ran a command. Done.");
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
    public void A_saved_note_reads_as_the_note_and_offers_one_button_named_for_it_that_forgets_it()
    {
        (Guid Id, string Text)? forgot = null;
        var card = RenderNoteCard((id, text) =>
        {
            forgot = (id, text);
            return Task.FromResult(new ForgetOutcome(true));
        });

        card.Find(".dv-disclosure-title").TextContent.Trim().Should().Be("Remembered: Prefers tea.");
        var forget = card.Find(".dv-tool-note button");
        forget.TextContent.Trim().Should().Be("Forget");
        forget.GetAttribute("aria-label").Should().Be("Forget the note: Prefers tea.");
        forget.Click();
        card.Render(parameters => parameters.Add(component => component.Forgotten, true));

        forgot.Should().Be((noteId, "Prefers tea."), "the note is removed only while it says what the card shows");
        card.FindAll(".dv-tool-note button").Should().BeEmpty("the button is replaced once the note is gone");
        card.Find(".dv-tool-forgotten").TextContent.Should().Be("Forgotten. The note is no longer kept.");
        JSInterop.VerifyFocusAsyncInvoke().Arguments[0].Should().BeOfType<ElementReference>("the focus moves to the line that took the button's place");

        card.Render(parameters => parameters.Add(component => component.Forgotten, false));
        card.Find(".dv-tool-note button").TextContent.Trim().Should().Be("Forget", "once undone, the note can be forgotten again");
    }

    [Fact]
    public void A_note_changed_since_its_card_is_left_alone_and_the_card_says_where_to_see_it()
    {
        var card = RenderNoteCard((_, _) => Task.FromResult(new ForgetOutcome(false, Changed: true)));

        card.Find(".dv-tool-note button").Click();

        card.Find(".dv-tool-forgotten").TextContent.Should().Contain("This note has changed since Sample bot saved it, so it was not forgotten.");
        var link = card.Find(".dv-tool-forgotten a");
        link.TextContent.Should().Be("Open Memory to see it");
        link.GetAttribute("href").Should().Be("/talk/sample/memory");
        card.FindAll(".dv-tool-note button").Should().BeEmpty();
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public void A_note_that_could_not_be_forgotten_keeps_its_button_and_says_why()
    {
        var card = RenderNoteCard((_, _) => Task.FromResult(new ForgetOutcome(false, Problem: "The note was not forgotten. The server failed to answer.")));

        card.Find(".dv-tool-note button").Click();

        card.Find(".dv-tool-note button").TextContent.Trim().Should().Be("Forget");
        card.Find("[role=alert]").TextContent.Should().Contain("The note was not forgotten.");
        JSInterop.VerifyNotInvoke("Blazor._internal.domWrapper.focus");
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
        var shown = RenderCard(changed);
        shown.Find("button[aria-expanded]").Click();
        shown.Find(".dv-disclosure-title").TextContent.Trim().Should().Be("Updated a note");
        shown.FindAll(".dv-tool-fact dt").Select(term => term.TextContent).Should().Equal("Before", "After");
        shown.FindAll(".dv-tool-note").Should().BeEmpty();
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

    private IRenderedComponent<ToolCallCard> RenderNoteCard(Func<Guid, string, Task<ForgetOutcome>> forget)
    {
        var call = Call("memory.save", """{"text":"Prefers tea."}""", "completed") with
        {
            Preview = $$$"""{"note":{"id":"{{{noteId}}}","text":"Prefers tea."}}""",
        };
        return Render<ToolCallCard>(parameters => parameters
            .Add(component => component.Card, call.ToCard())
            .Add(component => component.BotName, "Sample bot")
            .Add(component => component.MemoryHref, "/talk/sample/memory")
            .Add(component => component.Forget, forget));
    }

    private IRenderedComponent<ToolCallCard> RenderCard(ToolCard card) =>
        Render<ToolCallCard>(parameters => parameters.Add(component => component.Card, card).Add(component => component.BotName, "Sample bot"));

    #endregion
}
