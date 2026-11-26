namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// Covers narrow detection and presentation of raw tool-call protocol envelopes so unusable model output never looks
/// like a completed answer or executed tool call while ordinary prose, quoted examples, valid tool activity, streaming,
/// copy, and activity diagnostics keep working.
/// </summary>
public sealed class UnusableToolRequestsTests
{
    #region Private Fields

    private const string FullWidthEnvelope = """
        <｜DSML｜calls>
        <｜DSML｜invoke name="sandbox.run">
        <｜DSML｜parameter name="command" string="true">ls -la && echo "a > b" | head -n 5</｜DSML｜parameter>
        </｜DSML｜invoke>
        </｜DSML｜calls>
        """;

    private const string AsciiFunctionCallsEnvelope = """
        <|DSML|function_calls>
        <|DSML|invoke name="time.now"></|DSML|invoke>
        </|DSML|function_calls>
        """;

    private const string QuotedCodeSample = """
        Here is a DSML example:

        ```xml
        <｜DSML｜calls>
        <｜DSML｜invoke name="sandbox.run">
        <｜DSML｜parameter name="command" string="true">ls</｜DSML｜parameter>
        </｜DSML｜invoke>
        </｜DSML｜calls>
        ```
        """;

    #endregion

    #region Detection

    [Theory]
    [InlineData(FullWidthEnvelope)]
    [InlineData(AsciiFunctionCallsEnvelope)]
    [InlineData("  <｜DSML｜calls><｜DSML｜invoke name=\"time.now\" /></｜DSML｜calls>  ")]
    [InlineData("<｜DSML｜calls><｜DSML｜invoke name=\"a\"><｜DSML｜parameter name=\"x\" /></｜DSML｜invoke></｜DSML｜calls>")]
    [InlineData("<｜DSML｜calls><｜DSML｜invoke name=\"a\"></｜DSML｜invoke></｜DSML｜calls>\n<｜DSML｜calls><｜DSML｜invoke name=\"b\"></｜DSML｜invoke></｜DSML｜calls>")]
    public void Complete_dsml_protocol_envelopes_are_recognized(string text) =>
        UnusableToolRequests.IsProtocolEnvelope(text).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("DSML uses <｜DSML｜calls> and <｜DSML｜invoke> markers.")]
    [InlineData(QuotedCodeSample)]
    [InlineData("```xml\n<｜DSML｜calls><｜DSML｜invoke name=\"sandbox.run\"></｜DSML｜invoke></｜DSML｜calls>\n```")]
    [InlineData("`<｜DSML｜calls><｜DSML｜invoke name=\"sandbox.run\"></｜DSML｜invoke></｜DSML｜calls>`")]
    [InlineData("> <｜DSML｜calls><｜DSML｜invoke name=\"sandbox.run\"></｜DSML｜invoke></｜DSML｜calls>")]
    [InlineData("\"<｜DSML｜calls><｜DSML｜invoke name=\"sandbox.run\"></｜DSML｜invoke></｜DSML｜calls>\"")]
    [InlineData("<｜DSML｜calls><｜DSML｜invoke name=\"sandbox.run\"></｜DSML｜invoke></｜DSML｜calls>\nThat is the raw format.")]
    [InlineData("<｜DSML｜calls></｜DSML｜calls>")]
    [InlineData("<｜DSML｜calls><｜DSML｜invoke name=\"sandbox.run\"><｜DSML｜parameter name=\"command\">ls")]
    [InlineData("<｜DSML｜calls><｜DSML｜invoke name=\"sandbox.run\"></｜DSML｜invoke></｜DSML｜function_calls>")]
    [InlineData("<｜DSML｜calls>ordinary prose<｜DSML｜invoke name=\"sandbox.run\"></｜DSML｜invoke></｜DSML｜calls>")]
    public void Ordinary_prose_quoted_examples_and_incomplete_markup_are_not_marked_unusable(string? text) =>
        UnusableToolRequests.IsProtocolEnvelope(text).Should().BeFalse();

    #endregion

    #region Presentation

    [Fact]
    public void A_completed_protocol_only_reply_is_marked_unusable_without_becoming_a_tool_call()
    {
        var runId = Guid.CreateVersion7();
        var asked = Message("user", false, new PartView("text", "List files"));
        var reply = Message("assistant", false, new PartView("text", FullWidthEnvelope)) with
        {
            RunId = runId,
            RunPrincipalUserId = Guid.CreateVersion7(),
        };

        var reader = new UsageReader(reply.RunPrincipalUserId!.Value, IsSpaceAdmin: false);
        var shown = new[] { asked, reply }.ToChatMessages(reader: reader).OfferingVersions(idle: true);
        var entry = shown[1];

        entry.UnusableToolRequest.Should().BeTrue();
        entry.Tools.Should().BeEmpty("raw text never becomes an executable tool call");
        entry.Text.Should().Be(FullWidthEnvelope, "original content stays intact for copy and inspection");
        entry.MayRegenerate.Should().BeTrue();
        ChatRows.EntryName(entry, "Sample bot", null).Should().Be("Sample bot returned an unusable tool request");
        ChatRows.OffersCopy(entry).Should().BeTrue("copy access to original content is preserved");
        ChatRows.OffersUnusableRetry(entry, canAsk: true).Should().BeTrue();
        ChatRows.OffersUnusableRetry(entry, canAsk: false).Should().BeFalse();
        ChatRows.FooterParts(entry, canRegenerate: true, canSwitch: true, canReadUsage: true)
            .Should().Equal(FooterPart.Copy, FooterPart.Usage);
    }

    [Fact]
    public void Streamed_chunked_protocol_envelope_preserves_streaming_and_marks_unusable_on_completion_replay_and_reload()
    {
        var runId = Guid.CreateVersion7();
        var messageId = Guid.CreateVersion7();
        var threadId = Guid.CreateVersion7();
        var asked = new MessageResponse(
            Guid.CreateVersion7(),
            threadId,
            1,
            "user",
            null,
            [new("text") { Text = "List files" }],
            DateTimeOffset.UnixEpoch);
        var completedMessage = new MessageResponse(
            messageId,
            threadId,
            2,
            "assistant",
            runId,
            [new("text") { Text = FullWidthEnvelope }],
            DateTimeOffset.UnixEpoch);

        RunEventResponse[] streamEvents =
        [
            Event(1, runId, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)),
            Event(2, runId, RunEventTypes.StepStarted, new StepChanged(1, messageId)),
            Event(3, runId, RunEventTypes.MessageDelta, new MessageDelta(messageId, 0, "text", "<\uFF5CDSML\uFF5Ccalls>\n<\uFF5CDSML\uFF5Cinvoke ")),
            Event(4, runId, RunEventTypes.MessageDelta, new MessageDelta(messageId, 0, "text", "name=\"sandbox.run\">\n<\uFF5CDSML\uFF5Cparameter name=\"command\" string=\"true\">ls -la && echo \"a > b\" | head -n 5")),
            Event(5, runId, RunEventTypes.MessageDelta, new MessageDelta(messageId, 0, "text", "</\uFF5CDSML\uFF5Cparameter>\n</\uFF5CDSML\uFF5Cinvoke>\n</\uFF5CDSML\uFF5Ccalls>")),
        ];

        var streamingRun = RunReducer.Apply(RunView.Start(runId), streamEvents);
        var whileStreaming = ChatTimeline.Merge([asked], streamingRun).ToChatMessages(run: streamingRun);

        streamingRun.ToolCalls.Should().BeEmpty();
        whileStreaming[1].Streaming.Should().BeTrue();
        whileStreaming[1].UnusableToolRequest.Should().BeFalse("streaming text is not reclassified before completion");

        RunEventResponse[] completionEvents =
        [
            Event(6, runId, RunEventTypes.MessageCompleted, new MessageCompleted(completedMessage)),
            Event(7, runId, RunEventTypes.RunCompleted, new RunStatusChanged("completed", null, null, 1, null)),
        ];

        var finishedRun = RunReducer.Apply(streamingRun, completionEvents);
        var completed = ChatTimeline.Merge([asked], finishedRun).ToChatMessages(run: finishedRun).OfferingVersions(idle: true);
        var replayedRun = RunReducer.Apply(RunView.Start(runId), [.. streamEvents, .. completionEvents]);
        var replayed = ChatTimeline.Merge([asked], replayedRun).ToChatMessages(run: replayedRun).OfferingVersions(idle: true);
        var reloaded = ChatTimeline.Merge([asked, completedMessage], null).ToChatMessages().OfferingVersions(idle: true);

        IReadOnlyList<ChatMessage>[] views = [completed, replayed, reloaded];

        foreach (var conversation in views)
        {
            var reply = conversation[1];
            reply.Streaming.Should().BeFalse();
            reply.UnusableToolRequest.Should().BeTrue();
            reply.Tools.Should().BeEmpty();
            reply.Text.Should().Be(FullWidthEnvelope);
            reply.MayRegenerate.Should().BeTrue();
        }

        var tree = RunTreeReducer.Apply(RunTreeView.Start(runId, Guid.CreateVersion7()), [.. streamEvents, .. completionEvents]);
        var step = tree.Moments.Single(moment => moment.Kind is RunMomentKind.Step);
        RunTimelines.TextOf(tree, step).Should().Be(FullWidthEnvelope, "activity keeps the original response for diagnostics");
    }

    [Fact]
    public void Valid_tool_activity_reasoning_and_explanatory_prose_retain_their_behavior()
    {
        var runId = Guid.CreateVersion7();
        var callId = Guid.CreateVersion7();
        var step1 = Message(
            "assistant",
            false,
            new PartView("tool_call", ToolCallId: callId, ToolName: "time.now", Arguments: "{}")) with
        {
            RunId = runId,
        };
        var result = Message("tool", false, new PartView("tool_result", ToolCallId: callId, Result: "\"12:00\""));
        var step2 = Message(
            "assistant",
            false,
            new PartView("reasoning", "Trying to call another tool."),
            new PartView("text", FullWidthEnvelope)) with
        {
            RunId = runId,
        };
        var quotedReply = Message("assistant", false, new PartView("text", QuotedCodeSample));
        var personEnvelope = Message("user", false, new PartView("text", FullWidthEnvelope));

        var turn = new[] { step1, result, step2 }.ToChatMessages().InTurns();

        turn[0].UnusableToolRequest.Should().BeFalse();
        turn[0].Ledger.Should().NotBeNull();
        turn[1].ContinuesTurn.Should().BeTrue();
        turn[1].UnusableToolRequest.Should().BeTrue();
        turn[1].Reasoning.Should().Be("Trying to call another tool.");

        var normal = new[] { personEnvelope, quotedReply }.ToChatMessages();
        normal[0].UnusableToolRequest.Should().BeFalse("a person's message is never marked as a bot's unusable reply");
        normal[1].UnusableToolRequest.Should().BeFalse("legitimate prose and code examples are never censored");
        ChatRows.EntryName(normal[1], "Sample bot", null).Should().Be("Sample bot said");
    }

    [Fact]
    public void Unusable_reply_announces_plain_words_stays_searchable_and_is_not_promoted_as_a_delegation_answer()
    {
        var key = Guid.CreateVersion7().ToString();
        var list = new MessageListViewModel
        {
            BotName = "Sample bot",
            OnRegenerate = _ => Task.CompletedTask,
            Messages = [new(key, false, FullWidthEnvelope, Streaming: true)],
        };
        list.Update();

        list.Messages = [new(key, false, FullWidthEnvelope, Streaming: false) { UnusableToolRequest = true, MayRegenerate = true }];
        list.Update();

        list.Spoken.Should().Be("Sample bot's model returned a tool request that could not be used. No tool ran. Try again to ask for another reply.");

        var findable = new FindableText();
        findable.Of(list.Messages[0]).Should().ContainSingle(segment => segment.Text == FullWidthEnvelope);

        var childRunId = Guid.CreateVersion7();
        var childBotId = Guid.CreateVersion7();
        var callId = Guid.CreateVersion7();
        var childReply = Message("assistant", false, new PartView("text", FullWidthEnvelope)) with { RunId = childRunId };
        var childRun = RunView.Start(childRunId) with { Status = "completed", Messages = [childReply] };
        var call = new ToolCallView(callId, "delegate", "Delegate", $"{{\"bot_id\":\"{childBotId}\",\"task\":\"Check files\"}}", "allow", "completed")
        {
            Delegation = new(childRunId, childBotId, Guid.CreateVersion7(), childRun)
            {
                Outcome = "completed",
                Answer = FullWidthEnvelope,
            },
        };
        var parentMessage = Message("assistant", false, new PartView("tool_call", ToolCallId: callId, ToolName: "delegate", Arguments: call.Arguments));
        var handover = new Handover("Parent bot", new Dictionary<Guid, string> { [childBotId] = "Child bot" });

        var shown = new[] { parentMessage }.ToChatMessages(new Dictionary<Guid, ToolCallView> { [callId] = call }, handover: handover);
        var delegation = shown.Single().Tools.Single().Delegation!;

        delegation.Work.Should().ContainSingle(child => child.UnusableToolRequest, "an unusable child reply stays in the work view instead of being promoted as a clean answer");
    }

    #endregion

    #region Private Methods

    private static MessageView Message(string role, bool streaming, params PartView[] parts) =>
        new(Guid.CreateVersion7(), role, [.. parts], streaming, null);

    private static RunEventResponse Event<T>(long seq, Guid runId, string type, T payload)
        where T : notnull =>
        new(seq, runId, type, RunEventTypes.Write(payload), DateTimeOffset.UnixEpoch);

    #endregion
}
