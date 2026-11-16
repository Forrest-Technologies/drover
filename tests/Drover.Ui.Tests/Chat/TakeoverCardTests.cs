namespace Drover.Ui.Tests.Chat;

/// <summary>
/// A bot's request for a person at its screen, as the conversation and the screen beside it show it: moved here with
/// the starter for #150, with what the product added (no screen to open, controls beside the screen, the focus).
/// </summary>
public sealed class TakeoverCardTests : BunitContext
{
    #region Private Fields

    private static readonly Guid callId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000c1");

    #endregion

    #region Constructors

    public TakeoverCardTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddDroverUi();
    }

    #endregion

    #region The Card

    [Fact]
    public void A_person_who_cannot_do_what_the_bot_asked_can_say_why()
    {
        string? told = null;
        var card = Render<TakeoverCard>(set => set
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.Reason, "Sign in to the sample site.")
            .Add(view => view.Phase, TakeoverPhase.Holding)
            .Add(view => view.OnCouldNot, note => told = note));

        card.Find("h3").TextContent.Should().Be("You have Sample bot's screen");
        Button(card, "I could not do it").Click();
        card.Find("textarea").Input("  The site wants a code I do not have.  ");
        card.Find("form").Submit();

        told.Should().Be("The site wants a code I do not have.");
    }

    [Fact]
    public void A_new_phase_of_the_takeover_starts_without_the_last_ones_half_written_answer()
    {
        var card = Render<TakeoverCard>(set => set
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.Reason, "Sign in.")
            .Add(view => view.Phase, TakeoverPhase.Asked));

        Button(card, "I cannot do it").Click();
        card.FindAll("textarea").Should().ContainSingle();

        card.Render(set => set.Add(view => view.Phase, TakeoverPhase.Holding));

        card.FindAll("textarea").Should().BeEmpty();
    }

    [Fact]
    public void The_ask_names_the_bot_and_its_reason_and_both_buttons_are_named_and_described_by_the_title()
    {
        var opened = false;
        var card = Render<TakeoverCard>(set => set
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.Reason, "Sign in to the sample site.")
            .Add(view => view.Phase, TakeoverPhase.Asked)
            .Add(view => view.OnOpenScreen, () => opened = true));

        var title = card.Find("h3");
        title.TextContent.Should().Be("Sample bot needs you at its screen");
        card.Find("section").GetAttribute("aria-labelledby").Should().Be(title.Id);
        card.Find(".dv-takeover-reason").TextContent.Should().Be("Sign in to the sample site.");
        card.Find(".dv-badge").TextContent.Should().Be("Waiting for you");
        card.FindAll("button").Select(button => button.TextContent.Trim()).Should().Equal("Open the screen", "I cannot do it");
        card.FindAll("button").Should().OnlyContain(button => button.GetAttribute("aria-describedby") == title.Id && button.GetAttribute("type") == "button");

        Button(card, "Open the screen").Click();

        opened.Should().BeTrue();
    }

    [Fact]
    public void A_person_who_does_not_use_the_screen_is_told_who_does_and_still_answers_with_neither_answer_pressed_on_them()
    {
        var done = false;
        var card = Render<TakeoverCard>(set => set
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.Reason, "Sign in.")
            .Add(view => view.Phase, TakeoverPhase.Asked)
            .Add(view => view.Access, ScreenAccess.NotPermitted)
            .Add(view => view.OnDone, () => done = true));

        card.Find(".dv-takeover-note").TextContent.Should().Be("Owners and admins of this space use Sample bot's screen. Ask one of them to do it, then tell Sample bot here.");
        card.FindAll("button").Select(button => button.TextContent.Trim()).Should().Equal("It is done", "I cannot do it");
        card.FindAll("button.dv-button-primary").Should().BeEmpty();

        Button(card, "It is done").Click();

        done.Should().BeTrue();
    }

    [Fact]
    public void Until_it_is_known_whether_the_person_can_use_the_screen_no_answer_is_offered()
    {
        var card = Render<TakeoverCard>(set => set
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.Reason, "Sign in.")
            .Add(view => view.Phase, TakeoverPhase.Asked)
            .Add(view => view.Access, ScreenAccess.Unknown));

        card.Find("[role=status]").TextContent.Should().Be("Checking whether you can use Sample bot's screen.");
        card.FindAll("button").Should().BeEmpty();
    }

    [Fact]
    public void A_bot_with_no_screen_now_says_so_links_to_its_settings_and_can_only_be_told_no()
    {
        var card = Render<TakeoverCard>(set => set
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.Reason, "Sign in.")
            .Add(view => view.Phase, TakeoverPhase.Asked)
            .Add(view => view.Access, ScreenAccess.Missing)
            .Add(view => view.SetupHref, "/bots/sample"));

        card.Find(".dv-takeover-note").TextContent.Should().Contain("Sample bot has no screen now, so this cannot be done there.");
        card.Find(".dv-takeover-note a").TextContent.Should().Be("See what it needs.");
        card.Find(".dv-takeover-note a").GetAttribute("href").Should().Be("/bots/sample");
        card.FindAll("button").Select(button => button.TextContent.Trim()).Should().Equal("I cannot do it");
    }

    [Fact]
    public void While_the_screen_is_open_beside_the_conversation_the_card_there_points_to_it_and_has_no_buttons()
    {
        var card = Render<TakeoverCard>(set => set
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.Reason, "Sign in.")
            .Add(view => view.Phase, TakeoverPhase.Holding)
            .Add(view => view.AnswerBesideScreen, true));

        card.Find("h3").TextContent.Should().Be("You have Sample bot's screen");
        card.Find(".dv-takeover-note").TextContent.Should().Be("Use I'm done at the screen when you have finished.");
        card.FindAll("button").Should().BeEmpty();
    }

    [Fact]
    public void Holding_the_screen_says_what_is_kept_and_answers_done_while_nothing_is_on_its_way()
    {
        var done = 0;
        var card = Render<TakeoverCard>(set => set
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.Reason, "Sign in.")
            .Add(view => view.Phase, TakeoverPhase.Holding)
            .Add(view => view.OnDone, () => done++));

        card.Find(".dv-takeover-note").TextContent.Should().Be("What you type and click goes to the screen only and is not recorded. Sample bot sees only what the screen shows when it next looks.");
        Button(card, "I'm done").Click();
        card.Render(set => set.Add(view => view.Sending, true).Add(view => view.Problem, "Your answer was not sent. Drover could not reach its server. Check that it is running."));

        card.FindAll("button").Should().OnlyContain(button => button.HasAttribute("disabled"));
        card.Render(set => set.Add(view => view.Sending, false));
        Button(card, "I could not do it").Click();
        card.Render(set => set.Add(view => view.Sending, true));
        card.FindAll("form button").Select(button => (button.TextContent.Trim(), button.HasAttribute("disabled")))
            .Should().Equal(("Tell Sample bot I could not", true), ("Back", true));
        card.Find("[role=alert]").TextContent.Should().Contain("Your answer was not sent.");
        done.Should().Be(1);
    }

    [Fact]
    public void Taking_the_screen_moves_the_focus_to_im_done()
    {
        var card = Render<TakeoverCard>(set => set
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.Reason, "Sign in.")
            .Add(view => view.Phase, TakeoverPhase.Holding)
            .Add(view => view.TakeFocus, true));

        JSInterop.VerifyFocusAsyncInvoke().Arguments[0].Should().BeOfType<ElementReference>();
        card.FindAll("button").Should().HaveCount(2);
    }

    [Fact]
    public void The_answers_say_what_the_person_did()
    {
        var done = Render<TakeoverCard>(set => set.Add(view => view.BotName, "Sample bot").Add(view => view.Reason, "Sign in.").Add(view => view.Phase, TakeoverPhase.Done));
        var couldNot = Render<TakeoverCard>(set => set.Add(view => view.BotName, "Sample bot").Add(view => view.Reason, "Sign in.").Add(view => view.Phase, TakeoverPhase.CouldNot));

        done.Find("h3").TextContent.Should().Be("You told Sample bot it is done");
        couldNot.Find("h3").TextContent.Should().Be("You told Sample bot you could not do it");
        done.FindAll("button").Should().BeEmpty();
        couldNot.FindAll("button").Should().BeEmpty();
    }

    #endregion

    #region From The Call

    [Theory]
    [InlineData("awaiting_approval", "", TakeoverPhase.Asked)]
    [InlineData("completed", "{\"done\":true}", TakeoverPhase.Done)]
    [InlineData("denied", "", TakeoverPhase.CouldNot)]
    [InlineData("denied", "{\"done\":false,\"note\":\"The person could not do it.\"}", TakeoverPhase.CouldNot)]
    public void A_call_that_asks_for_a_person_is_a_takeover_with_its_reason(string state, string preview, TakeoverPhase phase)
    {
        var card = Handover(state, preview).ToCard();

        card.Takeover.Should().Be(new Takeover(callId, "Sign in with your passkey.", phase));
    }

    [Theory]
    [InlineData("denied", "{\"error\":\"The run ended before anyone answered.\"}")]
    [InlineData("completed", "{\"error\":\"No tool by that name is offered to this bot.\"}")]
    [InlineData("completed", "")]
    [InlineData("cancelled", "")]
    [InlineData("interrupted", "")]
    public void A_request_that_ended_without_the_persons_answer_is_a_plain_tool_card(string state, string preview) =>
        Handover(state, preview).ToCard().Takeover.Should().BeNull();

    [Fact]
    public void Any_other_call_is_not_a_takeover() =>
        new ToolCallView(callId, "computer.click", "computer.click", """{"x":1,"y":2}""", "allow", "awaiting_approval").ToCard().Takeover.Should().BeNull();

    [Fact]
    public void A_waiting_request_in_the_conversation_shows_the_takeover_card_in_place_of_the_approval_card()
    {
        Takeover? opened = null;
        var message = new ChatMessage("m1", false, string.Empty) { Tools = [Handover("awaiting_approval", string.Empty).ToCard()] };
        var list = Render<MessageList>(set => set
            .Add(view => view.Messages, [message])
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.Takeovers, new ScreenTakeovers(null, ScreenAccess.Usable, null, takeover =>
            {
                opened = takeover;
                return Task.FromResult<string?>(null);
            }, null)));

        list.FindAll(".dv-takeover").Should().ContainSingle();
        list.FindAll(".dv-tool-ask").Should().BeEmpty();
        list.FindAll("button").Single(button => button.TextContent.Trim() == "Open the screen").Click();

        opened.Should().NotBeNull();
        opened!.ToolCallId.Should().Be(callId);
    }

    [Fact]
    public void Without_what_a_takeover_needs_the_request_is_a_plain_tool_card()
    {
        var message = new ChatMessage("m1", false, string.Empty) { Tools = [Handover("awaiting_approval", string.Empty).ToCard()] };
        var list = Render<MessageList>(set => set.Add(view => view.Messages, [message]).Add(view => view.BotName, "Sample bot"));

        list.FindAll(".dv-takeover").Should().BeEmpty();
    }

    [Fact]
    public void A_bot_beneath_asking_for_its_own_screen_points_to_its_sandbox_page_and_is_answered_here()
    {
        var card = Render<TakeoverCard>(set => set
            .Add(view => view.BotName, "Helper")
            .Add(view => view.Reason, "Sign in.")
            .Add(view => view.Phase, TakeoverPhase.Asked)
            .Add(view => view.Access, ScreenAccess.Elsewhere)
            .Add(view => view.SetupHref, "/bots/helper/sandbox"));

        card.Find(".dv-takeover-note").TextContent.Should().MatchRegex(@"^\s*Helper works on a screen of its own\. Open it from\s+its sandbox page, then tell Helper here\.\s*$");
        card.Find(".dv-takeover-note a").GetAttribute("href").Should().Be("/bots/helper/sandbox");
        card.FindAll("button").Select(button => button.TextContent.Trim()).Should().Equal("It is done", "I cannot do it");
        card.FindAll("button.dv-button-primary").Should().BeEmpty();
    }

    [Theory]
    [InlineData(ScreenAccess.Usable, "Helper works on a screen of its own.")]
    [InlineData(ScreenAccess.NotPermitted, "Owners and admins of this space use Helper's screen.")]
    public void A_bot_beneaths_request_inside_handed_work_is_a_takeover_named_for_that_bot(ScreenAccess parent, string sentence)
    {
        var helper = Guid.Parse("0196f1a0-0000-7000-8000-0000000000c2");
        var work = new DelegationItem(Guid.NewGuid(), "Sample bot", helper, "Helper", "Read the mail.", DelegationPhase.WaitingForYou, null, DelegationRefusal.None, null, 1)
        {
            Work = [new ChatMessage("w1", false, string.Empty) { Tools = [Handover("awaiting_approval", string.Empty).ToCard()] }],
        };
        var handed = Handover("completed", string.Empty).ToCard() with { Delegation = work };
        var entry = Render<ChatEntry>(set => set
            .Add(view => view.Message, new ChatMessage("m1", false, string.Empty) { Tools = [handed] })
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.Takeovers, new ScreenTakeovers(null, parent, "/bots/sample", null, null)));

        entry.Find(".dv-takeover h3").TextContent.Should().Be("Helper needs you at its screen");
        entry.Find(".dv-takeover-note").TextContent.Trim().Should().StartWith(sentence);
        entry.FindAll(".dv-takeover button").Select(button => button.TextContent.Trim()).Should().Equal("It is done", "I cannot do it");
    }

    [Fact]
    public void In_the_conversation_a_held_screen_is_answered_beside_it()
    {
        var message = new ChatMessage("m1", false, string.Empty) { Tools = [Handover("awaiting_approval", string.Empty).ToCard()] };
        var list = Render<MessageList>(set => set
            .Add(view => view.Messages, [message])
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.Takeovers, new ScreenTakeovers(callId, ScreenAccess.Usable, null, null, null)));

        list.Find(".dv-takeover h3").TextContent.Should().Be("You have Sample bot's screen");
        list.FindAll(".dv-takeover button").Should().BeEmpty();
    }

    #endregion

    #region Helpers

    private static IElement Button(IRenderedComponent<IComponent> component, string text) =>
        component.FindAll("button").First(button => button.TextContent.Trim() == text);

    private static ToolCallView Handover(string state, string preview) =>
        new(callId, "computer_handover", "computer.handover", """{"reason":"Sign in with your passkey."}""", "ask", state, Preview: preview);

    #endregion
}
