namespace Drover.Ui.Tests.Chat;

/// <summary>Tool calls in words and as cards: names, one-line arguments, every state, motion, and what is read out.</summary>
public sealed class ToolCardTests : BunitContext
{
    #region Private Fields

    private static readonly Guid callId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000c1");

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
        card.FindAll("h3").Select(heading => heading.TextContent).Should().Equal("Arguments", "Result");
        card.Find(".dv-tool-exit").TextContent.Should().Be("Exit code 0");
        card.FindAll(".dv-tool-output").Should().BeEmpty("nothing was printed");
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

    #region Private Methods

    private static ToolCallView Call(string tool, string arguments, string state) =>
        new(callId, tool, tool is "issues.create" ? "Create issue" : tool, arguments, "allow", state, IsError: state is "failed");

    private IRenderedComponent<ToolCallCard> RenderCard(ToolCard card) =>
        Render<ToolCallCard>(parameters => parameters.Add(component => component.Card, card).Add(component => component.BotName, "Sample bot"));

    #endregion
}
