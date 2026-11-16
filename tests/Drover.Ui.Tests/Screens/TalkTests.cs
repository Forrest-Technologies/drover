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

    private static readonly Guid failedRunId = Guid.Parse("0196f1a0-0000-7000-8000-00000000000a");

    private static readonly string space = $"/api/spaces/{SpaceId}";

    private readonly FakeTimeProvider time = new();

    #endregion

    #region Constructors

    public TalkTests()
    {
        SignedIn();
        Services.AddSingleton<TimeProvider>(time);
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

    private static RunFailure Failure { get; } = new(failedRunId, FailureCauses.ModelUnreachable, "invalid_key: Sample report of the failure.");

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

    [Fact]
    public void The_pages_title_is_drawn_while_the_space_is_still_loading_and_names_the_bot_once_it_has()
    {
        var bots = Server.Hold("GET", $"{space}/bots");

        var page = Render(builder =>
        {
            builder.OpenComponent<HeadOutlet>(0);
            builder.CloseComponent();
            builder.OpenComponent<Talk>(1);
            builder.CloseComponent();
        });

        page.WaitForAssertion(() => page.Find("title").TextContent.Should().Be("Drover"));
        bots.SetResult();
        page.WaitForAssertion(() => page.Find("title").TextContent.Should().Be("Marshal · Drover"));
    }

    #endregion

    #region What Each Bot Has For You

    [Fact]
    public void Each_bot_says_in_words_what_it_has_for_the_person_and_the_tab_counts_who_waits()
    {
        Server.Answer("GET", $"{space}/bots", new[] { Marshal, Helper });
        Server.Answer("GET", $"{space}/bots/{botId}/threads", new[] { Thread(null) with { Attention = new(false, true, true, Failure) }, Side with { Attention = new(true, false, false, null) } });
        Server.Answer("GET", $"{space}/bots/{helperId}/threads", new[] { new ThreadSummary(Guid.NewGuid(), helperId, null, false, DateTimeOffset.UnixEpoch, null, true, new(false, false, true, null)) });

        var page = Render(builder =>
        {
            builder.OpenComponent<HeadOutlet>(0);
            builder.CloseComponent();
            builder.OpenComponent<Talk>(1);
            builder.CloseComponent();
        });

        page.WaitForAssertion(() => page.FindAll("nav[aria-label=Crew] a.dv-crew-bot").Select(link => link.GetAttribute("aria-label"))
            .Should().Equal("Marshal, waiting for you", "Sample helper, paused, new"));
        page.FindAll("nav[aria-label=Crew] .dv-attention").Select(note => note.TextContent).Should().Equal("Waiting for you", "New");
        page.FindAll("nav[aria-label=Crew] .dv-attention .dv-badge-warn").Should().ContainSingle("waiting has the attention color as well as the word");
        page.FindAll("nav[aria-label=Crew] .dv-attention [class*=dv-state-]").Should().BeEmpty("nothing moves for a fact");
        page.WaitForAssertion(() => page.Find("title").TextContent.Should().Be("(1) Marshal · Drover"));
        Server.Sent("PUT", $"{space}/threads/{threadId}/seen").Should().BeEmpty("on the start page a phone shows the crew: nothing was opened");
    }

    [Fact]
    public async Task A_signal_from_another_screen_changes_the_crew_without_a_reload()
    {
        Server.Answer("GET", $"{space}/bots", new[] { Marshal, Helper });
        var helperThread = new ThreadSummary(Guid.NewGuid(), helperId, null, false, DateTimeOffset.UnixEpoch, null, true, new(true, false, false, null));
        Server.Answer("GET", $"{space}/bots/{helperId}/threads", new[] { helperThread });
        var signals = Server.Stream($"{space}/stream");
        var page = Render<Talk>();
        page.WaitForAssertion(() => page.FindAll("nav[aria-label=Crew] .dv-attention").Select(note => note.TextContent).Should().Equal("Waiting for you"));

        Server.Answer("GET", $"{space}/threads/{helperThread.Id}", helperThread with { Attention = new(false, false, false, null) });
        await signals.Send(new UserSignal(UserSignalKinds.ThreadChanged, helperThread.Id, null));

        page.WaitForAssertion(() => page.FindAll("nav[aria-label=Crew] .dv-attention").Should().BeEmpty("the approval was answered on another screen"));
    }

    [Fact]
    public void Opening_a_conversation_marks_it_seen_and_its_bot_has_nothing_new_while_it_is_open()
    {
        Server.Answer("GET", $"{space}/bots/{botId}/threads", new[] { Thread(null) with { Attention = new(false, false, true, null) } });
        Main(Thread(null) with { Attention = new(false, false, true, null) });
        Server.Answer("PUT", $"{space}/threads/{threadId}/seen", HttpStatusCode.NoContent);

        var page = RenderMain();

        page.WaitForAssertion(() => Server.Sent("PUT", $"{space}/threads/{threadId}/seen").Should().ContainSingle());
        page.FindAll("nav[aria-label=Crew] .dv-attention").Should().BeEmpty();
    }

    [Fact]
    public void A_side_chat_says_what_it_has_so_a_word_in_the_crew_can_be_traced_to_it()
    {
        Server.Answer("GET", $"{space}/bots/{botId}/threads", new[] { Thread(null), Side with { Attention = new(false, false, true, null) } });

        var page = RenderSide();

        page.WaitForAssertion(() => page.Find("aside a.dv-side-chat .dv-attention").TextContent.Should().Be("New"));
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

    #region Screen

    [Fact]
    public void A_bot_with_a_screen_offers_it_beside_the_conversation_live_and_ready_to_take_control()
    {
        Server.Answer("GET", $"{space}/bots/{botId}/sandbox", ScreenStatus(desktop: true));
        Server.AnswerBytes("POST", $"{space}/bots/{botId}/sandbox/screen", ScreenPng, "image/png");
        Server.Answer("POST", $"{space}/bots/{botId}/sandbox/input", HttpStatusCode.NoContent);
        Navigation.NavigateTo($"/talk/{botId}/screen");

        var page = Render<Talk>(parameters => parameters.Add(talk => talk.BotId, botId).Add(talk => talk.Rail, "screen"));

        page.WaitForAssertion(() => page.Find("aside img.dv-sandbox-image").GetAttribute("alt").Should().StartWith("Marshal's sandbox screen"));
        page.Find("aside").GetAttribute("aria-label").Should().Be("Marshal's screen");
        page.Find("aside h1").TextContent.Should().Be("Screen");
        page.Find("aside a.dv-shell-up").GetAttribute("aria-label").Should().Be("Close the screen");
        page.Find("aside .dv-screen-panel-state .dv-badge").TextContent.Should().Be("Running");
        page.Find("aside a.dv-screen-panel-more").GetAttribute("href").Should().Be($"/bots/{botId}/sandbox");
        var way = page.Find("a.dv-shell-side[href$=\"/screen\"]");
        way.TextContent.Should().Be("Screen");
        way.GetAttribute("aria-current").Should().Be("true");
        page.FindAll("main article").Should().HaveCount(2, "the conversation stays beside the screen");

        page.Find("aside input[role=switch]").Change(true);

        page.Find("aside button.dv-sandbox-picture").GetAttribute("aria-label").Should().StartWith("Marshal's sandbox screen. Tap to click there");
        page.Find("aside .dv-sandbox-screen [role=status]").TextContent.Should().Contain("You have control");
    }

    [Fact]
    public void A_bot_whose_sandbox_has_no_screen_offers_no_way_to_one()
    {
        Server.Answer("GET", $"{space}/bots/{botId}/sandbox", ScreenStatus(desktop: false));

        var page = RenderMain();

        page.WaitForAssertion(() => page.FindAll("main article").Should().HaveCount(2));
        page.FindAll("a.dv-shell-side[href$=\"/screen\"]").Should().BeEmpty();
    }

    [Fact]
    public void The_screen_opened_for_a_bot_without_one_says_how_to_give_it_one()
    {
        Server.Answer("GET", $"{space}/bots/{botId}/sandbox", ScreenStatus(desktop: false));
        Navigation.NavigateTo($"/talk/{botId}/screen");

        var page = Render<Talk>(parameters => parameters.Add(talk => talk.BotId, botId).Add(talk => talk.Rail, "screen"));

        page.WaitForAssertion(() => page.Find("aside h2").TextContent.Should().Be("Marshal has no screen"));
        page.Find("aside h2 + p").TextContent.Should().Be("This sandbox has no screen. Turn on Desktop in the Sandbox section of the bot's settings to give it one.");
        RailLink(page, "Open its settings").GetAttribute("href").Should().Be($"/bots/{botId}");
    }

    [Fact]
    public void The_screen_opened_for_a_bot_without_a_sandbox_says_how_to_give_it_one()
    {
        Server.Answer("GET", $"{space}/bots/{botId}/sandbox", ScreenStatus(desktop: false) with { HasSandbox = false, State = "none" });

        var page = RenderScreen();

        page.WaitForAssertion(() => page.Find("aside h2").TextContent.Should().Be("Marshal has no sandbox"));
        page.Find("aside h2 + p").TextContent.Should().Be("Give it a sandbox with Desktop on, in the Sandbox section of its settings.");
        RailLink(page, "Open its settings").GetAttribute("href").Should().Be($"/bots/{botId}");
    }

    [Fact]
    public void The_screen_on_a_server_that_offers_none_says_so_and_offers_the_terminal_instead()
    {
        Server.Answer("GET", $"{space}/bots/{botId}/sandbox", ScreenStatus(desktop: false) with { DesktopOffered = false });

        var page = RenderScreen();

        page.WaitForAssertion(() => page.Find("aside h2").TextContent.Should().Be("Marshal has no screen"));
        page.Find("aside h2 + p").TextContent.Should().Be("This server does not offer sandboxes with a screen. Its operator decides that.");
        RailLink(page, "Open its terminal").GetAttribute("href").Should().Be($"/bots/{botId}/sandbox");
    }

    [Fact]
    public void The_screen_for_a_bot_without_a_sandbox_on_a_server_that_offers_none_says_the_server_offers_none()
    {
        Server.Answer("GET", $"{space}/bots/{botId}/sandbox", ScreenStatus(desktop: false) with { HasSandbox = false, State = "none", DesktopOffered = false });

        var page = RenderScreen();

        page.WaitForAssertion(() => page.Find("aside h2").TextContent.Should().Be("Marshal has no screen"));
        page.Find("aside h2 + p").TextContent.Should().Be("This server does not offer sandboxes with a screen. Its operator decides that.");
        RailLink(page, "Open its settings").GetAttribute("href").Should().Be($"/bots/{botId}");
    }

    [Fact]
    public async Task The_screen_that_could_not_load_says_why_offers_to_try_again_and_recovers_on_the_next_read()
    {
        Server.TimeOut("GET", $"{space}/bots/{botId}/sandbox");
        var page = RenderScreen();
        page.WaitForAssertion(() => page.Find("aside h2").TextContent.Should().Be("Drover could not load the screen"));
        page.Find("aside h2 + p").TextContent.Should().Be(Failures.TooSlow);
        page.FindAll("aside button").Should().Contain(button => button.TextContent.Trim() == "Try again");

        Server.Answer("GET", $"{space}/bots/{botId}/sandbox", ScreenStatus(desktop: true) with { State = "paused" });
        await Elapse(TimeSpan.FromSeconds(10));

        page.WaitForAssertion(() => page.Find("aside .dv-screen-panel-state .dv-badge").TextContent.Should().Be("Paused"));
        page.FindAll("aside h2").Should().BeEmpty("the read that worked replaced the problem");
    }

    [Fact]
    public async Task The_screen_opened_before_the_bot_starts_its_sandbox_says_so_then_shows_the_picture_after_the_next_read()
    {
        Server.Answer("GET", $"{space}/bots/{botId}/sandbox", ScreenStatus(desktop: true) with { State = "none", LastUsedAt = null });
        Server.AnswerBytes("POST", $"{space}/bots/{botId}/sandbox/screen", ScreenPng, "image/png");
        var page = RenderScreen();
        page.WaitForAssertion(() => page.Find("aside .dv-sandbox-screen").TextContent.Should().Contain("No sandbox yet. It starts when the bot first uses it."));
        Server.Sent("POST", $"{space}/bots/{botId}/sandbox/screen").Should().BeEmpty("there is nothing to look at yet");

        Server.Answer("GET", $"{space}/bots/{botId}/sandbox", ScreenStatus(desktop: true));
        time.Advance(TimeSpan.FromSeconds(10));

        page.WaitForAssertion(() => page.Find("aside .dv-screen-panel-state .dv-badge").TextContent.Should().Be("Running"));
        await Elapse(SandboxWords.NextPictureAfter(0));
        page.WaitForAssertion(() => page.Find("aside img.dv-sandbox-image"));
    }

    [Fact]
    public void A_state_read_answered_with_a_page_does_not_stop_the_screens_reads_after_it()
    {
        Server.Answer("GET", $"{space}/bots/{botId}/sandbox", ScreenStatus(desktop: true) with { State = "none", LastUsedAt = null });
        var page = RenderScreen();
        page.WaitForAssertion(() => page.Find("aside .dv-screen-panel-state .dv-badge").TextContent.Should().Be("No sandbox yet"));

        Server.AnswerPage("GET", $"{space}/bots/{botId}/sandbox");
        time.Advance(TimeSpan.FromSeconds(10));
        page.WaitForAssertion(() => Server.Sent("GET", $"{space}/bots/{botId}/sandbox").Should().HaveCount(3));
        Server.Answer("GET", $"{space}/bots/{botId}/sandbox", ScreenStatus(desktop: true) with { State = "paused" });
        time.Advance(TimeSpan.FromSeconds(10));

        page.WaitForAssertion(() => page.Find("aside .dv-screen-panel-state .dv-badge").TextContent.Should().Be("Paused"));
    }

    [Fact]
    public void A_screen_whose_first_read_is_answered_with_a_page_says_so_and_offers_to_try_again()
    {
        Server.AnswerPage("GET", $"{space}/bots/{botId}/sandbox");

        var page = RenderScreen();

        page.WaitForAssertion(() => page.Find("aside h2").TextContent.Should().Be("Drover could not load the screen"));
        page.Find("aside h2 + p").TextContent.Should().Be(Failures.Unreadable);
        page.FindAll("aside button").Should().Contain(button => button.TextContent.Trim() == "Try again");
    }

    [Fact]
    public void A_state_read_that_takes_too_long_does_not_stop_the_screens_reads_after_it()
    {
        Server.Answer("GET", $"{space}/bots/{botId}/sandbox", ScreenStatus(desktop: true) with { State = "none", LastUsedAt = null });
        var page = RenderScreen();
        page.WaitForAssertion(() => page.Find("aside .dv-screen-panel-state .dv-badge").TextContent.Should().Be("No sandbox yet"));

        Server.TimeOut("GET", $"{space}/bots/{botId}/sandbox");
        time.Advance(TimeSpan.FromSeconds(10));
        page.WaitForAssertion(() => Server.Sent("GET", $"{space}/bots/{botId}/sandbox").Should().HaveCount(3, "the page's own read, the screen's first, and the one that timed out"));
        Server.Answer("GET", $"{space}/bots/{botId}/sandbox", ScreenStatus(desktop: true) with { State = "paused" });
        time.Advance(TimeSpan.FromSeconds(10));

        page.WaitForAssertion(() => page.Find("aside .dv-screen-panel-state .dv-badge").TextContent.Should().Be("Paused"));
    }

    [Fact]
    public void A_person_who_may_not_use_the_screen_is_told_so_and_can_still_talk()
    {
        Server.Answer("GET", $"{space}/bots/{botId}/sandbox", HttpStatusCode.Forbidden);
        Navigation.NavigateTo($"/talk/{botId}/screen");

        var page = Render<Talk>(parameters => parameters.Add(talk => talk.BotId, botId).Add(talk => talk.Rail, "screen"));

        page.WaitForAssertion(() => page.Find("aside .dv-screen-panel-hint").TextContent
            .Should().Be("Owners and admins of this space watch and use its bots' screens. You can still talk to Marshal."));
        page.FindAll("main textarea").Should().ContainSingle();
    }

    #endregion

    #region Summary

    [Fact]
    public void The_summary_opens_beside_the_main_conversation_and_says_what_it_covers_and_when_it_was_written()
    {
        Server.Answer("GET", $"{space}/threads/{threadId}/summary",
            new ConversationSummary("Sample **summary** of what was said.", 24, DateTimeOffset.UnixEpoch));

        var page = RenderSummary();

        page.WaitForAssertion(() => page.Find("aside .dv-markdown strong").TextContent.Should().Be("summary"));
        page.Find("aside").GetAttribute("aria-label").Should().Be("Summary of the conversation with Marshal");
        page.Find("aside h1").TextContent.Should().Be("Summary");
        page.Find("aside .dv-summary-facts").TextContent.Should().StartWith("Covers 24 messages · written ");
        var close = page.Find("aside a.dv-shell-up");
        close.GetAttribute("aria-label").Should().Be("Close the summary");
        close.GetAttribute("href").Should().Be($"/talk/{botId}");
        var way = page.Find("a.dv-shell-side[href$=\"/summary\"]");
        way.TextContent.Should().Be("Summary");
        way.GetAttribute("aria-current").Should().Be("true");
        page.Find("a.dv-shell-side[href$=\"/side\"]").HasAttribute("aria-current").Should().BeFalse();
        page.FindAll("main article").Should().HaveCount(2, "the main conversation stays beside it");
    }

    [Fact]
    public void A_conversation_with_no_summary_yet_says_when_one_is_written()
    {
        Server.Answer("GET", $"{space}/threads/{threadId}/summary", new ConversationSummary(null, 0, null));

        var page = RenderSummary();

        page.WaitForAssertion(() => page.Find("aside .dv-summary-hint").TextContent
            .Should().Be("No summary yet. Drover writes one when this conversation is long enough to need it, and rewrites it as the conversation goes on."));
        page.FindAll("aside .dv-markdown").Should().BeEmpty();
    }

    [Fact]
    public void A_server_that_does_not_write_summaries_says_so_and_still_shows_one_written_before()
    {
        Server.Answer("GET", $"{space}/threads/{threadId}/summary", new ConversationSummary(null, 0, null, ServerWrites: false));
        var page = RenderSummary();
        page.WaitForAssertion(() => page.Find("aside .dv-summary-hint").TextContent
            .Should().Be("This server does not write summaries. Its operator has them turned off."));

        Server.Answer("GET", $"{space}/threads/{threadId}/summary", new ConversationSummary("Sample summary.", 12, DateTimeOffset.UnixEpoch, ServerWrites: false));
        var later = RenderSummary();

        later.WaitForAssertion(() => later.Find("aside .dv-markdown").TextContent.Trim().Should().Be("Sample summary."));
        later.Find("aside .dv-note").TextContent.Trim().Should().Be("This server no longer writes summaries, so this one stays as it is.");
    }

    [Fact]
    public void A_summary_that_fails_to_load_says_why_and_loads_on_a_second_try()
    {
        Server.Answer("GET", $"{space}/threads/{threadId}/summary", HttpStatusCode.InternalServerError);
        var page = RenderSummary();
        page.WaitForAssertion(() => page.Find("aside .dv-empty-title").TextContent.Should().Be("Drover could not load the summary"));

        Server.Answer("GET", $"{space}/threads/{threadId}/summary", new ConversationSummary("Sample summary.", 1, DateTimeOffset.UnixEpoch));
        page.Find("aside .dv-empty button").Click();

        page.WaitForAssertion(() => page.Find("aside .dv-summary-facts").TextContent.Should().StartWith("Covers 1 message · written "));
    }

    [Fact]
    public async Task When_the_bot_stops_working_the_summary_and_the_crew_are_read_again()
    {
        Server.Answer("GET", $"{space}/threads/{threadId}/summary", new ConversationSummary(null, 0, null));
        var events = RunStarts();
        var page = RenderSummary();
        page.WaitForElement("main article");
        await page.Find("main textarea").InputAsync(new ChangeEventArgs { Value = "Another sample question" });
        await page.Find("main form").SubmitAsync();
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));
        page.WaitForAssertion(() => page.Find("main .dv-composer button.dv-button").TextContent.Trim().Should().Be("Stop"));

        Server.Answer("GET", $"{space}/threads/{threadId}/summary", new ConversationSummary("Sample summary, rewritten.", 26, DateTimeOffset.UnixEpoch));
        Server.Answer("GET", $"{space}/bots", new[] { Marshal, Helper with { MadeByBotId = botId } });
        await events.Send(Event(2, RunEventTypes.RunCompleted, new RunStatusChanged("completed", null, null, 1, null)));
        await events.End();

        page.WaitForAssertion(() => page.Find("aside .dv-markdown").TextContent.Trim().Should().Be("Sample summary, rewritten."));
        page.WaitForAssertion(() => page.FindAll("nav[aria-label=Crew] a.dv-crew-bot").Should().HaveCount(2, "a bot the lead made shows up without a reload"));
    }

    #endregion

    #region Side Chats

    [Fact]
    public void The_way_to_the_side_chats_says_how_many_there_are()
    {
        var page = RenderMain();
        page.WaitForAssertion(() => page.Find("a.dv-shell-side[href$=\"/side\"]").TextContent.Should().Be("Side chats"));
        page.Find("a.dv-shell-side[href$=\"/side\"]").GetAttribute("href").Should().Be($"/talk/{botId}/side");
        page.Find("a.dv-shell-side[href$=\"/side\"]").HasAttribute("aria-current").Should().BeFalse();
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
        page.Find("a.dv-shell-side[href$=\"/side\"]").TextContent.Should().Be("1 side chat");
        page.Find("a.dv-shell-side[href$=\"/side\"]").GetAttribute("aria-current").Should().Be("true");
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
        page.FindAll(".dv-composer button.dv-button").Select(button => button.TextContent.Trim()).Should().Equal("Stop");
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
        page.Find(".dv-composer button.dv-button").TextContent.Trim().Should().Be("Stop");

        await events.Send(Event(4, RunEventTypes.MessageDelta, new MessageDelta(replyId, 0, "text", " answer")));
        page.WaitForAssertion(() => page.FindAll("article .dv-markdown")[^1].TextContent.Trim().Should().Be("Second sample answer"));

        var reply = Said("assistant", "Second sample answer") with { Id = replyId };
        Server.Answer("GET", $"{space}/threads/{threadId}/messages", new[] { Said("user", "Sample question"), Said("assistant", "Sample answer"), Said("user", "Another sample question"), reply });
        await events.Send(Event(5, RunEventTypes.MessageCompleted, new MessageCompleted(reply)));
        await events.Send(Event(6, RunEventTypes.RunCompleted, new RunStatusChanged("completed", null, null, 1, null)));
        await events.End();

        page.WaitForAssertion(() => page.Find(".dv-composer button.dv-button").TextContent.Trim().Should().Be("Send"));
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
        page.WaitForAssertion(() => page.Find(".dv-composer button.dv-button").TextContent.Trim().Should().Be("Stop"));

        var steering = Said("user", "Keep it short") with { Position = null, Steers = true };
        Server.Answer("POST", $"{space}/threads/{threadId}/messages", new SendMessageResponse(steering, Run("running")), HttpStatusCode.Accepted);
        await page.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Keep it short" });
        await page.Find("form").SubmitAsync();

        page.WaitForAssertion(() => page.Find(".dv-message-pending").TextContent.Should().Be("Sent. Marshal reads it at its next step."));
        page.FindAll(".dv-message-bubble")[^1].TextContent.Should().Be("Keep it short");
        page.Find(".dv-composer button.dv-button").TextContent.Trim().Should().Be("Stop", "the run is still the same one");

        await events.Send(Event(2, RunEventTypes.MessageDelivered, new MessageDelivered(steering with { Position = 3 })));

        page.WaitForAssertion(() => page.FindAll(".dv-message-pending").Should().BeEmpty());
        page.FindAll(".dv-message-bubble").Select(bubble => bubble.TextContent).Should().Equal("Sample question", "Another sample question", "Keep it short");
    }

    [Fact]
    public async Task A_run_that_fails_ends_the_conversation_with_what_failed_and_says_it_aloud()
    {
        var events = RunStarts();
        var page = RenderMain();
        page.WaitForElement("article");
        await page.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Another sample question" });
        await page.Find("form").SubmitAsync();
        Main(Thread(null) with { Attention = new(false, true, false, Failure) });

        await events.Send(Event(1, RunEventTypes.RunFailed, new RunStatusChanged("failed", null, new("invalid_key", "The provider rejected the API key."), 1, null)));
        await events.End();

        page.WaitForAssertion(() => page.Find("main .dv-failure h3").TextContent
            .Should().Be("Marshal could not reach its model. Check the model connection, then try again."));
        page.Find(".dv-messages ~ [aria-live]").TextContent.Should().Be(page.Find("main .dv-failure h3").TextContent);
        page.Find(".dv-composer button.dv-button").TextContent.Trim().Should().Be("Send");
        page.Find("main .dv-failure").TextContent.Should().NotContain("rejected the API key", "the server's report is shown only when asked for");
    }

    [Fact]
    public async Task Try_again_starts_a_new_run_from_the_same_message_and_follows_it()
    {
        Main(Thread(null) with { Attention = new(false, true, false, Failure) });
        Server.Answer("POST", $"{space}/runs/{failedRunId}/retry", Run("queued"), HttpStatusCode.Accepted);
        var events = Server.Stream($"{space}/runs/{runId}/events");
        var page = RenderMain();

        page.WaitForAssertion(() => page.Find("main .dv-failure button").TextContent.Trim().Should().Be("Try again"));
        await page.Find("main .dv-failure button").ClickAsync(new MouseEventArgs());
        await events.Send(Event(1, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 1, null)));

        page.WaitForAssertion(() => page.Find(".dv-composer button.dv-button").TextContent.Trim().Should().Be("Stop"));
        page.FindAll("main .dv-failure").Should().BeEmpty("a run after the failure is under way");
        Server.Sent("POST", $"{space}/runs/{failedRunId}/retry").Should().ContainSingle();

        // Focus goes to the composer, where Stop took the place of Try again. The composer is the one thing on this
        // screen that moves focus, and this page renders no element ids to compare a reference with.
        page.WaitForAssertion(() => JSInterop.VerifyFocusAsyncInvoke().Arguments[0].Should().BeOfType<ElementReference>());
    }

    [Fact]
    public async Task A_retry_the_server_refuses_says_why()
    {
        Main(Thread(null) with { Attention = new(false, false, false, Failure) });
        Server.Refuse("POST", $"{space}/runs/{failedRunId}/retry", HttpStatusCode.Conflict, "This run cannot be tried again: something came after it, or it did not fail.");
        var page = RenderMain();
        page.WaitForElement("main .dv-failure button");

        await page.Find("main .dv-failure button").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.Find("main [role=alert]").TextContent.Trim()
            .Should().StartWith("It was not tried again. This run cannot be tried again"));
        page.Find("main .dv-failure button").HasAttribute("disabled").Should().BeFalse("the button the person pressed keeps focus");
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
        page.WaitForAssertion(() => page.Find(".dv-composer button.dv-button").TextContent.Trim().Should().Be("Stop"));

        await page.Find(".dv-composer button.dv-button").ClickAsync(new MouseEventArgs());
        page.WaitForAssertion(() => Server.Sent("POST", $"{space}/runs/{runId}/cancel").Should().ContainSingle());
        await events.Send(Event(2, RunEventTypes.RunCancelled, new RunStatusChanged("cancelled", null, null, 1, null)));
        await events.End();

        page.WaitForAssertion(() => page.Find("main [role=status] .dv-note").TextContent.Trim().Should().Be("You stopped this reply."));
        page.Find(".dv-composer button.dv-button").TextContent.Trim().Should().Be("Send");
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
        page.Find(".dv-composer button.dv-button").TextContent.Trim().Should().Be("Stop");
    }

    #endregion

    #region Files

    [Fact]
    public async Task A_file_attached_is_uploaded_at_once_and_goes_with_the_message()
    {
        var fileId = Guid.CreateVersion7();
        AttachmentsAllowed();
        Server.Answer("POST", $"{space}/threads/{threadId}/files", new FileResponse(fileId, "notes.txt", "text/plain", FileKinds.Text, 3, Preview: "one"), HttpStatusCode.Created);
        RunStarts(string.Empty);
        var page = RenderMain();
        page.WaitForElement("article");

        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("one", "notes.txt"));

        page.WaitForAssertion(() => page.Find(".dv-attached-status").TextContent.Should().Be("3 bytes"));
        Server.Addresses.Should().Contain($"POST {space}/threads/{threadId}/files?name=notes.txt");
        await page.Find("form").SubmitAsync();

        page.WaitForAssertion(() => Server.Sent("POST", $"{space}/threads/{threadId}/messages").Should().ContainSingle()
            .Which.Should().Contain($"\"fileIds\":[\"{fileId}\"]"));
        page.WaitForAssertion(() => page.FindAll(".dv-attached-file").Should().BeEmpty("the files went with the message"));
    }

    [Fact]
    public async Task A_file_the_server_refuses_says_why_before_anything_is_sent_and_holds_the_message()
    {
        AttachmentsAllowed();
        Server.Answer(
            "POST",
            $"{space}/threads/{threadId}/files",
            new { title = "Marshal has no way to open this kind of file. It needs a sandbox for it.", status = 422, refusal = FileRefusals.NothingCanOpenIt },
            HttpStatusCode.UnprocessableEntity);
        var page = RenderMain();
        page.WaitForElement("article");

        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([0, 1, 2], "tool.bin"));

        page.WaitForAssertion(() => page.Find(".dv-attached-problem .dv-attached-status").TextContent
            .Should().Be("Marshal has no way to open this kind of file. It needs a sandbox for it."));
        await page.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Here it is" });
        page.Find("button[type=submit]").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void A_file_refused_because_the_conversation_is_private_says_so_in_the_composer()
    {
        AttachmentsAllowed();
        Server.Answer(
            "POST",
            $"{space}/threads/{threadId}/files",
            new { title = "This conversation is private, and others in this space can reach Marshal's sandbox, so this file cannot go there.", status = 422, refusal = FileRefusals.PrivateConversation },
            HttpStatusCode.UnprocessableEntity);
        var page = RenderMain();
        page.WaitForElement("article");

        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([0, 1, 2], "report.pdf"));

        page.WaitForAssertion(() => page.Find(".dv-attached-problem .dv-attached-status").TextContent
            .Should().Be("This conversation is private, and others in this space can reach Marshal's sandbox, so this file cannot go there."));
        page.Find("[aria-label='Files attached to this message'] ~ [role=status]").TextContent.Should().Be("1 file cannot be sent.");
        page.Find("button[aria-label='Remove report.pdf']").Should().NotBeNull();
        page.Find("button[type=submit]").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public async Task A_file_past_the_servers_limit_is_refused_without_being_sent()
    {
        Server.Answer("GET", $"{space}/threads/{threadId}/attachment-rules", new AttachmentRules(2, 10, false, false));
        var page = RenderMain();
        page.WaitForElement("article");

        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("three", "big.txt"));

        page.WaitForAssertion(() => page.Find(".dv-attached-status").TextContent.Should().Be("Larger than this server takes."));
        Server.Sent("POST", $"{space}/threads/{threadId}/files").Should().BeEmpty();
    }

    [Fact]
    public async Task Removing_an_uploaded_file_removes_it_from_the_server_too()
    {
        var fileId = Guid.CreateVersion7();
        AttachmentsAllowed();
        Server.Answer("POST", $"{space}/threads/{threadId}/files", new FileResponse(fileId, "notes.txt", "text/plain", FileKinds.Text, 3), HttpStatusCode.Created);
        Server.Answer("DELETE", $"{space}/files/{fileId}", HttpStatusCode.NoContent);
        var page = RenderMain();
        page.WaitForElement("article");
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("one", "notes.txt"));
        page.WaitForAssertion(() => page.Find(".dv-attached-status").TextContent.Should().Be("3 bytes"));

        await page.Find("[aria-label='Remove notes.txt']").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => Server.Requests.Should().Contain(request => request.Method == "DELETE" && request.Path == $"{space}/files/{fileId}"));
        page.FindAll(".dv-attached-file").Should().BeEmpty();
    }

    [Fact]
    public async Task The_first_file_of_a_new_side_chat_makes_its_thread_once_even_when_a_message_races_it()
    {
        var fileId = Guid.CreateVersion7();
        var making = Server.Hold("POST", $"{space}/bots/{botId}/threads");
        Server.Answer("POST", $"{space}/bots/{botId}/threads", Side with { Title = null }, HttpStatusCode.Created);
        Server.Answer("GET", $"{space}/threads/{sideId}", Side with { Title = null });
        Server.Answer("GET", $"{space}/threads/{sideId}/attachment-rules", new AttachmentRules(1024, 10, true, true));
        Server.Answer("POST", $"{space}/threads/{sideId}/files", new FileResponse(fileId, "notes.txt", "text/plain", FileKinds.Text, 3), HttpStatusCode.Created);
        Server.Answer("POST", $"{space}/threads/{sideId}/messages",
            new SendMessageResponse(Said("user", "First sample words") with { ThreadId = sideId }, Run("queued") with { ThreadId = sideId }), HttpStatusCode.Accepted);
        Server.Stream($"{space}/runs/{runId}/events");
        var page = RenderSide("new");
        page.WaitForAssertion(() => page.Find("aside h1").TextContent.Should().Be("New side chat"));

        await page.Find("aside textarea").InputAsync(new ChangeEventArgs { Value = "First sample words" });

        // The message asks for the thread first. Choosing the file waits for its handler, so the thread is let
        // through from aside while both are waiting on it.
        var submitted = page.Find("aside form").SubmitAsync();
        _ = ReleaseSoon(making);
        page.FindComponents<InputFile>()[^1].UploadFiles(InputFileContent.CreateFromText("one", "notes.txt"));
        await submitted;

        page.WaitForAssertion(() => Address.Should().Be($"talk/{botId}/side/{sideId}", string.Join("\n", Server.Addresses)));
        page.Render(parameters => parameters.Add(talk => talk.SideChat, sideId.ToString()));
        page.WaitForAssertion(() => page.Find("aside .dv-attached-status").TextContent.Should().Be("3 bytes"));
        Server.Sent("POST", $"{space}/bots/{botId}/threads").Should().ContainSingle("the message and the file share one new thread");
        Server.Sent("POST", $"{space}/threads/{sideId}/messages").Should().ContainSingle();
        Server.Sent("POST", $"{space}/threads/{sideId}/files").Should().ContainSingle("the file went to the thread the message went to");
    }

    [Fact]
    public async Task An_upload_that_fails_with_no_reason_from_the_server_says_so_and_holds_the_message()
    {
        AttachmentsAllowed();
        Server.Answer("POST", $"{space}/threads/{threadId}/files", HttpStatusCode.InternalServerError);
        var page = RenderMain();
        page.WaitForElement("article");

        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("one", "notes.txt"));
        await page.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Here" });

        page.WaitForAssertion(() => page.Find(".dv-attached-problem .dv-attached-status").TextContent
            .Should().Be("Not uploaded. Remove it and attach it again."));
        page.Find("button[type=submit]").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public async Task A_file_taken_off_while_it_uploads_is_removed_from_the_server_when_it_arrives()
    {
        var fileId = Guid.CreateVersion7();
        AttachmentsAllowed();
        var uploading = Server.Hold("POST", $"{space}/threads/{threadId}/files");
        Server.Answer("POST", $"{space}/threads/{threadId}/files", new FileResponse(fileId, "notes.txt", "text/plain", FileKinds.Text, 3), HttpStatusCode.Created);
        Server.Answer("DELETE", $"{space}/files/{fileId}", HttpStatusCode.NoContent);
        var page = RenderMain();
        page.WaitForElement("article");
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("one", "notes.txt"));
        page.WaitForAssertion(() => page.Find(".dv-attached-status").TextContent.Should().StartWith("Uploading"));

        await page.Find("[aria-label='Remove notes.txt']").ClickAsync(new MouseEventArgs());
        uploading.SetResult();

        page.WaitForAssertion(() => Server.Requests.Should().Contain(request => request.Method == "DELETE" && request.Path == $"{space}/files/{fileId}"));
        page.FindAll(".dv-attached-file").Should().BeEmpty();
    }

    [Fact]
    public async Task A_file_attached_while_a_message_is_on_its_way_stays_for_the_next_one()
    {
        var sentId = Guid.CreateVersion7();
        var laterId = Guid.CreateVersion7();
        AttachmentsAllowed();
        Server.Answer("POST", $"{space}/threads/{threadId}/files", new FileResponse(sentId, "first.txt", "text/plain", FileKinds.Text, 3), HttpStatusCode.Created);
        var sending = Server.Hold("POST", $"{space}/threads/{threadId}/messages");
        RunStarts("With a file");
        var page = RenderMain();
        page.WaitForElement("article");
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("one", "first.txt"));
        page.WaitForAssertion(() => page.Find(".dv-attached-status").TextContent.Should().Be("3 bytes"));
        await page.Find("textarea").InputAsync(new ChangeEventArgs { Value = "With a file" });

        var submitted = page.Find("form").SubmitAsync();
        Server.Answer("POST", $"{space}/threads/{threadId}/files", new FileResponse(laterId, "later.txt", "text/plain", FileKinds.Text, 3), HttpStatusCode.Created);
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("two", "later.txt"));
        page.WaitForAssertion(() => page.FindAll(".dv-attached-file").Should().HaveCount(2));
        sending.SetResult();
        await submitted;

        page.WaitForAssertion(() => page.FindAll(".dv-attached-name").Select(name => name.TextContent).Should().Equal("later.txt"));
        Server.Sent("POST", $"{space}/threads/{threadId}/messages").Single().Should().Contain(sentId.ToString()).And.NotContain(laterId.ToString());
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

    private static async Task ReleaseSoon(TaskCompletionSource gate)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        gate.SetResult();
    }

    private void AttachmentsAllowed() =>
        Server.Answer("GET", $"{space}/threads/{threadId}/attachment-rules", new AttachmentRules(1024, 10, true, true));

    private IRenderedComponent<Talk> RenderMain()
    {
        Navigation.NavigateTo($"/talk/{botId}");
        return Render<Talk>(parameters => parameters.Add(page => page.BotId, botId));
    }

    private static IElement RailLink(IRenderedComponent<Talk> page, string name) =>
        page.FindAll("aside a").Single(link => link.TextContent.Trim() == name);

    /// <summary>Moves the page's clock on in small steps, giving the page a moment after each.</summary>
    private async Task Elapse(TimeSpan span)
    {
        for (var moved = TimeSpan.Zero; moved < span; moved += TimeSpan.FromMilliseconds(100))
        {
            time.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Delay(TimeSpan.FromMilliseconds(5), Xunit.TestContext.Current.CancellationToken);
        }
    }

    private IRenderedComponent<Talk> RenderScreen()
    {
        Navigation.NavigateTo($"/talk/{botId}/screen");
        return Render<Talk>(parameters => parameters.Add(talk => talk.BotId, botId).Add(talk => talk.Rail, "screen"));
    }

    private static BotSandboxStatus ScreenStatus(bool desktop) =>
        new(Available: true, HasSandbox: true, "running", DateTimeOffset.UnixEpoch, Desktop: desktop, BotWorking: false, DesktopOffered: true);

    // The header of a 1024 by 768 PNG: as much of one as the screen reads.
    private static byte[] ScreenPng { get; } =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 4, 0, 0, 0, 3, 0, 8, 6, 0, 0, 0, 0, 0, 0, 0];

    private IRenderedComponent<Talk> RenderSummary()
    {
        Navigation.NavigateTo($"/talk/{botId}/summary");
        return Render<Talk>(parameters => parameters.Add(page => page.BotId, botId).Add(page => page.Rail, "summary"));
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
