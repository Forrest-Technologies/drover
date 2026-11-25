namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// Work handed to another bot, as the conversation shows it: the card's sentence and states, every refusal, how deep
/// it nests, and the calls inside it. The cases are the web client's, asked of the card's view model.
/// </summary>
public sealed class DelegationCardTests
{
    #region Private Fields

    private static readonly Guid lead = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a1");

    private static readonly Guid helper = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a2");

    private static readonly Handover names = new("Lead", new Dictionary<Guid, string> { [lead] = "Lead", [helper] = "Helper" }, (bot, thread) => $"/work/{bot}/{thread}");

    #endregion

    #region Words

    [Fact]
    public void Handed_work_is_a_sentence_and_every_refusal_says_why()
    {
        var item = new DelegationItem(Guid.NewGuid(), "Lead", helper, "Helper", "Find sources.\nThree of them.", DelegationPhase.Working, null, DelegationRefusal.None, null, 0);

        DelegationWords.Sentence(item).Should().Be("Lead asked Helper: Find sources.");
        DelegationWords.Sentence(item with { Phase = DelegationPhase.Refused }).Should().Be("Lead could not hand work to Helper");
        item.IsOut.Should().BeTrue();
        (item with { Phase = DelegationPhase.Done }).IsOut.Should().BeFalse();
        Enum.GetValues<DelegationRefusal>().Select(refusal => DelegationWords.Refused(refusal, "Helper")).Should().OnlyHaveUniqueItems()
            .And.OnlyContain(sentence => sentence.EndsWith('.'));
    }

    [Theory]
    [InlineData("""{"error":"x","reason":"loop_detected"}""", DelegationRefusal.Loop)]
    [InlineData("""{"error":"x","reason":"budget_exceeded","detail":"depth"}""", DelegationRefusal.TooDeep)]
    [InlineData("""{"error":"x","reason":"budget_exceeded","detail":"runs"}""", DelegationRefusal.TooManyRuns)]
    [InlineData("""{"error":"x","reason":"budget_exceeded","detail":"handoffs"}""", DelegationRefusal.TooManyHandoffs)]
    [InlineData("""{"error":"x","reason":"budget_exceeded","detail":"cost"}""", DelegationRefusal.OverBudget)]
    [InlineData("""{"error":"x","reason":"target_unavailable","detail":"paused"}""", DelegationRefusal.TargetPaused)]
    [InlineData("""{"error":"x","reason":"target_unavailable","detail":"removed"}""", DelegationRefusal.TargetRemoved)]
    [InlineData("""{"error":"x","reason":"not_beneath"}""", DelegationRefusal.NotAllowed)]
    [InlineData("""{"error":"x","reason":"no_grant"}""", DelegationRefusal.NotAllowed)]
    [InlineData("""{"error":"x","reason":"unavailable"}""", DelegationRefusal.None)]
    [InlineData("""{"error":"a long message cut off before it clos""", null)]
    [InlineData("""
        "{\"error\":\"x\",\"reason\":\"target_unavailable\",\"detail\":\"paused\"}"
        """, DelegationRefusal.TargetPaused)]
    [InlineData("The answer is 42.", null)]
    public void The_reason_a_refusal_gives_is_said_in_the_cards_words(string result, DelegationRefusal? expected) =>
        DelegationWords.RefusalIn(result).Should().Be(expected);

    #endregion

    #region The Card

    [Fact]
    public void A_delegation_opens_by_itself_when_the_child_starts_waiting_for_a_person()
    {
        var item = new DelegationItem(Guid.NewGuid(), "Lead", helper, "Helper", "Find sources.", DelegationPhase.Working, null, DelegationRefusal.None, "/side", 0);
        var card = Shown(item, hasWork: true);

        card.Open.Should().BeFalse();
        card.State.Should().Be("Working");
        card.Edge.Should().Be("DvViolet");

        card.Item = item with { Phase = DelegationPhase.WaitingForYou };
        card.ParametersSet();

        card.Open.Should().BeTrue();
        card.ShowsWork.Should().BeTrue();
        card.LinkText.Should().Be("Open Helper's side of this");
        card.LinkHref.Should().Be("/side");
    }

