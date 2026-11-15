namespace Drover.Ui.Tests.Screens;

public sealed class TalkTests : ScreenTest
{
    #region Private Fields

    private static readonly Guid connectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly Guid threadId = Guid.Parse("0196f1a0-0000-7000-8000-000000000005");

    private static readonly Guid runId = Guid.Parse("0196f1a0-0000-7000-8000-000000000006");

    private static readonly Guid replyId = Guid.Parse("0196f1a0-0000-7000-8000-000000000007");

    private static readonly Guid helperId = Guid.Parse("0196f1a0-0000-7000-8000-000000000008");

    private static readonly Guid sideId = Guid.Parse("0196f1a0-0000-7000-8000-000000000009");

    private static readonly string space = $"/api/spaces/{SpaceId}";

    #endregion

    #region Constructors

    public TalkTests()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/model-connections", new[] { Connection });
        Server.Answer("GET", $"{space}/bots", new[] { Marshal });
        Server.Answer("GET", $"{space}/bots/{botId}/threads", new[] { Thread(null) });
        Main(Thread(null));
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", new[] { Said("user", "Sample question"), Said("assistant", "Sample **answer**") });
    }

    #endregion

    #region Properties

    private static ModelConnectionSummary Connection { get; } =
        new(connectionId, "Sample connection", "venice", null, true, "connected", null, [], null, null);

    private static BotSummary Marshal { get; } = new(botId, "Marshal", "marshal", "Sample instructions", connectionId, "sample-default", "active");

    private static BotSummary Helper { get; } =
        new(helperId, "Sample helper", "sample-helper", "Sample instructions", connectionId, "sample-default", "paused", botId);

    private static ThreadSummary Side { get; } = new(sideId, botId, "Sample side chat", false, DateTimeOffset.UnixEpoch, null);

    #endregion

    #region Not Ready

    [Fact]
    public void With_no_model_the_workspace_says_so_and_links_to_connecting_one()
    {
        Server.Answer("GET", $"{space}/model-connections", Array.Empty<ModelConnectionSummary>());
        Server.Answer("GET", $"{space}/bots", Array.Empty<BotSummary>());

        var page = Render<Talk>();

        page.WaitForAssertion(() => page.Find("main .dv-empty-title").TextContent.Should().Be("No model is connected"));
        page.Find("main .dv-empty-action").GetAttribute("href").Should().Be("/setup/model");
        page.Find("nav .dv-empty-title").TextContent.Should().Be("No model is connected", "a phone shows only the list");
        page.FindAll("textarea").Should().BeEmpty();
    }

    [Fact]
    public void With_a_model_and_no_bot_the_workspace_links_to_meeting_the_first_bot()
    {
        Server.Answer("GET", $"{space}/bots", Array.Empty<BotSummary>());

        var page = Render<Talk>();

        page.WaitForAssertion(() => page.Find("main .dv-empty-title").TextContent.Should().Be("No bot yet"));
        page.Find("main .dv-empty-action").GetAttribute("href").Should().Be("/setup/bot");
    }

    [Fact]
    public void A_space_that_fails_to_load_says_why_and_loads_on_a_second_try()
    {
        Server.Answer("GET", $"{space}/bots", HttpStatusCode.InternalServerError);
        var page = Render<Talk>();
        page.WaitForAssertion(() => page.Find("main .dv-empty-title").TextContent.Should().Be("Drover could not load this space"));

        Server.Answer("GET", $"{space}/bots", new[] { Marshal });
        page.Find("main .dv-empty button").Click();

        page.WaitForAssertion(() => page.Find("textarea").GetAttribute("aria-label").Should().Be("Message to Marshal"));
    }

    #endregion

    #region Reading

    [Fact]
    public void The_start_page_lists_the_crew_as_a_tree_and_opens_the_leads_main_conversation()
    {
        Server.Answer("GET", $"{space}/bots", new[] { Marshal, Helper });
        Server.Answer("GET", $"{space}/bots/{helperId}/threads", Array.Empty<ThreadSummary>());

        var page = Render<Talk>();

        page.WaitForAssertion(() => page.FindAll("nav[aria-label=Crew] a.dv-crew-bot").Select(link => link.GetAttribute("href"))
            .Should().Equal($"/talk/{botId}", $"/talk/{helperId}"));
        page.Find("nav h1").TextContent.Should().Be("Conversations");
        page.Find(".dv-crew > li > a").GetAttribute("aria-current").Should().Be("page", "the lead is the bot being talked to");
        var beneath = page.Find(".dv-crew > li > ul.dv-crew-branch a");
        beneath.QuerySelector("[role=img]")!.GetAttribute("aria-label").Should().Be("Sample helper");
        beneath.QuerySelector(".dv-crew-note")!.TextContent.Should().Be("Paused");
        page.Find("main h2").TextContent.Should().Be("Marshal");
        page.WaitForAssertion(() => page.FindAll("main article").Should().HaveCount(2));
        Server.Sent("PUT", $"{space}/bots/{botId}/main-thread").Should().ContainSingle("the main conversation is opened through the bot");
    }

    [Fact]
    public void Choosing_a_bot_shows_its_main_conversation_under_its_name_with_a_way_back_to_the_crew()
    {
        var page = RenderMain();

        page.WaitForAssertion(() => page.FindAll("article").Should().HaveCount(2));
        page.Find("main h1").TextContent.Should().Be("Marshal");
        page.Find("article .dv-message-bubble").TextContent.Should().Be("Sample question");
        page.Find("article .dv-markdown strong").TextContent.Should().Be("answer");
        page.Find("a.dv-crew-bot").GetAttribute("aria-current").Should().Be("page");
        var back = page.Find("a.dv-shell-back");
        back.GetAttribute("aria-label").Should().Be("Back to the crew");
        back.GetAttribute("href").Should().Be("/");
        page.FindAll("aside").Should().BeEmpty("side chats stay out of the way until they are asked for");
    }

    [Fact]
    public void A_bot_that_is_gone_says_so_and_leads_back_to_the_crew()
    {
        Navigation.NavigateTo($"/talk/{helperId}");

        var page = Render<Talk>(parameters => parameters.Add(talk => talk.BotId, helperId));

        page.WaitForAssertion(() => page.Find("main .dv-empty-title").TextContent.Should().Be("This bot is not here"));
        page.Find("main .dv-empty-action").GetAttribute("href").Should().Be("/");
        page.FindAll("textarea").Should().BeEmpty();
    }

    [Fact]
    public void An_address_kept_from_before_goes_to_the_bots_main_conversation_or_its_side_chat()
    {
        Navigation.NavigateTo($"/threads/{threadId}");
        var page = Render<Talk>(parameters => parameters.Add(talk => talk.FormerThreadId, threadId));
        page.WaitForAssertion(() => Address.Should().Be($"talk/{botId}"));

        Server.Answer("GET", $"{space}/threads/{sideId}", Side);
        Navigation.NavigateTo($"/threads/{sideId}");
        page.Render(parameters => parameters.Add(talk => talk.FormerThreadId, sideId));
        page.WaitForAssertion(() => Address.Should().Be($"talk/{botId}/side/{sideId}"));
    }

    [Fact]
    public void An_address_kept_from_before_whose_conversation_is_gone_says_so()
    {
        var gone = Guid.Parse("0196f1a0-0000-7000-8000-00000000000a");
        Server.Answer("GET", $"{space}/threads/{gone}", HttpStatusCode.NotFound);
        Navigation.NavigateTo($"/threads/{gone}");

        var page = Render<Talk>(parameters => parameters.Add(talk => talk.FormerThreadId, gone));

        page.WaitForAssertion(() => page.Find("main .dv-empty-title").TextContent.Should().Be("This conversation is not here"));
        page.Find("main .dv-empty-action").GetAttribute("href").Should().Be("/");
    }

    #endregion

    #region Side Chats

    [Fact]
    public void The_way_to_the_side_chats_says_how_many_there_are()
    {
        var page = RenderMain();
        page.WaitForAssertion(() => page.Find("a.dv-shell-side").TextContent.Should().Be("Side chats"));
        page.Find("a.dv-shell-side").GetAttribute("href").Should().Be($"/talk/{botId}/side");
        page.Find("a.dv-shell-side").HasAttribute("aria-current").Should().BeFalse();
    }

    [Fact]
    public void The_side_chats_open_beside_the_main_conversation_and_say_what_a_side_chat_is()
    {
        Server.Answer("GET", $"{space}/bots/{botId}/threads", new[] { Thread(null), Side });

        var page = RenderSide();

        page.WaitForAssertion(() => page.Find("aside").GetAttribute("aria-label").Should().Be("Side chats with Marshal"));
        page.Find("aside h1").TextContent.Should().Be("Side chats");
        page.Find("main h2").TextContent.Should().Be("Marshal", "the main conversation stays, and the rail has the page's heading");
        page.Find("aside .dv-shell-hint").TextContent.Should().Be("A side chat is a separate conversation with Marshal. What is said there stays out of the main conversation.");
        var chat = page.Find("aside a.dv-side-chat");
        chat.GetAttribute("href").Should().Be($"/talk/{botId}/side/{sideId}");
        chat.QuerySelector(".dv-side-chat-title")!.TextContent.Should().Be("Sample side chat");
        page.FindAll("aside a.dv-side-chat").Should().ContainSingle("the main conversation is not a side chat");
        page.Find("aside a.dv-shell-new").GetAttribute("href").Should().Be($"/talk/{botId}/side/new");
        page.Find("aside a.dv-shell-new").TextContent.Trim().Should().Be("New side chat");
        var close = page.Find("aside a.dv-shell-up");
        close.GetAttribute("aria-label").Should().Be("Close side chats");
        close.GetAttribute("href").Should().Be($"/talk/{botId}");
        page.Find("a.dv-shell-side").TextContent.Should().Be("1 side chat");
        page.Find("a.dv-shell-side").GetAttribute("aria-current").Should().Be("true");
    }

    [Fact]
    public void A_side_chat_shows_its_own_messages_under_its_title_with_a_way_back_to_the_others()
    {
        Server.Answer("GET", $"{space}/bots/{botId}/threads", new[] { Thread(null), Side });
        Server.Answer("GET", $"{space}/threads/{sideId}", Side);
        Server.Answer("GET", $"{space}/threads/{sideId}/messages", new[] { Said("user", "Sample aside") });

        var page = RenderSide(sideId.ToString());

        page.WaitForAssertion(() => page.Find("aside .dv-message-bubble").TextContent.Should().Be("Sample aside"));
        page.Find("aside h1").TextContent.Should().Be("Sample side chat");
        var up = page.Find("aside a.dv-shell-up");
        up.GetAttribute("aria-label").Should().Be("All side chats with Marshal");
        up.GetAttribute("href").Should().Be($"/talk/{botId}/side");
        page.FindAll("main article").Should().HaveCount(2, "the main conversation is still there");
        page.FindAll("textarea").Should().HaveCount(2);
    }

    [Fact]
    public void A_side_chat_that_is_gone_says_so_and_leads_back_to_the_bot()
    {
        Server.Answer("GET", $"{space}/threads/{sideId}", HttpStatusCode.NotFound);

        var page = RenderSide(sideId.ToString());

        page.WaitForAssertion(() => page.Find("aside .dv-empty-title").TextContent.Should().Be("This side chat is not here"));
        page.Find("aside .dv-empty-action").GetAttribute("href").Should().Be($"/talk/{botId}");
        page.Find("aside .dv-empty-action").TextContent.Should().Be("Back to Marshal");
    }

    [Fact]
    public async Task The_first_message_of_a_new_side_chat_makes_its_thread_and_moves_to_its_address()
    {
        Server.Answer("POST", $"{space}/bots/{botId}/threads", Side with { Title = null }, HttpStatusCode.Created);
        Server.Answer("POST", $"{space}/threads/{sideId}/messages",
            new SendMessageResponse(Said("user", "First sample words") with { ThreadId = sideId }, Run("queued") with { ThreadId = sideId }), HttpStatusCode.Accepted);
        Server.Answer("GET", $"{space}/threads/{sideId}", Side with { Title = "First sample words", ActiveRunId = runId });
        var events = Server.Stream($"{space}/runs/{runId}/events");
        var page = RenderSide("new");
        page.WaitForAssertion(() => page.Find("aside h1").TextContent.Should().Be("New side chat"));

        await page.Find("aside textarea").InputAsync(new ChangeEventArgs { Value = "First sample words" });
        await page.Find("aside form").SubmitAsync();
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));

        page.WaitForAssertion(() => Address.Should().Be($"talk/{botId}/side/{sideId}"));
        page.Render(parameters => parameters.Add(talk => talk.SideChat, sideId.ToString()));
        page.WaitForAssertion(() => page.Find("aside .dv-message-bubble").TextContent.Should().Be("First sample words"));
        page.Find("aside h1").TextContent.Should().Be("First sample words");
        Server.Sent("GET", $"{space}/threads/{sideId}/messages").Should().BeEmpty("the conversation on screen is kept, not loaded again");
    }

    #endregion

    #region Writing

    [Fact]
    public async Task Sending_shows_the_message_at_once_and_the_bot_thinking_with_stop_in_place_of_send()
    {
        var events = RunStarts();
        var page = RenderMain();
        page.WaitForElement("article");

        await page.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Another sample question" });
        await page.Find("form").SubmitAsync();
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));

        page.WaitForAssertion(() => page.Find(".dv-message-state").TextContent.Should().Be("Thinking"));
        page.FindAll(".dv-message-bubble")[^1].TextContent.Should().Be("Another sample question");
        page.FindAll(".dv-composer button").Select(button => button.TextContent.Trim()).Should().Equal("Stop");
        page.Find(".dv-crew-bot [role=img]").GetAttribute("aria-label").Should().Be("Marshal, thinking");
        page.Find(".dv-shell-main-header [role=img]").GetAttribute("aria-label").Should().Be("Marshal, thinking");
    }

    [Fact]
    public async Task The_reply_appears_as_it_streams_and_send_returns_when_the_run_ends()
    {
        var events = RunStarts();
        var page = RenderMain();
        page.WaitForElement("article");
        await page.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Another sample question" });
        await page.Find("form").SubmitAsync();

        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));
        await events.Send(Event(2, RunEventTypes.StepStarted, new StepChanged(1, replyId)));
        await events.Send(Event(3, RunEventTypes.MessageDelta, new MessageDelta(replyId, 0, "text", "Second sample")));

        page.WaitForAssertion(() => page.FindAll("article .dv-markdown")[^1].TextContent.Trim().Should().Be("Second sample"));
        page.FindAll("article .dv-markdown")[^1].GetAttribute("aria-busy").Should().Be("true");
        page.FindAll(".dv-message-state").Should().BeEmpty("the bot has started to answer");
        page.Find(".dv-composer button").TextContent.Trim().Should().Be("Stop");

        await events.Send(Event(4, RunEventTypes.MessageDelta, new MessageDelta(replyId, 0, "text", " answer")));
        page.WaitForAssertion(() => page.FindAll("article .dv-markdown")[^1].TextContent.Trim().Should().Be("Second sample answer"));

        var reply = Said("assistant", "Second sample answer") with { Id = replyId };
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", new[] { Said("user", "Sample question"), Said("assistant", "Sample answer"), Said("user", "Another sample question"), reply });
        await events.Send(Event(5, RunEventTypes.MessageCompleted, new MessageCompleted(reply)));
        await events.Send(Event(6, RunEventTypes.RunCompleted, new RunStatusChanged("completed", null, null, 1, null)));
        await events.End();

        page.WaitForAssertion(() => page.Find(".dv-composer button").TextContent.Trim().Should().Be("Send"));
        page.FindAll("article").Should().HaveCount(4, "the streamed reply and the stored one are the same message");
        page.FindAll("article .dv-markdown")[^1].HasAttribute("aria-busy").Should().BeFalse();
    }

    [Fact]
    public async Task A_message_sent_while_the_bot_works_waits_as_pending_and_takes_its_place_when_delivered()
    {
        var events = RunStarts();
        var page = RenderMain();
        page.WaitForElement("article");
        await page.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Another sample question" });
        await page.Find("form").SubmitAsync();
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));
        page.WaitForAssertion(() => page.Find(".dv-composer button").TextContent.Trim().Should().Be("Stop"));

        var steering = Said("user", "Keep it short") with { Position = null, Steers = true };
        Server.Answer("POST", $"{space}/threads/{threadId}/messages", new SendMessageResponse(steering, Run("running")), HttpStatusCode.Accepted);
        await page.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Keep it short" });
        await page.Find("form").SubmitAsync();

        page.WaitForAssertion(() => page.Find(".dv-message-pending").TextContent.Should().Be("Sent. Marshal reads it at its next step."));
        page.FindAll(".dv-message-bubble")[^1].TextContent.Should().Be("Keep it short");
        page.Find(".dv-composer button").TextContent.Trim().Should().Be("Stop", "the run is still the same one");

        await events.Send(Event(2, RunEventTypes.MessageDelivered, new MessageDelivered(steering with { Position = 3 })));

        page.WaitForAssertion(() => page.FindAll(".dv-message-pending").Should().BeEmpty());
        page.FindAll(".dv-message-bubble").Select(bubble => bubble.TextContent).Should().Equal("Sample question", "Another sample question", "Keep it short");
    }

    [Fact]
    public async Task A_run_that_fails_says_why_in_an_alert()
    {
        var events = RunStarts();
        var page = RenderMain();
        page.WaitForElement("article");
        await page.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Another sample question" });
        await page.Find("form").SubmitAsync();

        await events.Send(Event(1, RunEventTypes.RunFailed, new RunStatusChanged("failed", null, new("unauthorized", "The provider rejected the API key."), 1, null)));
        await events.End();

        page.WaitForAssertion(() => page.Find("main [role=alert]").TextContent.Trim()
            .Should().Be("The provider rejected the API key. Send your message again to retry."));
        page.Find(".dv-composer button").TextContent.Trim().Should().Be("Send");
    }

    [Fact]
    public void A_message_the_server_refuses_is_reported_and_nothing_is_added()
    {
        Server.Refuse("POST", $"{space}/threads/{threadId}/messages", HttpStatusCode.Conflict, "The bot is still working on this thread.");
        var page = RenderMain();
        page.WaitForElement("article");

        page.Find("textarea").Input("Another sample question");
        page.Find("form").Submit();

        page.WaitForAssertion(() => page.Find("main [role=alert]").TextContent.Trim()
            .Should().Be("Your message was not sent. The bot is still working on this thread."));
        page.FindAll("article").Should().HaveCount(2);
    }

    [Fact]
    public async Task Stop_asks_the_server_to_cancel_the_run_and_says_it_was_stopped()
    {
        Main(Thread(runId));
        Server.Answer("POST", $"{space}/runs/{runId}/cancel", Run("running"));
        var events = Server.Stream($"{space}/runs/{runId}/events");
        var page = RenderMain();
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));
        page.WaitForAssertion(() => page.Find(".dv-composer button").TextContent.Trim().Should().Be("Stop"));

        await page.Find(".dv-composer button").ClickAsync(new MouseEventArgs());
        page.WaitForAssertion(() => Server.Sent("POST", $"{space}/runs/{runId}/cancel").Should().ContainSingle());
        await events.Send(Event(2, RunEventTypes.RunCancelled, new RunStatusChanged("cancelled", null, null, 1, null)));
        await events.End();

        page.WaitForAssertion(() => page.Find("main [role=status] .dv-note").TextContent.Trim().Should().Be("You stopped this reply."));
        page.Find(".dv-composer button").TextContent.Trim().Should().Be("Send");
    }

    [Fact]
    public async Task A_reply_under_way_when_the_page_loads_is_rebuilt_from_its_events_and_goes_on()
    {
        Main(Thread(runId));
        var events = Server.Stream($"{space}/runs/{runId}/events");

        var page = RenderMain();
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));
        await events.Send(Event(2, RunEventTypes.StepStarted, new StepChanged(1, replyId)));
        await events.Send(Event(3, RunEventTypes.MessageDelta, new MessageDelta(replyId, 0, "text", "Said before the reload,")));
        await events.Send(Event(4, RunEventTypes.MessageDelta, new MessageDelta(replyId, 0, "text", " and after it")));

        page.WaitForAssertion(() => page.FindAll("article .dv-markdown")[^1].TextContent.Trim().Should().Be("Said before the reload, and after it"));
        page.Find(".dv-composer button").TextContent.Trim().Should().Be("Stop");
    }

    #endregion

    #region Private Methods

    private static ThreadSummary Thread(Guid? activeRunId) =>
        new(threadId, botId, null, false, DateTimeOffset.UnixEpoch, activeRunId, IsMain: true);

    // The main conversation is opened through its bot and read again by its own address when a run ends.
    private void Main(ThreadSummary thread)
    {
        Server.Answer("PUT", $"{space}/bots/{botId}/main-thread", thread);
        Server.Answer("GET", $"{space}/threads/{threadId}", thread);
    }

    private static MessageResponse Said(string role, string text) =>
        new(Guid.CreateVersion7(), threadId, 0, role, null, [new("text") { Text = text }], DateTimeOffset.UnixEpoch);

    private static RunSummary Run(string status) =>
        new(runId, threadId, botId, runId, status, null, null, 1, 0, new(0, 0, 0, 0), null, null);

    private static RunEventResponse Event(long seq, string type, object payload) =>
        new(seq, runId, type, RunEventTypes.Write(payload), DateTimeOffset.UnixEpoch);

    // The server accepts the next message and starts a run whose events the test then writes.
    private LiveEvents RunStarts(string text = "Another sample question")
    {
        Server.Answer("POST", $"{space}/threads/{threadId}/messages", new SendMessageResponse(Said("user", text), Run("queued")), HttpStatusCode.Accepted);
        return Server.Stream($"{space}/runs/{runId}/events");
    }

    private IRenderedComponent<Talk> RenderMain()
    {
        Navigation.NavigateTo($"/talk/{botId}");
        return Render<Talk>(parameters => parameters.Add(page => page.BotId, botId));
    }

    // The side chats of the bot: their list, or one of them by its thread or as "new".
    private IRenderedComponent<Talk> RenderSide(string? sideChat = null)
    {
        Navigation.NavigateTo(sideChat is null ? $"/talk/{botId}/side" : $"/talk/{botId}/side/{sideChat}");
        return Render<Talk>(parameters => parameters
            .Add(page => page.BotId, botId)
            .Add(page => page.Rail, sideChat is null ? "side" : null)
            .Add(page => page.SideChat, sideChat));
    }

    #endregion
}
