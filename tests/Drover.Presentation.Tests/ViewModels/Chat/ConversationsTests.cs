namespace Drover.Presentation.Tests.ViewModels.Chat;

public sealed class ConversationsTests
{
    #region Public Methods

    [Fact]
    public void The_conversation_shows_what_the_person_wrote_and_what_the_bot_said()
    {
        MessageView[] timeline =
        [
            Message("user", false, new PartView("text", "What time is it")),
            Message("assistant", false, new PartView("tool_call", ToolName: "time.now", Arguments: "{}")),
            Message("tool", false, new PartView("tool_result", Result: "\"12:00\"")),
            Message("assistant", false, new PartView("reasoning", "Hidden"), new PartView("text", "Noon.")),
        ];

        var shown = timeline.ToChatMessages();

        shown.Select(message => (message.FromPerson, message.Text, message.Streaming))
            .Should().Equal((true, "What time is it", false), (false, "Noon.", false));
        shown.Select(message => message.Key).Should().Equal(timeline[0].Id.ToString(), timeline[3].Id.ToString());
    }

    [Fact]
    public void A_reply_still_arriving_is_shown_as_streaming_once_it_has_words()
    {
        var empty = Message("assistant", true);
        var arriving = Message("assistant", true, new PartView("text", "Half a sent"));

        new[] { empty }.ToChatMessages().Should().BeEmpty();
        new[] { arriving }.ToChatMessages().Single().Should().Be(new ChatMessage(arriving.Id.ToString(), false, "Half a sent", true));
    }

    [Fact]
    public void A_reply_shows_its_reasoning_while_it_arrives_and_after_it_is_stored()
    {
        var arriving = Message("assistant", true, new PartView("reasoning", "Check the sample"));
        var stored = Message("assistant", false, new PartView("reasoning", "Check the sample page."), new PartView("text", "Done."));

        var thinking = new[] { arriving }.ToChatMessages().Single();
        var finished = new[] { stored }.ToChatMessages().Single();

        thinking.Should().Match<ChatMessage>(message => message.Text == string.Empty && message.Thinking);
        thinking.Reasoning.Should().Be("Check the sample");
        finished.Reasoning.Should().Be("Check the sample page.");
        finished.Thinking.Should().BeFalse();
    }

    [Fact]
    public void Reasoning_saved_in_pieces_reads_as_one_text_and_a_tool_call_keeps_two_thoughts_apart()
    {
        var reply = Message(
            "assistant",
            false,
            new PartView("reasoning", "Let"),
            new PartView("reasoning", " me check whether the sample"),
            new PartView("reasoning", " page lists it."),
            new PartView("tool_call", ToolName: "web.search", Arguments: "{}"),
            new PartView("reasoning", "It does."),
            new PartView("reasoning", " "),
            new PartView("text", "Yes."));

        var shown = new[] { reply }.ToChatMessages().Single();

        shown.Reasoning.Should().Be("Let me check whether the sample page lists it.\n\nIt does. ");
    }

    [Fact]
    public void A_reply_shows_the_web_sources_it_cites_once_each_with_a_title()
    {
        var reply = Message(
            "assistant",
            false,
            new PartView("text", "Cited."),
            new PartView("source", "Sample page", Url: "https://www.sample.example/page"),
            new PartView("source", "Same page again", Url: "https://www.sample.example/page"),
            new PartView("source", Url: "http://sample.example/untitled"),
            new PartView("source", "Not a web address", Url: "javascript:alert(1)"));

        var sources = new[] { reply }.ToChatMessages().Single().Sources;

        sources.Should().Equal(
            new SourceLink("Sample page", "https://www.sample.example/page", "sample.example"),
            new SourceLink("sample.example", "http://sample.example/untitled", "sample.example"));
    }

    [Fact]
    public void A_reply_without_reasoning_or_sources_shows_neither()
    {
        var reply = Message("assistant", false, new PartView("text", "Plain."));

        var shown = new[] { reply }.ToChatMessages().Single();

        shown.Reasoning.Should().BeNull();
        shown.Sources.Should().BeEmpty();
        shown.Thinking.Should().BeFalse();
    }

