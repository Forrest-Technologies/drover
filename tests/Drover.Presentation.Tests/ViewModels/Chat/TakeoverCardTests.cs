namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// A bot's request for a person at its screen, as the conversation and the screen beside it show it. The cases are the
/// web client's, asked of the card's view model and of the one that sends its steps.
/// </summary>
public sealed class TakeoverCardTests
{
    #region Private Fields

    private static readonly Guid callId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000c1");

    #endregion

    #region The Card

    [Fact]
    public void A_person_who_cannot_do_what_the_bot_asked_can_say_why()
    {
        string? told = null;
        var card = Card(TakeoverPhase.Holding, reason: "Sign in to the sample site.");
        card.CouldNot += note => told = note;

        card.Title.Should().Be("You have Sample bot's screen");
        card.Press(Step(card, "I could not do it"));
        card.ShowsDecline.Should().BeTrue();
        card.DeclineLabel.Should().Be("Reason (optional, Sample bot reads it)");
        card.Note = "  The site wants a code I do not have.  ";
        card.Decline();

        told.Should().Be("The site wants a code I do not have.");
        card.ShowsDecline.Should().BeFalse();
    }

    [Fact]
    public void A_new_phase_of_the_takeover_starts_without_the_last_ones_half_written_answer()
    {
        var card = Card(TakeoverPhase.Asked);

        card.Press(Step(card, "I cannot do it"));
        card.Note = "Half an answer";
        card.ShowsDecline.Should().BeTrue();

        card.Phase = TakeoverPhase.Holding;
        card.ParametersSet();

        card.ShowsDecline.Should().BeFalse();
        card.Note.Should().BeEmpty();
    }

    [Fact]
    public void The_ask_names_the_bot_and_its_reason_and_both_buttons_are_named()
    {
        var opened = false;
        var card = Card(TakeoverPhase.Asked, reason: "Sign in to the sample site.");
        card.OpenScreen += () => opened = true;

        card.Title.Should().Be("Sample bot asks you to take over its screen");
        card.Reason.Should().Be("Sign in to the sample site.");
        card.State.Should().Be("Waiting for you");
        card.StateKind.Should().Be(BadgeKind.Warn);
        card.Edge.Should().Be("DvWarn");
        card.Actions.Select(action => action.Label).Should().Equal("Open the screen", "I cannot do it");
        card.Actions.Select(action => action.Primary).Should().Equal(true, false);

        card.Press(Step(card, "Open the screen"));

        opened.Should().BeTrue();
    }

    [Fact]
    public void A_person_who_does_not_use_the_screen_is_told_who_does_and_still_answers_with_neither_answer_pressed_on_them()
    {
        var done = false;
        var card = Card(TakeoverPhase.Asked, ScreenAccess.NotPermitted);
        card.Done += () => done = true;

        card.NoteText.Should().Be("Owners and admins of this space use Sample bot's screen. Ask one of them to do it, then tell Sample bot here.");
        card.Actions.Select(action => action.Label).Should().Equal("It is done", "I cannot do it");
        card.Actions.Should().NotContain(action => action.Primary);

        card.Press(Step(card, "It is done"));

        done.Should().BeTrue();
    }

    [Fact]
    public void Until_it_is_known_whether_the_person_can_use_the_screen_no_answer_is_offered()
    {
        var card = Card(TakeoverPhase.Asked, ScreenAccess.Unknown);

        card.NoteIsStatus.Should().BeTrue();
        card.NoteText.Should().Be("Checking whether you can use Sample bot's screen.");
        card.Actions.Should().BeEmpty();
    }

    [Fact]
    public void A_bot_with_no_screen_now_says_so_links_to_its_settings_and_can_only_be_told_no()
    {
        var card = Card(TakeoverPhase.Asked, ScreenAccess.Missing, setupHref: "/bots/sample");

        card.NoteText.Should().Contain("Sample bot has no screen now, so this cannot be done there.");
        card.NoteWords.Should().ContainSingle(words => words.Href != null).Which.Should().Be(new TakeoverWords("Read its request.", "/bots/sample"));
        card.Actions.Select(action => action.Label).Should().Equal("I cannot do it");
    }

    [Fact]
    public void While_the_screen_is_open_beside_the_conversation_the_card_there_points_to_it_and_has_no_buttons()
    {
        var card = Card(TakeoverPhase.Holding);
        card.AnswerBesideScreen = true;
        card.ParametersSet();

        card.Title.Should().Be("You have Sample bot's screen");
        card.NoteText.Should().Be("Use I'm done at the screen when you have finished.");
        card.Actions.Should().BeEmpty();
    }

    [Fact]
    public void Holding_the_screen_says_what_is_kept_and_answers_done_while_nothing_is_on_its_way()
    {
        var done = 0;
        var card = Card(TakeoverPhase.Holding);
        card.Done += () => done++;

        card.NoteText.Should().Be("Drover records nothing you type or click, and no bot can start anything new on this screen while you hold it. Sample bot sees the screen again when it next looks.");
        card.State.Should().Be("Sample bot is waiting");
        card.Press(Step(card, "I'm done"));
        card.Sending = true;
        card.Problem = "Your answer was not sent. Drover could not reach its server. Check that it is running.";
        card.ParametersSet();

        card.Sending.Should().BeTrue("every button is off while an answer is on its way");
        card.Sending = false;
        card.ParametersSet();
        card.Press(Step(card, "I could not do it"));
        card.Sending = true;
        card.ParametersSet();
        card.ShowsDecline.Should().BeTrue("the same phase keeps what the person was writing");
        (card.DeclineSubmit, TakeoverCardViewModel.Back).Should().Be(("Tell Sample bot I could not", "Back"));
        card.Problem.Should().Contain("Your answer was not sent.");
        done.Should().Be(1);
    }