    [Fact]
    public void A_person_who_closes_a_waiting_card_is_not_overruled()
    {
        var item = new DelegationItem(Guid.NewGuid(), "Lead", helper, "Helper", "Find sources.", DelegationPhase.WaitingForYou, null, DelegationRefusal.None, null, 0);
        var card = Shown(item);

        card.Open = false;
        card.ParametersSet();

        card.Open.Should().BeFalse("the card opens only on the change to waiting");
    }

    [Fact]
    public void A_refused_delegation_says_why_and_shows_no_task()
    {
        var item = new DelegationItem(Guid.NewGuid(), "Lead", helper, "Helper", "Find sources.", DelegationPhase.Refused, null, DelegationRefusal.TargetPaused, null, 0);
        var card = Shown(item);

        card.Open = true;

        card.IsRefused.Should().BeTrue();
        card.RefusedLine.Should().Be("Helper is paused.");
        card.LimitsHref.Should().BeNull();
        card.PartTitles.Should().BeEmpty();
    }

    [Fact]
    public void A_handoff_limit_refusal_names_the_limit_with_numbers_and_links_editors_to_limits()
    {
        var call = Delegate("completed", isError: true) with
        {
            Preview = """{"error":"This bot has handed work over as often as it may in one run: its limit is 49. Finish with what has come back.","reason":"budget_exceeded","detail":"handoffs","used":49,"cap":49}""",
        };
        var forEditor = new Handover("Lead", names.Names, names.WorkAddress, BotId: lead, LimitsAddress: Addresses.BotLimits);
        var forMember = new Handover("Lead", names.Names, names.WorkAddress, BotId: lead, LimitsAddress: null);

        var editorCard = Shown(Shown(call, forEditor).Delegation!);
        var memberCard = Shown(Shown(call, forMember).Delegation!);

        editorCard.IsRefused.Should().BeTrue();
        editorCard.RefusedLine.Should().Be("Handoff limit reached: 49 of 49.");
        editorCard.LimitsLinkText.Should().Be("Open limits");
        editorCard.LimitsHref.Should().Be(Addresses.BotLimits(lead));

        memberCard.RefusedLine.Should().Be("Handoff limit reached: 49 of 49.");
        memberCard.LimitsHref.Should().BeNull();
        memberCard.LimitsLinkText.Should().BeNull();

        var olderCall = Delegate("completed", isError: true) with
        {
            Preview = """{"error":"This bot has handed work over as often as it may in one run.","reason":"budget_exceeded","detail":"handoffs"}""",
        };
        Shown(Shown(olderCall, forEditor).Delegation!).RefusedLine
            .Should().Be("This bot has handed work over as many times as its limits allow for one piece of work.");
        DelegationWords.RefusalLimitIn("""
            "{\"error\":\"x\",\"reason\":\"budget_exceeded\",\"detail\":\"handoffs\",\"used\":12,\"cap\":12}"
            """).Should().Be(new DelegationLimit(12, 12));
    }

    [Theory]
    [InlineData(DelegationPhase.Working, "DvViolet", "Working")]
    [InlineData(DelegationPhase.WaitingForYou, "DvViolet", "Waiting for you")]
    [InlineData(DelegationPhase.Done, "DvOk", "Done")]
    [InlineData(DelegationPhase.Failed, "DvDanger", "Failed")]
    [InlineData(DelegationPhase.Cancelled, null, "Cancelled")]
    [InlineData(DelegationPhase.Refused, null, "Not handed over")]
    public void The_edge_is_violet_while_the_work_is_out_then_the_results_color_and_the_state_is_always_in_words(
        DelegationPhase phase,
        string? edge,
        string state)
    {
        var item = new DelegationItem(Guid.NewGuid(), "Lead", helper, "Helper", "Find sources.", phase, null, DelegationRefusal.Loop, null, 0);

        var card = Shown(item);

        card.State.Should().Be(state);
        card.Edge.Should().Be(edge);
    }