    [Fact]
    public void A_message_the_bot_has_not_taken_yet_is_shown_as_pending()
    {
        var waiting = Message("user", false, new PartView("text", "One more thing")) with { IsPending = true };

        new[] { waiting }.ToChatMessages().Single().Pending.Should().BeTrue();
    }

    [Fact]
    public void A_message_a_schedule_sent_names_the_schedule_by_its_time_or_says_it_was_removed()
    {
        var scheduled = Message("user", false, new PartView("text", "Brief me")) with { ScheduleId = Guid.CreateVersion7(), ScheduleCron = "30 9 * * 1-5" };
        var orphaned = Message("user", false, new PartView("text", "Brief me")) with { ScheduleId = Guid.CreateVersion7() };
        var typed = Message("user", false, new PartView("text", "Hello"));

        var shown = new[] { scheduled, orphaned, typed }.ToChatMessages();

        shown.Select(message => message.SentBy).Should().Equal(
            "Sent by the routine 'Every weekday at 09:30'",
            "Sent by a routine that has since been removed",
            null);
        shown.Should().OnlyContain(message => message.FromPerson, "a schedule sends on its owner's behalf, on the person's side of the conversation");
    }

    [Fact]
    public void A_bot_is_thinking_while_it_works_and_has_said_nothing()
    {
        var working = RunView.Start(Guid.CreateVersion7()) with { Status = "running" };

        working.IsThinking().Should().BeTrue();
        (working with { Messages = [Message("assistant", true)] }).IsThinking().Should().BeTrue();
        (working with { Messages = [Message("assistant", true, new PartView("text", "Words"))] }).IsThinking().Should().BeFalse();
        (working with { Status = "completed" }).IsThinking().Should().BeFalse();
    }

    [Fact]
    public void A_bot_is_not_thinking_while_its_tool_runs_or_while_it_waits_for_a_person()
    {
        var working = RunView.Start(Guid.CreateVersion7()) with { Status = "running" };
        var call = new ToolCallView(Guid.CreateVersion7(), "sandbox.run", "Run a command", "{}", "ask", "running");

        (working with { ToolCalls = [call] }).IsThinking().Should().BeFalse();
        (working with { ToolCalls = [call with { State = "completed" }] }).IsThinking().Should().BeTrue("the tool is done and the bot goes on");
        var waiting = working with { Status = "waiting", Reason = "awaiting_approval", ToolCalls = [call with { State = "awaiting_approval" }] };
        waiting.IsThinking().Should().BeFalse();
        waiting.Notice().Should().BeNull("the waiting call's own card asks the question");
        (waiting with { Reason = "awaiting_input" }).Notice().Should().Contain("cannot ask for yet");
    }

    [Fact]
    public void A_call_shows_after_the_words_of_the_reply_that_made_it_and_a_call_not_stored_yet_after_the_runs_newest_message()
    {
        var callId = Guid.CreateVersion7();
        var later = Guid.CreateVersion7();
        var reply = Message("assistant", false, new PartView("text", "Looking."), new PartView("tool_call", ToolCallId: callId, ToolName: "time_now", Arguments: "{}"));
        var result = Message("tool", false, new PartView("tool_result", ToolCallId: callId, Result: """{"utc":"noon"}"""));
        var streaming = Message("assistant", true);
        var run = RunView.Start(Guid.CreateVersion7()) with
        {
            Status = "running",
            Messages = [reply, streaming],
            ToolCalls =
            [
                new(callId, "time.now", "Current time", "{}", "allow", "completed"),
                new(later, "sandbox.run", "Run a command", """{"command":"ls"}""", "allow", "running"),
            ],
        };

        var shown = new[] { Message("user", false, new PartView("text", "Time?")), reply, result, streaming }.ToChatMessages(run.ToolCalls.ToDictionary(tool => tool.ToolCallId), run);

        shown.Select(message => (message.Text, string.Join(",", message.Tools.Select(tool => tool.Label))))
            .Should().Equal(("Time?", ""), ("Looking.", "Checked the time"), ("", "Running a command"));
        shown[1].Tools[0].Result.Should().Contain("noon", "the stored result is shown");
        shown[2].Key.Should().Be(streaming.Id.ToString(), "the call joins the reply being written, which will make it");
    }

