namespace Drover.Ui.Tests.Chat;

public sealed class ChatComponentTests : BunitContext
{
    #region Private Fields

    private static readonly Guid first = Guid.Parse("0196f1a0-0000-7000-8000-00000000000a");

    private static readonly Guid second = Guid.Parse("0196f1a0-0000-7000-8000-00000000000b");

    #endregion

    #region Constructors

    public ChatComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    #endregion

    #region Crew And Side Chats

    [Fact]
    public void Each_bot_is_a_link_to_its_main_conversation_under_the_bot_that_manages_it()
    {
        var lead = new BotSummary(first, "Sample lead", "sample-lead", string.Empty, Guid.Empty, "sample-model", "active");
        var helper = new BotSummary(second, "Sample helper", "sample-helper", string.Empty, Guid.Empty, "sample-model", "active", first);

        var list = Render<CrewList>(parameters => parameters
            .Add(component => component.Crew, CrewTree.From([helper, lead]))
            .Add(component => component.SelectedId, second)
            .Add(component => component.Working, ImmutableHashSet.Create(second)));

        var links = list.FindAll("a");
        links.Select(link => link.GetAttribute("href")).Should().Equal($"/talk/{first}", $"/talk/{second}");
        links[0].HasAttribute("aria-current").Should().BeFalse();
        links[1].GetAttribute("aria-current").Should().Be("page");
        links[1].QuerySelector("[role=img]")!.GetAttribute("aria-label").Should().Be("Sample helper, thinking");
        links[1].QuerySelector(".dv-crew-name")!.GetAttribute("aria-hidden").Should().Be("true", "the head already says the name");
        list.Find("ul.dv-crew > li > ul.dv-crew-branch a").GetAttribute("href").Should().Be($"/talk/{second}");
    }

    [Fact]
    public void A_bot_whose_parent_is_not_listed_or_points_back_at_it_is_still_in_the_crew()
    {
        var orphan = new BotSummary(first, "Sample orphan", "sample-orphan", string.Empty, Guid.Empty, "sample-model", "paused", Guid.Parse("0196f1a0-0000-7000-8000-00000000000c"));
        var looped = new BotSummary(second, "Sample loop", "sample-loop", string.Empty, Guid.Empty, "sample-model", "active", second);

        var crew = CrewTree.From([orphan, looped]);

        crew.Select(node => node.Bot.Id).Should().Equal(first, second);
        CrewTree.Lead([orphan, looped])!.Id.Should().Be(second, "the lead is the first bot at the top that can answer");
    }

    [Fact]
    public void Each_side_chat_is_a_link_and_the_one_whose_bot_is_working_says_so()
    {
        var list = Render<SideChatList>(parameters => parameters
            .Add(component => component.Chats,
            [
                new(first, "Sample side chat one", $"/talk/{second}/side/{first}", "Sample bot", 2, false),
                new(second, "Sample side chat two", $"/talk/{second}/side/{second}", "Sample bot", 2, true),
            ])
            .Add(component => component.SelectedId, second));

        var links = list.FindAll("a");
        links.Select(link => link.GetAttribute("href")).Should().Equal($"/talk/{second}/side/{first}", $"/talk/{second}/side/{second}");
        links.Select(link => link.QuerySelector(".dv-side-chat-title")!.TextContent).Should().Equal("Sample side chat one", "Sample side chat two");
        links[0].HasAttribute("aria-current").Should().BeFalse();
        links[1].GetAttribute("aria-current").Should().Be("page");
        links[1].QuerySelector("[role=img]")!.GetAttribute("aria-label").Should().Be("Sample bot, thinking");
    }

    #endregion

    #region Message List

    [Fact]
    public void A_persons_message_is_a_bubble_and_a_bots_message_is_rendered_markdown_under_its_name()
    {
        var list = RenderMessages([new("1", true, "Hello **there**"), new("2", false, "Hello **back**")]);

        var messages = list.FindAll("article");
        messages[0].GetAttribute("aria-label").Should().Be("You said");
        messages[0].QuerySelector(".dv-message-bubble")!.TextContent.Should().Be("Hello **there**");
        messages[1].GetAttribute("aria-label").Should().Be("Sample bot said");
        messages[1].QuerySelector(".dv-markdown strong")!.TextContent.Should().Be("back");
        messages[1].QuerySelector("[role=img]")!.GetAttribute("aria-label").Should().Be("Sample bot");
    }

    [Fact]
    public void A_bot_speaks_from_its_character_large_beside_its_words_and_the_plate_is_live_only_while_it_writes()
    {
        var list = RenderMessages([new("1", false, "Sample reply."), new("2", false, "Sample", Streaming: true)]);

        var heads = list.FindAll(".dv-message-bot .dv-message-head");
        heads.Should().HaveCount(2);
        heads.Select(head => head.QuerySelector("[role=img]")!.ClassList.Contains("dv-avatar-48")).Should().Equal(true, true);
        heads.Select(head => head.QuerySelector("[role=img]")!.GetAttribute("aria-label")).Should().Equal("Sample bot", "Sample bot, writing");
        heads.Select(head => head.ClassList.Contains("dv-message-head-live")).Should().Equal(false, true);
    }

    [Fact]
    public void A_message_the_bot_has_not_read_yet_says_so_in_words()
    {
        var list = RenderMessages([new("1", true, "Hello"), new("2", true, "One more thing", Pending: true)]);

        var messages = list.FindAll("article");
        messages[0].QuerySelectorAll(".dv-message-pending").Should().BeEmpty();
        messages[1].QuerySelector(".dv-message-pending")!.TextContent.Should().Be("Sent. Sample bot reads it at its next step.");
    }