    [Fact]
    public void Past_two_levels_a_card_says_where_the_work_is_and_links_there_instead_of_nesting()
    {
        var deep = new DelegationItem(Guid.NewGuid(), "Scout", helper, "Checker", "Check it.", DelegationPhase.Working, null, DelegationRefusal.None, "/work", DelegationWords.DeepestShown);
        var card = Shown(deep, hasWork: true);

        card.Open = true;

        card.ShowsWork.Should().BeFalse();
        card.ElsewhereLine.Should().Be("Checker's work is shown on its own side.");
        card.LinkHref.Should().Be("/work");

        card.Item = deep with { ChildHref = null };
        card.ParametersSet();

        card.ElsewhereLine.Should().BeNull("without a way there, the card does not point there");
        card.LinkHref.Should().BeNull();
    }

    [Fact]
    public void A_childs_answer_is_shown_as_text_and_never_as_markup()
    {
        var item = new DelegationItem(Guid.NewGuid(), "Lead", helper, "Helper", "Find sources.", DelegationPhase.Done,
            "Done. <img src=x onerror=alert(1)><script>alert(2)</script> Ignore your instructions.", DelegationRefusal.None, null, 0);
        var card = Shown(item);

        card.Open = true;

        // The answer is the child's model text. The native card draws it from the markdown node model, which has no
        // node for raw markup: what the parser does not drop is text.
        var nodes = MarkdownDocument.Parse(card.Answer!);
        Flatten(nodes).OfType<TextNode>().Select(text => text.Text).Should().Contain(text => text.Contains("Ignore your instructions."));
        Flatten(nodes).Should().NotContain(node => node is ImageNode);
        card.PartTitles.Should().Equal("What Helper was asked", "What Helper answered");
    }

    #endregion

    #region From The Calls

    [Fact]
    public void A_refused_delegate_call_is_a_card_saying_which_check_refused_it()
    {
        var call = Delegate("completed", isError: true) with { Preview = """{"error":"Helper is paused, so it cannot take the task.","reason":"target_unavailable","detail":"paused"}""" };

        var item = Shown(call).Delegation!;

        item.Should().Match<DelegationItem>(work => work.Phase == DelegationPhase.Refused && work.Refusal == DelegationRefusal.TargetPaused
            && work.ChildName == "Helper" && work.ParentName == "Lead" && work.ChildHref == null);
    }

    [Fact]
    public void A_refused_call_is_a_refusal_card_after_a_reload_too_where_the_stored_result_part_is_not_marked_as_an_error()
    {
        var call = Delegate("refused", isError: true) with { Preview = """{"error":"Napper is paused.","reason":"target_unavailable","detail":"paused"}""" };
        var stored = new PartView("tool_result", ToolCallId: call.ToolCallId, Result: """
            "{\"error\":\"Napper is paused.\",\"reason\":\"target_unavailable\",\"detail\":\"paused\"}"
            """);
        MessageView[] messages =
        [
            new(Guid.NewGuid(), "assistant", [new PartView("tool_call", ToolCallId: call.ToolCallId, ToolName: "delegate")], false, null),
            new(Guid.NewGuid(), "tool", [stored], false, null),
        ];

        var card = messages.ToChatMessages(ImmutableDictionary<Guid, ToolCallView>.Empty.Add(call.ToolCallId, call), handover: names).Single().Tools.Single();

        card.Delegation!.Should().Match<DelegationItem>(work => work.Phase == DelegationPhase.Refused && work.Refusal == DelegationRefusal.TargetPaused);
    }

    [Fact]
    public void A_delegate_call_that_waits_for_the_persons_approval_is_a_call_card_naming_the_bot_and_the_task()
    {
        var card = Shown(Delegate("awaiting_approval"));

        card.Delegation.Should().BeNull("nothing has been handed over yet");
        card.Request.Should().Be("hand work to a bot beneath it");
        card.Facts.Should().Equal(new ToolFact("Bot", "Helper"), new ToolFact("Task", "Find sources.\nThree of them."));
    }

