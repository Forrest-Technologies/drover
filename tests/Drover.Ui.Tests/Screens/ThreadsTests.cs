namespace Drover.Ui.Tests.Screens;

public sealed class ThreadsTests : ScreenTest
{
    #region Private Fields

    private static readonly Guid connectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly Guid threadId = Guid.Parse("0196f1a0-0000-7000-8000-000000000005");

    private static readonly Guid runId = Guid.Parse("0196f1a0-0000-7000-8000-000000000006");

    private static readonly Guid replyId = Guid.Parse("0196f1a0-0000-7000-8000-000000000007");

    private static readonly string space = $"/api/spaces/{SpaceId}";

    #endregion

    #region Constructors

    public ThreadsTests()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/model-connections", new[] { Connection });
        Server.Answer("GET", $"{space}/bots", new[] { Atlas });
        Server.Answer("GET", $"{space}/bots/{botId}/threads", new[] { Thread(null) });
        Server.Answer("GET", $"{space}/threads/{threadId}", Thread(null));
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", new[] { Said("user", "Sample question"), Said("assistant", "Sample **answer**") });
    }

    #endregion

    #region Properties

    private static ModelConnectionSummary Connection { get; } =
        new(connectionId, "Sample connection", "venice", null, true, "connected", null, [], null, null);

    private static BotSummary Atlas { get; } = new(botId, "Atlas", "atlas", "Sample instructions", connectionId, "sample-default", "active");

    #endregion

    #region Not Ready

    [Fact]
    public void With_no_model_the_workspace_says_so_and_links_to_connecting_one()
    {
        Server.Answer("GET", $"{space}/model-connections", Array.Empty<ModelConnectionSummary>());
        Server.Answer("GET", $"{space}/bots", Array.Empty<BotSummary>());

        var page = Render<Threads>();

        page.WaitForAssertion(() => page.Find("main .dv-empty-title").TextContent.Should().Be("No model is connected"));
        page.Find("main .dv-empty-action").GetAttribute("href").Should().Be("/setup/model");
        page.Find("nav .dv-empty-title").TextContent.Should().Be("No model is connected", "a phone shows only the list");
        page.FindAll("textarea").Should().BeEmpty();
    }

    [Fact]
    public void With_a_model_and_no_bot_the_workspace_links_to_meeting_the_first_bot()
    {
        Server.Answer("GET", $"{space}/bots", Array.Empty<BotSummary>());

        var page = Render<Threads>();

        page.WaitForAssertion(() => page.Find("main .dv-empty-title").TextContent.Should().Be("No bot yet"));
        page.Find("main .dv-empty-action").GetAttribute("href").Should().Be("/setup/bot");
    }

    [Fact]
    public void A_space_that_fails_to_load_says_why_and_loads_on_a_second_try()
    {
        Server.Answer("GET", $"{space}/bots", HttpStatusCode.InternalServerError);
        var page = Render<Threads>();
        page.WaitForAssertion(() => page.Find("main .dv-empty-title").TextContent.Should().Be("Drover could not load this space"));

        Server.Answer("GET", $"{space}/bots", new[] { Atlas });
        page.Find("main .dv-empty button").Click();

        page.WaitForAssertion(() => page.Find("textarea").GetAttribute("aria-label").Should().Be("Message to Atlas"));
    }

    #endregion

    #region Reading

    [Fact]
    public void The_start_page_lists_the_threads_and_is_ready_for_a_new_one()
    {
        var page = Render<Threads>();

        page.WaitForAssertion(() => page.Find("nav[aria-label=Threads] a.dv-thread").GetAttribute("href").Should().Be($"/threads/{threadId}"));
        page.Find(".dv-thread-title").TextContent.Should().Be("Sample thread");
        page.Find("nav h1").TextContent.Should().Be("Threads");
        page.Find("a.dv-shell-new").TextContent.Trim().Should().Be("New thread");
        page.Find("a.dv-shell-new").GetAttribute("href").Should().Be("/new");
        page.Find("main h2").TextContent.Should().Be("New thread");
        page.Find(".dv-messages-start").TextContent.Should().Contain("Write to Atlas below.");
    }

    [Fact]
    public void An_open_thread_shows_its_messages_under_its_title_with_a_way_back()
    {
        var page = RenderThread();

        page.WaitForAssertion(() => page.FindAll("article").Should().HaveCount(2));
        page.Find("main h1").TextContent.Should().Be("Sample thread");
        page.Find("article .dv-message-bubble").TextContent.Should().Be("Sample question");
        page.Find("article .dv-markdown strong").TextContent.Should().Be("answer");
        page.Find("a.dv-thread").GetAttribute("aria-current").Should().Be("page");
        var back = page.Find("a.dv-shell-back");
        back.GetAttribute("aria-label").Should().Be("Back to threads");
        back.GetAttribute("href").Should().Be("/");
    }

    [Fact]
    public void A_thread_that_is_gone_says_so_and_offers_a_new_one()
    {
        Server.Answer("GET", $"{space}/threads/{threadId}", HttpStatusCode.NotFound);

        var page = RenderThread();

        page.WaitForAssertion(() => page.Find("main .dv-empty-title").TextContent.Should().Be("This thread is not here"));
        page.Find("main .dv-empty-action").GetAttribute("href").Should().Be("/new");
    }

    #endregion

    #region Writing

    [Fact]
    public async Task Sending_shows_the_message_at_once_and_the_bot_thinking_with_stop_in_place_of_send()
    {
        var events = RunStarts();
        var page = RenderThread();
        page.WaitForElement("article");

        await page.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Another sample question" });
        await page.Find("form").SubmitAsync();
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));

        page.WaitForAssertion(() => page.Find(".dv-message-state").TextContent.Should().Be("Thinking"));
        page.FindAll(".dv-message-bubble")[^1].TextContent.Should().Be("Another sample question");
        page.FindAll(".dv-composer button").Select(button => button.TextContent.Trim()).Should().Equal("Stop");
        page.Find(".dv-thread [role=img]").GetAttribute("aria-label").Should().Be("Atlas, thinking");
    }

    [Fact]
    public async Task The_reply_appears_as_it_streams_and_send_returns_when_the_run_ends()
    {
        var events = RunStarts();
        var page = RenderThread();
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
        var page = RenderThread();
        page.WaitForElement("article");
        await page.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Another sample question" });
        await page.Find("form").SubmitAsync();
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));
        page.WaitForAssertion(() => page.Find(".dv-composer button").TextContent.Trim().Should().Be("Stop"));

        var steering = Said("user", "Keep it short") with { Position = null, Steers = true };
        Server.Answer("POST", $"{space}/threads/{threadId}/messages", new SendMessageResponse(steering, Run("running")), HttpStatusCode.Accepted);
        await page.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Keep it short" });
        await page.Find("form").SubmitAsync();

        page.WaitForAssertion(() => page.Find(".dv-message-pending").TextContent.Should().Be("Sent. Atlas reads it at its next step."));
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
        var page = RenderThread();
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
        var page = RenderThread();
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
        Server.Answer("GET", $"{space}/threads/{threadId}", Thread(runId));
        Server.Answer("POST", $"{space}/runs/{runId}/cancel", Run("running"));
        var events = Server.Stream($"{space}/runs/{runId}/events");
        var page = RenderThread();
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
        Server.Answer("GET", $"{space}/threads/{threadId}", Thread(runId));
        var events = Server.Stream($"{space}/runs/{runId}/events");

        var page = RenderThread();
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));
        await events.Send(Event(2, RunEventTypes.StepStarted, new StepChanged(1, replyId)));
        await events.Send(Event(3, RunEventTypes.MessageDelta, new MessageDelta(replyId, 0, "text", "Said before the reload,")));
        await events.Send(Event(4, RunEventTypes.MessageDelta, new MessageDelta(replyId, 0, "text", " and after it")));

        page.WaitForAssertion(() => page.FindAll("article .dv-markdown")[^1].TextContent.Trim().Should().Be("Said before the reload, and after it"));
        page.Find(".dv-composer button").TextContent.Trim().Should().Be("Stop");
    }

    [Fact]
    public async Task The_first_message_of_a_new_conversation_makes_its_thread_and_moves_to_its_address()
    {
        Server.Answer("POST", $"{space}/bots/{botId}/threads", Thread(null) with { Title = null }, HttpStatusCode.Created);
        var events = RunStarts("First sample words");
        Navigation.NavigateTo("/new");
        var page = Render<Threads>();
        page.WaitForElement("textarea");

        await page.Find("textarea").InputAsync(new ChangeEventArgs { Value = "First sample words" });
        await page.Find("form").SubmitAsync();
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));

        page.WaitForAssertion(() => Address.Should().Be($"threads/{threadId}"));
        page.WaitForAssertion(() => page.Find(".dv-message-bubble").TextContent.Should().Be("First sample words"));
        page.Find(".dv-shell-title").TextContent.Should().Be("First sample words");
    }

    #endregion

    #region Private Methods

    private static ThreadSummary Thread(Guid? activeRunId) =>
        new(threadId, botId, "Sample thread", false, DateTimeOffset.UnixEpoch, activeRunId);

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

    private IRenderedComponent<Threads> RenderThread()
    {
        Navigation.NavigateTo($"/threads/{threadId}");
        return Render<Threads>(parameters => parameters.Add(page => page.ThreadId, threadId));
    }

    #endregion
}