    [Fact]
    public void A_failed_run_is_not_a_notice_because_the_conversation_ends_with_its_failure_line()
    {
        var failed = RunView.Start(Guid.CreateVersion7()) with { Status = "failed", Error = new("unauthorized", "The provider rejected the API key.") };

        failed.Notice().Should().BeNull();
    }

    [Fact]
    public void A_stopped_run_is_a_notice_not_a_failure()
    {
        var stopped = RunView.Start(Guid.CreateVersion7()) with { Status = "cancelled" };

        stopped.Notice().Should().Be("You stopped this reply.");
        (stopped with { Status = "completed" }).Notice().Should().BeNull();
    }

    #endregion

    #region Files

    [Fact]
    public void A_messages_files_are_shown_at_the_address_given_and_a_message_with_only_files_is_kept()
    {
        var file = new FileResponse(Guid.NewGuid(), "cat.png", "image/png", FileKinds.Image, 2048, 64, 48);
        var withFile = new MessageView(Guid.NewGuid(), "user", [new PartView("image", File: file)], false, null);

        var shown = new[] { withFile }.ToChatMessages(fileAddress: id => $"/files/{id}").Single();
        var without = new[] { withFile }.ToChatMessages();

        shown.Text.Should().BeEmpty();
        shown.Files.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { file.Id, Name = "cat.png", Href = $"/files/{file.Id}", FromBot = false, Width = 64, Height = 48 });
        without.Should().BeEmpty("without an address there is nothing to show");
    }

    #endregion

    #region What a Reply Used

    [Fact]
    public void What_a_reply_used_is_offered_once_at_the_last_words_of_each_finished_run_and_not_while_it_works()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        MessageView[] stored =
        [
            Message("user", false, new PartView("text", "Plan it")),
            Message("assistant", false, new PartView("text", "Thinking aloud.")) with { RunId = first },
            Message("assistant", false, new PartView("text", "Here is the plan.")) with { RunId = first },
            Message("user", false, new PartView("text", "And now?")),
        ];
        var arriving = Message("assistant", true, new PartView("text", "Working on"));
        var working = RunView.Start(second) with { Status = "running", Messages = [arriving] };

        var reader = new UsageReader(Guid.CreateVersion7(), IsSpaceAdmin: true);
        var whileWorking = stored.Append(arriving).ToChatMessages(run: working, reader: reader);
        var done = stored.Append(arriving with { IsStreaming = false }).ToChatMessages(run: working with { Status = "completed" }, reader: reader);

        whileWorking.Select(message => (message.Text, message.RunId, message.EndsReply)).Should().Equal(
            ("Plan it", null, false),
            ("Thinking aloud.", first, false),
            ("Here is the plan.", first, true),
            ("And now?", null, false),
            ("Working on", second, false));
        done[^1].Should().Match<ChatMessage>(message => message.RunId == second && message.EndsReply);
    }

    [Fact]
    public void In_a_shared_conversation_a_member_is_offered_what_their_own_replies_used_and_an_admin_every_reply()
    {
        var alice = Guid.CreateVersion7();
        var bob = Guid.CreateVersion7();
        var aliceRun = Guid.CreateVersion7();
        var bobRun = Guid.CreateVersion7();
        MessageView[] shared =
        [
            Message("user", false, new PartView("text", "Alice asks")),
            Message("assistant", false, new PartView("text", "Answer for Alice.")) with { RunId = aliceRun, RunPrincipalUserId = alice },
            Message("user", false, new PartView("text", "Bob asks")),
            Message("assistant", false, new PartView("text", "Answer for Bob.")) with { RunId = bobRun, RunPrincipalUserId = bob },
        ];

        static IEnumerable<string> Offered(IReadOnlyList<ChatMessage> shown) => shown.Where(message => message.EndsReply).Select(message => message.Text);

        Offered(shared.ToChatMessages(reader: new UsageReader(bob, IsSpaceAdmin: false))).Should().Equal(
            ["Answer for Bob."], "the server answers a member only for the runs that act for them");
        Offered(shared.ToChatMessages(reader: new UsageReader(alice, IsSpaceAdmin: false))).Should().Equal("Answer for Alice.");
        Offered(shared.ToChatMessages(reader: new UsageReader(Guid.CreateVersion7(), IsSpaceAdmin: true))).Should().Equal("Answer for Alice.", "Answer for Bob.");
        Offered(shared.ToChatMessages()).Should().BeEmpty("without a reader nothing is offered");
    }

    [Fact]
    public void A_messages_versions_are_switched_at_the_message_and_a_replys_at_its_last_words()
    {
        var earlier = Guid.CreateVersion7();
        var otherReply = Guid.CreateVersion7();
        var asked = Message("user", false, new PartView("text", "Sample question, edited."));
        var callId = Guid.CreateVersion7();
        var called = Message("assistant", false, new PartView("tool_call", ToolCallId: callId, ToolName: "sample.look", Arguments: "{}"));
        var answered = Message("assistant", false, new PartView("text", "Sample answer."));
        MessageView[] timeline =
        [
            asked with { Versions = [earlier, asked.Id] },
            called with { Versions = [otherReply, called.Id, Guid.CreateVersion7()] },
            Message("tool", false, new PartView("tool_result", ToolCallId: callId, Result: "{}")),
            answered,
        ];

        var shown = timeline.ToChatMessages();

        shown[0].Versions.Should().Be(new VersionSwitch(2, 2, earlier, null));
        shown.Single(message => message.Key == answered.Id.ToString()).Versions.Should().Be(
            new VersionSwitch(2, 3, otherReply, timeline[1].Versions![2]),
            "the reply's versions are switched where it ends, at its words");
        shown.Single(message => message.Key == called.Id.ToString()).Versions.Should().BeNull();
    }

    [Fact]
    public void Only_the_last_message_offers_an_edit_and_only_its_reply_offers_another_while_nothing_works()
    {
        MessageView[] timeline =
        [
            Message("user", false, new PartView("text", "First")),
            Message("assistant", false, new PartView("text", "First answer.")),
            Message("user", false, new PartView("text", "Second")),
            Message("assistant", false, new PartView("text", "Second answer.")) with { Versions = [Guid.CreateVersion7(), Guid.CreateVersion7()] },
        ];
        timeline[3] = timeline[3] with { Versions = [Guid.CreateVersion7(), timeline[3].Id] };

        var idle = timeline.ToChatMessages().OfferingVersions(idle: true);
        var working = timeline.ToChatMessages().OfferingVersions(idle: false);
        var pending = timeline.Append(Message("user", false, new PartView("text", "Third")) with { IsPending = true }).ToChatMessages().OfferingVersions(idle: true);

        idle.Select(message => (message.MayEdit, message.MayRegenerate)).Should().Equal((false, false), (false, false), (true, false), (false, true));
        idle[3].Versions!.Enabled.Should().BeTrue();
        working.Should().OnlyContain(message => !message.MayEdit && !message.MayRegenerate);
        working[3].Versions!.Enabled.Should().BeFalse("the server does not switch versions while the bot works");
        pending.Should().OnlyContain(message => !message.MayEdit && !message.MayRegenerate, "a message waits to be answered");
    }

    [Fact]
    public void A_conversation_that_ends_with_the_persons_message_offers_its_edit_and_no_other_reply()
    {
        MessageView[] timeline = [Message("user", false, new PartView("text", "Unanswered"))];

        var shown = timeline.ToChatMessages().OfferingVersions(idle: true);

        shown.Single().Should().Match<ChatMessage>(message => message.MayEdit && !message.MayRegenerate);
    }

    #endregion

    #region Private Methods

    private static MessageView Message(string role, bool streaming, params PartView[] parts) =>
        new(Guid.CreateVersion7(), role, [.. parts], streaming, null);

    #endregion
}