    [Fact]
    public void Taking_the_screen_moves_the_focus_to_im_done()
    {
        var card = new TakeoverCardViewModel { BotName = "Sample bot", Reason = "Sign in.", Phase = TakeoverPhase.Holding, TakeFocus = true };
        card.ParametersSet();

        card.TakeFocusForDone().Should().BeTrue();
        card.TakeFocusForDone().Should().BeFalse("once");
        card.Actions.Should().HaveCount(2);
    }

    [Fact]
    public void A_card_that_was_not_asked_to_take_the_focus_leaves_it_where_it_is()
    {
        var card = Card(TakeoverPhase.Holding);

        card.TakeFocusForDone().Should().BeFalse();
    }

    [Fact]
    public void The_answers_say_what_the_person_did()
    {
        var done = Card(TakeoverPhase.Done);
        var couldNot = Card(TakeoverPhase.CouldNot);

        done.Title.Should().Be("You told Sample bot it is done");
        couldNot.Title.Should().Be("You told Sample bot you could not do it");
        (done.State, couldNot.State).Should().Be(("Done", "Not done"));
        done.Actions.Should().BeEmpty();
        couldNot.Actions.Should().BeEmpty();
        done.Edge.Should().BeNull("an answered request no longer waits");
    }

    [Fact]
    public void A_bot_beneath_asking_for_its_own_screen_points_to_its_sandbox_page_and_is_answered_here()
    {
        var card = Card(TakeoverPhase.Asked, ScreenAccess.Elsewhere, setupHref: "/bots/helper/sandbox", botName: "Helper");

        card.NoteText.Should().Be("Helper works on a screen of its own. Open it from its sandbox page, then tell Helper here.");
        card.NoteWords.Single(words => words.Href != null).Should().Be(new TakeoverWords("its sandbox page", "/bots/helper/sandbox"));
        card.Actions.Select(action => action.Label).Should().Equal("It is done", "I cannot do it");
        card.Actions.Should().NotContain(action => action.Primary);
    }

    [Fact]
    public void A_bot_beneath_with_nowhere_to_point_names_its_sandbox_page_in_words_only()
    {
        var card = Card(TakeoverPhase.Asked, ScreenAccess.Elsewhere, botName: "Helper");

        card.NoteText.Should().Be("Helper works on a screen of its own. Open it from its sandbox page, then tell Helper here.");
        card.NoteWords.Should().NotContain(words => words.Href != null);
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
    public async Task A_waiting_request_in_the_conversation_opens_the_screen_through_the_page_around_it()
    {
        Takeover? opened = null;
        var call = new TakeoverCallViewModel
        {
            Takeover = Handover("awaiting_approval", string.Empty).ToCard().Takeover!,
            BotName = "Sample bot",
            Takeovers = new ScreenTakeovers(null, ScreenAccess.Usable, null, takeover =>
            {
                opened = takeover;
                return Task.FromResult<string?>(null);
            }, null),
        };

        call.Phase.Should().Be(TakeoverPhase.Asked);
        call.Held.Should().BeFalse();
        await call.Open();

        opened.Should().NotBeNull();
        opened!.ToolCallId.Should().Be(callId);
        call.Sending.Should().BeFalse();
        call.Problem.Should().BeNull();
    }

    [Fact]
    public async Task An_answer_from_the_conversation_goes_to_the_page_around_it_and_a_failure_is_said()
    {
        List<(Guid, bool, string?)> answers = [];
        var call = new TakeoverCallViewModel
        {
            Takeover = Handover("awaiting_approval", string.Empty).ToCard().Takeover!,
            BotName = "Sample bot",
            Takeovers = new ScreenTakeovers(null, ScreenAccess.NotPermitted, null, null, (id, done, note) =>
            {
                answers.Add((id, done, note));
                return Task.FromResult<string?>(done ? null : "Your answer was not sent.");
            }),
        };

        await call.Answer(done: true, note: null);
        call.Problem.Should().BeNull();
        await call.Answer(done: false, note: "No code.");

        answers.Should().Equal((callId, true, null), (callId, false, "No code."));
        call.Problem.Should().Be("Your answer was not sent.");
    }

    [Fact]
    public void In_the_conversation_a_held_screen_is_answered_beside_it()
    {
        var call = new TakeoverCallViewModel
        {
            Takeover = Handover("awaiting_approval", string.Empty).ToCard().Takeover!,
            BotName = "Sample bot",
            Takeovers = new ScreenTakeovers(callId, ScreenAccess.Usable, null, null, null),
        };
        var card = new TakeoverCardViewModel
        {
            BotName = call.BotName,
            Reason = call.Takeover.Reason,
            Phase = call.Phase,
            Access = call.Takeovers.Access,
            AnswerBesideScreen = call.Held,
        };
        card.ParametersSet();

        card.Title.Should().Be("You have Sample bot's screen");
        card.Actions.Should().BeEmpty();
    }

    #endregion

    #region Helpers

    private static TakeoverCardViewModel Card(
        TakeoverPhase phase, ScreenAccess access = ScreenAccess.Usable, string reason = "Sign in.", string? setupHref = null, string botName = "Sample bot")
    {
        var card = new TakeoverCardViewModel { BotName = botName, Reason = reason, Phase = phase, Access = access, SetupHref = setupHref };
        card.ParametersSet();
        return card;
    }

    private static TakeoverStep Step(TakeoverCardViewModel card, string label) => card.Actions.Single(action => action.Label == label).Step;

    private static ToolCallView Handover(string state, string preview) =>
        new(callId, "computer_handover", "computer.handover", """{"reason":"Sign in with your passkey."}""", "ask", state, Preview: preview);

    #endregion
}