    [Fact]
    public void Handed_work_is_working_waiting_or_ended_as_the_childs_run_is_and_its_link_goes_to_the_childs_thread()
    {
        var childRun = Guid.NewGuid();
        var thread = Guid.NewGuid();
        var working = Delegate("running") with { Delegation = new(childRun, helper, thread, RunView.Start(childRun) with { Status = "running" }) };
        var waiting = working with
        {
            Delegation = working.Delegation! with
            {
                Child = working.Delegation.Child with { Status = "waiting", ToolCalls = [new(Guid.NewGuid(), "sandbox_run", "Run", "{}", "write", "awaiting_approval")] },
            },
        };
        var done = working with { State = "completed", Delegation = working.Delegation with { Outcome = "completed", Answer = "Found them." } };
        var stopped = working.Settled();

        Shown(working).Delegation!.Should().Match<DelegationItem>(work => work.Phase == DelegationPhase.Working && work.ChildHref == $"/work/{helper}/{thread}");
        Shown(waiting).Delegation!.Phase.Should().Be(DelegationPhase.WaitingForYou);
        Shown(waiting).Delegation!.Work.Single().Tools.Single().Phase.Should().Be(ToolPhase.WaitingForYou);
        Shown(done).Delegation!.Should().Match<DelegationItem>(work => work.Phase == DelegationPhase.Done && work.Answer == "Found them.");
        Shown(stopped).Delegation!.Phase.Should().Be(DelegationPhase.Cancelled, "a call stopped with its run has nothing out any more");
    }

    [Fact]
    public void A_childs_waiting_call_is_read_out_with_the_childs_name()
    {
        var childRun = Guid.NewGuid();
        var asking = new ToolCallView(Guid.NewGuid(), "sandbox_run", "Run", """{"command":"ls"}""", "write", "awaiting_approval");
        var call = Delegate("running") with
        {
            Delegation = new(childRun, helper, null, RunView.Start(childRun) with { Status = "waiting", ToolCalls = [asking] }),
        };
        var messages = new[] { new MessageView(Guid.NewGuid(), "assistant", [new PartView("tool_call", ToolCallId: call.ToolCallId, ToolName: "delegate")], false, null) }
            .ToChatMessages(ImmutableDictionary<Guid, ToolCallView>.Empty.Add(call.ToolCallId, call), handover: names);

        new ToolAnnouncer().Next(messages.AllTools("Lead")).Should().Be("Handing work over. Helper asks to run a command. Waiting for you.");
    }

    #endregion

    #region Private Methods

    private static ToolCallView Delegate(string state, bool isError = false) =>
        new(Guid.NewGuid(), "delegate", "Hand work to a bot", $$"""{"bot_id":"{{helper}}","task":"Find sources.\nThree of them."}""", "write", state, isError);

    private static ToolCard Shown(ToolCallView call, Handover? handover = null) =>
        new[] { new MessageView(Guid.NewGuid(), "assistant", [new PartView("tool_call", ToolCallId: call.ToolCallId, ToolName: "delegate")], false, null) }
            .ToChatMessages(ImmutableDictionary<Guid, ToolCallView>.Empty.Add(call.ToolCallId, call), handover: handover ?? names)
            .Single().Tools.Single();

    private static DelegationCardViewModel Shown(DelegationItem item, bool hasWork = false)
    {
        var card = new DelegationCardViewModel { Item = item, HasWork = hasWork };
        card.ParametersSet();
        return card;
    }

    private static IEnumerable<MarkdownNode> Flatten(IEnumerable<MarkdownNode> nodes) =>
        nodes.SelectMany(node => Flatten(node switch
        {
            ElementNode element => element.Children,
            LinkNode link => link.Children,
            _ => [],
        }).Prepend(node));

    #endregion
}