    [Fact]
    public void A_reply_that_is_still_arriving_is_marked_busy()
    {
        var list = RenderMessages([new("1", true, "Hello"), new("2", false, "Thinking about", Streaming: true)]);

        list.Find(".dv-markdown").GetAttribute("aria-busy").Should().Be("true");
    }

    [Fact]
    public void A_bot_that_is_working_and_has_not_answered_shows_the_thinking_state_in_words_too()
    {
        var list = RenderMessages([new("1", true, "Hello")], thinking: true);

        list.Find(".dv-character-lit").ClassList.Should().Contain("dv-state-thinking");
        list.Find("[role=img][aria-label$=thinking]").GetAttribute("aria-label").Should().Be("Sample bot, thinking");
        list.Find(".dv-message-state").TextContent.Should().Be("Thinking");
    }

    [Fact]
    public void An_empty_conversation_says_what_to_do()
    {
        var list = RenderMessages([]);

        list.Find(".dv-messages-start").TextContent.Should().Contain("Nothing said yet. Write to Sample bot below.");
    }

    [Fact]
    public void The_conversation_can_be_scrolled_by_keyboard_and_has_a_name()
    {
        var region = RenderMessages([]).Find(".dv-messages");

        region.GetAttribute("tabindex").Should().Be("0");
        region.GetAttribute("role").Should().Be("group");
        region.GetAttribute("aria-label").Should().Be("Conversation with Sample bot");
    }

    [Fact]
    public void A_streaming_reply_is_read_out_one_finished_sentence_at_a_time()
    {
        var list = RenderMessages([new("2", false, "The build", Streaming: true)]);
        var spoken = () => list.Find("[aria-live=polite]").TextContent;
        spoken().Should().BeEmpty();

        list.Render(parameters => parameters.Add(component => component.Messages, [new("2", false, "The build passed. Next", Streaming: true)]));
        spoken().Should().Be("The build passed.");

        list.Render(parameters => parameters.Add(component => component.Messages, [new("2", false, "The build passed. Next step", Streaming: false)]));
        spoken().Should().Be("Next step");
    }

    [Fact]
    public void A_failed_reply_is_reported_in_an_alert()
    {
        var list = Render<MessageList>(parameters => parameters
            .Add(component => component.Messages, [new("1", true, "Hello")])
            .Add(component => component.BotName, "Sample bot")
            .Add(component => component.Problem, "The provider rejected the API key. Check the key and send again."));

        list.Find("[role=alert]").TextContent.Trim().Should().Be("The provider rejected the API key. Check the key and send again.");
    }

    #endregion

    #region Composer

    [Fact]
    public void The_composer_is_one_named_field_and_a_send_button_that_waits_for_text()
    {
        var composer = RenderComposer();

        var field = composer.Find("textarea");
        field.GetAttribute("aria-label").Should().Be("Message to Sample bot");
        var send = composer.Find("button[type=submit]");
        send.TextContent.Trim().Should().Be("Send");
        send.GetAttribute("type").Should().Be("submit");
        send.ClassList.Should().Contain("dv-button-primary");
        send.HasAttribute("disabled").Should().BeTrue();

        field.Input("Hello");

        composer.Find("button[type=submit]").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Sending_hands_over_the_trimmed_text_and_empties_the_field()
    {
        List<string> sent = [];
        var composer = RenderComposer(onSend: sent.Add);

        composer.Find("textarea").Input("  Hello there \n");
        composer.Find("form").Submit();

        sent.Should().Equal("Hello there");
        composer.Find("textarea").GetAttribute("value").Should().BeEmpty();
    }

    [Fact]
    public void While_a_run_is_active_stop_takes_the_place_of_send()
    {
        var stopped = 0;
        var composer = RenderComposer(running: true, onCancel: () => stopped++);

        var buttons = composer.FindAll("button.dv-button");
        buttons.Select(button => button.TextContent.Trim()).Should().Equal("Stop");
        buttons[0].GetAttribute("type").Should().Be("button");

        buttons[0].Click();

        stopped.Should().Be(1);
    }

    [Fact]
    public void A_message_written_while_the_bot_works_can_be_sent_and_stop_stays()
    {
        List<string> sent = [];
        var composer = RenderComposer(running: true, onSend: sent.Add);

        composer.Find("textarea").Input("Use the shorter name");

        composer.FindAll("button.dv-button").Select(button => button.TextContent.Trim()).Should().Equal("Stop", "Send");

        composer.Find("form").Submit();

        sent.Should().Equal("Use the shorter name");
        composer.FindAll("button.dv-button").Select(button => button.TextContent.Trim()).Should().Equal("Stop");
    }

    #endregion

    #region Private Methods

    private IRenderedComponent<MessageList> RenderMessages(IReadOnlyList<ChatMessage> messages, bool thinking = false) =>
        Render<MessageList>(parameters => parameters
            .Add(component => component.Messages, messages)
            .Add(component => component.BotName, "Sample bot")
            .Add(component => component.BotHue, 2)
            .Add(component => component.Thinking, thinking));

    private IRenderedComponent<Composer> RenderComposer(bool running = false, Action<string>? onSend = null, Action? onCancel = null) =>
        Render<Composer>(parameters => parameters
            .Add(component => component.BotName, "Sample bot")
            .Add(component => component.Running, running)
            .Add(component => component.OnSend, onSend ?? (_ => { }))
            .Add(component => component.OnCancel, onCancel ?? (() => { })));

    #endregion
}
