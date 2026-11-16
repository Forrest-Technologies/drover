namespace Drover.Ui.Tests.Screens;

/// <summary>
/// The sandbox screen against a stand-in server: each state it can be in, the power controls and their confirming
/// dialogs, taking control of the screen, and the terminal. Every name and value here is a sample.
/// </summary>
public sealed class BotSandboxTests : ScreenTest
{
    #region Private Fields

    private const string ModulePath = "./_content/Drover.Ui/Bots/SandboxScreen.razor.js";

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly string sandbox = $"/api/spaces/{SpaceId}/bots/{botId}/sandbox";

    private static readonly byte[] picture = Png(1024, 768);

    /// <summary>The page's clock, moved by hand: the state reads and the picture's own timer wait on it.</summary>
    private static readonly DateTimeOffset now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly BunitJSModuleInterop module;

    private readonly FakeTimeProvider time = new(now);

    /// <summary>How far the page's clock moves at a time while a test waits on it.</summary>
    private readonly TimeSpan step = TimeSpan.FromMilliseconds(20);

    #endregion

    #region Constructors

    public BotSandboxTests()
    {
        SignedIn();
        Services.AddSingleton<TimeProvider>(time);
        module = JSInterop.SetupModule(ModulePath);
        Server.Answer("GET", $"/api/spaces/{SpaceId}/bots/{botId}", new BotSummary(botId, "Sample bot", "sample", "Sample instructions", Guid.Parse("0196f1a0-0000-7000-8000-000000000003"), "sample-model", "active"));
        Server.AnswerBytes("POST", $"{sandbox}/screen", picture, "image/png");
        Server.Answer("POST", $"{sandbox}/input", HttpStatusCode.NoContent);
    }

    #endregion

    #region States

    [Fact]
    public void A_running_sandbox_with_a_screen_shows_its_state_in_words_its_power_controls_and_the_picture()
    {
        Answer(Status("running"));

        var page = RenderSandbox();

        page.WaitForAssertion(() => page.Find("img.dv-sandbox-image").GetAttribute("alt").Should().StartWith("Sample bot's sandbox screen"));
        page.Find("h1").TextContent.Should().Be("Sample bot's sandbox");
        page.Find(".dv-sandbox-state .dv-badge").TextContent.Should().Be("Running");
        page.Find(".dv-sandbox-used").TextContent.Should().Be("Used 3 minutes ago");
        var power = page.Find("[role=group][aria-label=Power]");
        power.QuerySelectorAll("button").Select(button => button.TextContent.Trim()).Should().Equal("Pause", "Rebuild", "Turn off");
        power.QuerySelectorAll("button").Should().OnlyContain(button => !button.HasAttribute("disabled"));
        page.FindAll("[role=tab]").Select(tab => tab.TextContent.Trim()).Should().Equal("Screen", "Terminal");
        var image = page.Find("img.dv-sandbox-image");
        image.GetAttribute("width").Should().Be("1024");
        image.GetAttribute("height").Should().Be("768");
        image.GetAttribute("src").Should().StartWith("data:image/png;base64,");
        page.Find(".dv-sandbox-screen [role=status]").TextContent.Should().Contain("View only");
    }

    [Fact]
    public void A_paused_sandbox_offers_resume_and_does_not_ask_for_the_screen()
    {
        Answer(Status("paused"));

        var page = RenderSandbox();

        page.WaitForAssertion(() => page.Find(".dv-sandbox-state .dv-badge").TextContent.Should().Be("Paused"));
        page.Find("[role=group][aria-label=Power] .dv-button-primary").TextContent.Trim().Should().Be("Resume");
        page.Find(".dv-sandbox-screen").TextContent.Should().Contain("Resume it to see the screen");
        Server.Sent("POST", $"{sandbox}/screen").Should().BeEmpty("looking at a paused sandbox would resume it");
    }

    [Fact]
    public void No_sandbox_yet_says_how_one_starts_and_explains_the_disabled_controls()
    {
        Answer(Status("none", lastUsed: null));

        var page = RenderSandbox();

        page.WaitForAssertion(() => page.Find(".dv-sandbox-state .dv-badge").TextContent.Should().Be("No sandbox yet"));
        page.Find(".dv-sandbox-used").TextContent.Should().Be("Not used yet");
        page.FindAll("[role=group][aria-label=Power] button").Should().OnlyContain(button => button.HasAttribute("disabled"));
        page.Find(".dv-sandbox-head").TextContent.Should().Contain("Nothing to pause, rebuild, or turn off yet.");
        page.Find(".dv-sandbox-screen").TextContent.Should().Contain("It starts when the bot first uses it.");
        Server.Sent("POST", $"{sandbox}/screen").Should().BeEmpty();
    }

    [Fact]
    public void A_bot_whose_last_start_did_not_finish_says_so_and_offers_to_start_it()
    {
        Answer(Status("none", lastUsed: null) with { StartFailed = true });

        var page = RenderSandbox();

        page.WaitForAssertion(() => page.Find(".dv-sandbox-state .dv-badge").TextContent.Should().Be("Start did not finish"));
        page.Find(".dv-sandbox-screen").TextContent.Should().Contain("The last start of this sandbox did not finish. Start it here to see why.")
            .And.NotContain("It starts when the bot first uses it.");
        WakeButton(page).TextContent.Trim().Should().Be("Start the sandbox");
        Server.Sent("POST", $"{sandbox}/screen").Should().BeEmpty("looking alone starts nothing");
    }

    [Fact]
    public void A_sandbox_being_replaced_says_so_in_words()
    {
        Answer(Status("replacing", lastUsed: null));

        var page = RenderSandbox();

        page.WaitForAssertion(() => page.Find(".dv-sandbox-state .dv-badge").TextContent.Should().Be("Being replaced"));
        page.Find(".dv-sandbox-head").TextContent.Should().Contain("The old sandbox is being taken down.");
    }

    [Fact]
    public void A_sandbox_without_a_screen_has_only_the_terminal()
    {
        Answer(Status("running") with { Desktop = false });

        var page = RenderSandbox();

        page.WaitForAssertion(() => page.FindAll("[role=tab]").Select(tab => tab.TextContent.Trim()).Should().Equal("Terminal"));
        page.Find("[role=tabpanel] label").TextContent.Should().Be("Command");
    }

    [Fact]
    public void A_sandbox_without_a_screen_says_why_and_links_to_the_setting_that_gives_it_one()
    {
        Answer(Status("running") with { Desktop = false });

        var page = RenderSandbox();

        page.WaitForAssertion(() => Words(Paragraph(page, "This sandbox has no screen."))
            .Should().Be("This sandbox has no screen. Turn on Desktop in the Sandbox section of the bot's settings to give it one. Open its settings"));
        Link(page, "Open its settings").GetAttribute("href").Should().Be(Addresses.Bot(botId));
    }

    [Fact]
    public void On_a_server_that_offers_no_screens_the_sandbox_says_so_and_offers_no_setting()
    {
        Answer(Status("running") with { Desktop = false, DesktopOffered = false });

        var page = RenderSandbox();

        page.WaitForAssertion(() => Words(Paragraph(page, "This server does not offer"))
            .Should().Be("This server does not offer sandboxes with a screen. Its operator decides that."));
        page.FindAll("a").Should().NotContain(link => link.TextContent.Trim() == "Open its settings", "no setting of the bot gives it a screen on this server");
        page.FindAll("[role=tab]").Select(tab => tab.TextContent.Trim()).Should().Equal("Terminal");
    }

    [Fact]
    public void A_starting_sandbox_says_the_screen_shows_once_it_runs_and_does_not_ask_for_it_yet()
    {
        Answer(Status("starting"));

        var page = RenderSandbox();

        page.WaitForAssertion(() => page.Find(".dv-sandbox-screen").TextContent.Should().Contain("The sandbox is starting. The screen shows once it runs."));
        Server.Sent("POST", $"{sandbox}/screen").Should().BeEmpty();
    }

    [Fact]
    public void While_the_first_picture_is_on_its_way_the_screen_says_so()
    {
        Answer(Status("running"));
        var gate = Server.Hold("POST", $"{sandbox}/screen");

        var page = RenderSandbox();

        page.WaitForAssertion(() => page.FindAll(".dv-sandbox-screen [role=status]").Select(note => note.TextContent).Should().Contain("Getting the screen."));
        gate.SetResult();
        page.WaitForAssertion(() => page.Find("img.dv-sandbox-image"));
    }

    [Fact]
    public void A_picture_the_server_refuses_says_why_in_its_words()
    {
        Answer(Status("running"));
        Server.Refuse("POST", $"{sandbox}/screen", HttpStatusCode.Conflict, "The sandbox's screen did not start.");

        var page = RenderSandbox();

        page.WaitForAssertion(() => page.Find(".dv-sandbox-screen [role=alert]").TextContent.Trim()
            .Should().Be("The sandbox's screen did not start. The screen is tried again shortly."));
        page.FindAll("img.dv-sandbox-image").Should().BeEmpty();
    }

    [Fact]
    public async Task A_picture_that_takes_too_long_says_so_and_the_screen_keeps_refreshing()
    {
        Answer(Status("running"));
        Server.TimeOut("POST", $"{sandbox}/screen");
        var page = RenderSandbox();
        page.WaitForAssertion(() => page.Find(".dv-sandbox-screen [role=alert]").TextContent.Trim()
            .Should().Be("Drover's server did not answer in time. The screen is tried again shortly."));

        Server.AnswerBytes("POST", $"{sandbox}/screen", picture, "image/png");
        (await PicturesWithin(2, SandboxWords.NextPictureAfter(1))).Should().Be(2, "the picture is asked for again on its own after a failure");

        page.WaitForAssertion(() => page.Find("img.dv-sandbox-image"));
        page.Find(".dv-sandbox-screen [role=alert]").TextContent.Trim().Should().BeEmpty("the picture came, so the problem is gone");
    }

    [Fact]
    public async Task A_sandbox_the_bot_starts_while_the_page_is_open_shows_its_screen_after_the_next_read()
    {
        Answer(Status("none"));
        var page = RenderSandbox();
        page.WaitForAssertion(() => page.Find(".dv-sandbox-screen").TextContent.Should().Contain("No sandbox yet."));
        Server.Sent("POST", $"{sandbox}/screen").Should().BeEmpty("there is nothing to look at yet");

        Answer(Status("running"));
        time.Advance(TimeSpan.FromSeconds(10));

        page.WaitForAssertion(() => page.Find(".dv-sandbox-state .dv-badge").TextContent.Should().Be("Running"));
        (await PicturesWithin(1, SandboxWords.NextPictureAfter(0))).Should().Be(1, "the picture comes within its own timer once the state reads running");
        page.WaitForAssertion(() => page.Find("img.dv-sandbox-image"));
    }

    [Fact]
    public async Task No_sandbox_yet_offers_to_start_it_and_a_tap_brings_the_screen_and_reads_the_state_again()
    {
        Answer(Status("none", lastUsed: null));
        var page = RenderSandbox();
        page.WaitForAssertion(() => WakeButton(page).TextContent.Trim().Should().Be("Start the sandbox"));
        Answer(Status("running"));

        await WakeButton(page).ClickAsync(new MouseEventArgs());

        page.FindAll(".dv-sandbox-screen [role=status]").Select(note => note.TextContent)
            .Should().Contain("Starting the sandbox. A new one can take a minute.");
        (await PicturesWithin(1)).Should().Be(1, "asking for the screen makes the sandbox");
        page.WaitForAssertion(() => page.Find("img.dv-sandbox-image"));
        page.WaitForAssertion(() => page.Find(".dv-sandbox-state .dv-badge").TextContent.Should().Be("Running"));
        Server.Sent("GET", sandbox).Should().HaveCount(2, "the state is read again at once, not on its ten-second timer");
        page.FindAll(".dv-sandbox-wake").Should().BeEmpty("a running sandbox needs no start");
    }

    [Fact]
    public async Task A_paused_sandbox_is_resumed_from_the_screen_and_says_so_while_it_comes_back()
    {
        Answer(Status("paused"));
        var gate = Server.Hold("POST", $"{sandbox}/screen");
        var page = RenderSandbox();
        page.WaitForAssertion(() => WakeButton(page).TextContent.Trim().Should().Be("Resume the sandbox"));

        await WakeButton(page).ClickAsync(new MouseEventArgs());

        (await PicturesWithin(1)).Should().Be(1, "asking for the screen resumes the sandbox");
        page.FindAll(".dv-sandbox-screen [role=status]").Select(note => note.TextContent).Should().Contain("Resuming the sandbox.");
        page.FindAll(".dv-sandbox-wake").Should().BeEmpty("one start at a time");
        page.Find(".dv-sandbox-screen").TextContent.Should().NotContain("Resume it to see the screen");
        Answer(Status("running"));
        gate.SetResult();
        page.WaitForAssertion(() => page.Find("img.dv-sandbox-image"));
    }

    [Fact]
    public async Task A_start_the_server_refuses_says_why_in_its_words_and_only_a_tap_tries_again()
    {
        var refusal = "The server's sandbox provider key does not work. Tell the server's operator.";
        Answer(Status("none", lastUsed: null));
        Server.Refuse("POST", $"{sandbox}/screen", HttpStatusCode.Conflict, refusal);
        var page = RenderSandbox();
        page.WaitForAssertion(() => WakeButton(page).TextContent.Trim().Should().Be("Start the sandbox"));
        Answer(Status("starting"));

        await WakeButton(page).ClickAsync(new MouseEventArgs());

        (await PicturesWithin(1)).Should().Be(1);
        page.WaitForAssertion(() => page.Find(".dv-sandbox-screen [role=alert]").TextContent.Trim().Should().Be(refusal));
        page.WaitForAssertion(() => WakeButton(page).TextContent.Trim().Should().Be("Try again"));
        await Elapse(TimeSpan.FromSeconds(3));
        Pictures().Should().Be(1, "each try asks the provider for a sandbox, so only a tap tries again");

        Server.AnswerBytes("POST", $"{sandbox}/screen", picture, "image/png");
        await WakeButton(page).ClickAsync(new MouseEventArgs());

        (await PicturesWithin(2)).Should().Be(2);
        page.WaitForAssertion(() => page.Find("img.dv-sandbox-image"));
        page.Find(".dv-sandbox-screen [role=alert]").TextContent.Trim().Should().BeEmpty("the picture came, so the problem is gone");
    }

    [Fact]
    public async Task A_state_read_that_takes_too_long_does_not_stop_the_reads_after_it()
    {
        Answer(Status("none"));
        var page = RenderSandbox();
        page.WaitForAssertion(() => page.Find(".dv-sandbox-state .dv-badge").TextContent.Should().Be("No sandbox yet"));

        Server.TimeOut("GET", sandbox);
        time.Advance(TimeSpan.FromSeconds(10));
        page.WaitForAssertion(() => Server.Sent("GET", sandbox).Should().HaveCount(2));
        Answer(Status("running"));
        time.Advance(TimeSpan.FromSeconds(10));

        page.WaitForAssertion(() => page.Find(".dv-sandbox-state .dv-badge").TextContent.Should().Be("Running"));
        (await PicturesWithin(1, SandboxWords.NextPictureAfter(0))).Should().Be(1);
        page.WaitForAssertion(() => page.Find("img.dv-sandbox-image"));
    }

    [Fact]
    public async Task A_picture_answered_with_a_page_says_so_and_the_screen_keeps_refreshing()
    {
        Answer(Status("running"));
        Server.AnswerPage("POST", $"{sandbox}/screen");
        var page = RenderSandbox();
        page.WaitForAssertion(() => page.Find(".dv-sandbox-screen [role=alert]").TextContent.Trim()
            .Should().Be("Drover's server sent an answer this page could not read. The screen is tried again shortly."));
        page.FindAll("img.dv-sandbox-image").Should().BeEmpty("a page is not a picture of the screen");

        Server.AnswerBytes("POST", $"{sandbox}/screen", picture, "image/png");
        (await PicturesWithin(2, SandboxWords.NextPictureAfter(1))).Should().Be(2);

        page.WaitForAssertion(() => page.Find("img.dv-sandbox-image"));
    }

    [Theory]
    [InlineData("page")]
    [InlineData("unexpected")]
    public async Task A_state_read_that_fails_in_any_way_does_not_stop_the_reads_after_it(string failure)
    {
        Answer(Status("none"));
        var page = RenderSandbox();
        page.WaitForAssertion(() => page.Find(".dv-sandbox-state .dv-badge").TextContent.Should().Be("No sandbox yet"));

        if (failure is "page")
        {
            Server.AnswerPage("GET", sandbox);
        }
        else
        {
            Server.Fail("GET", sandbox, new InvalidOperationException("A failure no screen expects."));
        }

        time.Advance(TimeSpan.FromSeconds(10));
        page.WaitForAssertion(() => Server.Sent("GET", sandbox).Should().HaveCount(2));
        Answer(Status("running"));
        time.Advance(TimeSpan.FromSeconds(10));

        page.WaitForAssertion(() => page.Find(".dv-sandbox-state .dv-badge").TextContent.Should().Be("Running"));
        (await PicturesWithin(1, SandboxWords.NextPictureAfter(0))).Should().Be(1);
    }

    [Fact]
    public void A_server_without_a_provider_says_what_is_missing_and_links_to_the_status_page()
    {
        Answer(Status("none") with { Available = false });

        var page = RenderSandbox();

        page.WaitForAssertion(() => page.Find(".dv-empty-title").TextContent.Should().Be("No sandbox provider is set up"));
        page.Find(".dv-empty-action").GetAttribute("href").Should().Be(Addresses.Status);
        page.FindAll("[role=group][aria-label=Power]").Should().BeEmpty();
    }

    [Fact]
    public void A_bot_without_a_sandbox_links_to_its_settings()
    {
        Answer(Status("none") with { HasSandbox = false });

        var page = RenderSandbox();

        page.WaitForAssertion(() => page.Find(".dv-empty-title").TextContent.Should().Be("Sample bot has no sandbox"));
        var action = page.Find(".dv-empty-action");
        action.TextContent.Should().Be("Open its settings");
        action.GetAttribute("href").Should().Be(Addresses.Bot(botId));
    }

    [Fact]
    public void While_the_state_is_on_its_way_the_screen_says_it_is_loading()
    {
        Answer(Status("running"));
        var gate = Server.Hold("GET", sandbox);

        var page = RenderSandbox();

        page.WaitForAssertion(() => page.Find("[role=status]").TextContent.Should().Be("Loading the sandbox."));
        gate.SetResult();
        page.WaitForAssertion(() => page.Find("h1").TextContent.Should().Be("Sample bot's sandbox"));
    }

    [Fact]
    public void A_state_that_cannot_be_read_says_what_failed_and_offers_to_try_again()
    {
        Server.Refuse("GET", sandbox, HttpStatusCode.Conflict, "The sandbox could not be read.");

        var page = RenderSandbox();

        page.WaitForAssertion(() => page.Find(".dv-empty-title").TextContent.Should().Be("Drover could not load this sandbox"));
        page.Find(".dv-empty-cause").TextContent.Should().Be("The sandbox could not be read.");
        page.Find(".dv-empty button").TextContent.Trim().Should().Be("Try again");
    }

    [Fact]
    public void A_person_who_may_not_change_the_bot_is_told_so()
    {
        Server.Answer("GET", sandbox, HttpStatusCode.Forbidden);

        var page = RenderSandbox();

        page.WaitForAssertion(() => page.Find(".dv-empty-title").TextContent.Should().Be("You cannot use this sandbox"));
    }

    #endregion

    #region Power

    [Fact]
    public async Task Rebuild_asks_first_says_what_is_lost_and_cancel_sends_nothing()
    {
        Answer(Status("running"));
        Server.Answer("POST", $"{sandbox}/rebuild", Status("running"));
        var page = RenderSandbox();
        page.WaitForAssertion(() => page.Find("[role=group][aria-label=Power]"));

        await PowerButton(page, "Rebuild").ClickAsync(new MouseEventArgs());

        var dialog = page.FindAll("dialog").Single(found => found.QuerySelector(".dv-dialog-body") is not null);
        dialog.QuerySelector("h2")!.TextContent.Should().Be("Rebuild the sandbox");
        dialog.TextContent.Should().Contain("Everything in this one is lost: its files, open windows, and anything installed.");
        await DialogButton(page, "Keep this sandbox").ClickAsync(new MouseEventArgs());
        Server.Sent("POST", $"{sandbox}/rebuild").Should().BeEmpty();

        await PowerButton(page, "Rebuild").ClickAsync(new MouseEventArgs());
        await DialogButton(page, "Rebuild").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => Server.Sent("POST", $"{sandbox}/rebuild").Should().ContainSingle());
    }

    [Fact]
    public async Task Turn_off_asks_first_says_what_is_lost_and_shows_the_state_it_left()
    {
        Answer(Status("running"));
        Server.Answer("POST", $"{sandbox}/turn-off", Status("none"));
        var page = RenderSandbox();
        page.WaitForAssertion(() => page.Find("[role=group][aria-label=Power]"));

        await PowerButton(page, "Turn off").ClickAsync(new MouseEventArgs());

        var dialog = page.FindAll("dialog").Single(found => found.QuerySelector(".dv-dialog-body") is not null);
        dialog.QuerySelector("h2")!.TextContent.Should().Be("Turn off the sandbox");
        dialog.TextContent.Should().Contain("everything in it is lost");
        await DialogButton(page, "Keep it").ClickAsync(new MouseEventArgs());
        Server.Sent("POST", $"{sandbox}/turn-off").Should().BeEmpty();

        await PowerButton(page, "Turn off").ClickAsync(new MouseEventArgs());
        await DialogButton(page, "Turn off").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.Find(".dv-sandbox-state .dv-badge").TextContent.Should().Be("No sandbox yet"));
        Server.Sent("POST", $"{sandbox}/turn-off").Should().ContainSingle();
    }

    [Fact]
    public async Task Pause_sends_pause_and_a_refusal_is_shown_in_the_servers_words()
    {
        Answer(Status("running"));
        Server.Refuse("POST", $"{sandbox}/pause", HttpStatusCode.Conflict, "The sandbox provider is not available right now. Try again in a moment.");
        var page = RenderSandbox();
        page.WaitForAssertion(() => page.Find("[role=group][aria-label=Power]"));

        await PowerButton(page, "Pause").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => page.Find(".dv-sandbox > .dv-note-region[role=alert]").TextContent.Should().Contain("not available right now"));
    }

    #endregion

    #region Taking Control

    [Fact]
    public void A_terminal_link_opens_the_terminal_even_when_the_bot_has_a_desktop()
    {
        Answer(Status("running"));
        Navigation.NavigateTo(Addresses.BotTerminal(botId));
        var page = RenderSandbox();

        page.WaitForAssertion(() => page.Find("[role=tab][aria-selected=true]").TextContent.Should().Be("Terminal"));
        page.Find(".dv-sandbox-terminal").Should().NotBeNull();
    }

    [Fact]
    public async Task Desktop_input_names_where_clicks_and_keys_go()
    {
        Answer(Status("running"));
        var page = await RenderInControl();

        page.Find(".dv-sandbox-screen [role=status]").TextContent.Should().Contain("Clicks and keys go to the sandbox");
        var screen = page.Find("button.dv-sandbox-picture");
        screen.GetAttribute("aria-label").Should().Contain("Click or tap the desktop").And.Contain("arrow keys move the pointer");
        page.Find("[id^=dv-sandbox-pointer-words]").TextContent.Should().Be("The pointer is at 512, 384 of 1024 by 768.");
        page.FindAll(".dv-sandbox-type label").Select(label => label.TextContent).Should().Equal("Text to type");
        var keys = page.Find("[role=group][aria-label=Keys]");
        keys.QuerySelectorAll("button.dv-button").Select(button => button.GetAttribute("aria-label"))
            .Should().Equal("Enter", "Tab", "Escape", "Backspace", "Arrow left", "Arrow up", "Arrow down", "Arrow right");
        keys.QuerySelector(".dv-menu-trigger")!.TextContent.Should().Be("Key combinations");
        page.Find("[role=group][aria-label=Scroll]").QuerySelectorAll("button").Select(button => button.TextContent.Trim()).Should().Equal("Scroll up", "Scroll down");
        page.Find(".dv-segmented legend").TextContent.Should().Be("Desktop click type");
    }

    [Fact]
    public async Task A_tap_on_the_picture_clicks_where_it_landed_on_the_sandboxs_own_screen()
    {
        Answer(Status("running"));
        module.Setup<double[]?>("size", _ => true).SetResult([328, 246]);
        var page = await RenderInControl();

        await page.Find("button.dv-sandbox-picture").ClickAsync(new MouseEventArgs { Detail = 1, OffsetX = 164, OffsetY = 123 });

        page.WaitForAssertion(() => Sent<SandboxInputRequest>($"{sandbox}/input").Should().ContainSingle()
            .Which.Should().Be(new SandboxInputRequest("click", X: 512, Y: 384, Button: "left", Twice: false)));
    }

    [Fact]
    public async Task With_the_keyboard_the_arrows_move_the_pointer_and_enter_clicks_there()
    {
        Answer(Status("running"));
        var page = await RenderInControl();
        var screen = page.Find("button.dv-sandbox-picture");

        await screen.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight" });
        await page.Find("button.dv-sandbox-picture").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown", ShiftKey = true });
        await page.Find("button.dv-sandbox-picture").ClickAsync(new MouseEventArgs { Detail = 0 });

        page.WaitForAssertion(() => Sent<SandboxInputRequest>($"{sandbox}/input").Should().ContainSingle()
            .Which.Should().Match<SandboxInputRequest>(input => input.Kind == "click" && input.X == 528 && input.Y == 448));
        page.Find("[id^=dv-sandbox-pointer-words]").TextContent.Should().Be("The pointer is at 528, 448 of 1024 by 768.");
    }

    [Fact]
    public async Task Typed_text_and_keys_are_sent_as_the_person_gave_them()
    {
        Answer(Status("running"));
        var page = await RenderInControl();
        EnterAfterSwitch(page).HasAttribute("checked").Should().BeFalse();

        await page.Find(".dv-sandbox-type input").InputAsync(new ChangeEventArgs { Value = "sample text" });
        await page.Find("form.dv-sandbox-type").SubmitAsync();
        page.WaitForAssertion(() => Sent<SandboxInputRequest>($"{sandbox}/input").Should().ContainSingle());
        await page.Find("[role=group][aria-label=Keys] button[aria-label=Enter]").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => Sent<SandboxInputRequest>($"{sandbox}/input").Should().BeEquivalentTo(
            [new SandboxInputRequest("type", Text: "sample text"), new SandboxInputRequest("key", Keys: ["enter"])],
            options => options.WithStrictOrdering()));
    }

    [Fact]
    public async Task An_active_bot_run_can_use_the_same_desktop()
    {
        Answer(Status("running") with { BotWorking = true });
        var page = await RenderInControl();

        page.FindAll(".dv-sandbox-state .dv-badge").Select(badge => badge.TextContent).Should().Contain("Bot working");
        page.Find(".dv-sandbox-screen").TextContent.Should().Contain("Sample bot has an active run and may use the same desktop.");
    }

    [Fact]
    public async Task The_keyboard_action_types_text_and_can_press_Enter_when_requested()
    {
        Answer(Status("running"));
        var page = await RenderInControl();
        var field = page.Find(".dv-sandbox-type input");

        field.GetAttribute("enterkeyhint").Should().Be("send");
        page.Find(".dv-sandbox-options .dv-switch-label").TextContent.Should().Be("Press Enter after typing");
        page.Find(".dv-sandbox-type button[type=submit]").TextContent.Trim().Should().Be("Type text");
        await EnterAfterSwitch(page).ChangeAsync(new ChangeEventArgs { Value = true });
        page.Find(".dv-sandbox-type button[type=submit]").TextContent.Trim().Should().Be("Type and press Enter");

        await field.InputAsync(new ChangeEventArgs { Value = "ls -la" });
        await page.Find("form.dv-sandbox-type").SubmitAsync();

        page.WaitForAssertion(() => Sent<SandboxInputRequest>($"{sandbox}/input").Should().BeEquivalentTo(
            [new SandboxInputRequest("type", Text: "ls -la"), new SandboxInputRequest("key", Keys: ["enter"])],
            options => options.WithStrictOrdering()));
        page.WaitForAssertion(() => page.Find(".dv-sandbox-type input").GetAttribute("value").Should().BeEmpty());
        module.VerifyInvoke("keepTyping");
    }

    [Fact]
    public async Task Text_that_was_refused_stays_in_the_field_and_no_Enter_follows_it()
    {
        Answer(Status("running"));
        Server.Refuse("POST", $"{sandbox}/input", HttpStatusCode.BadRequest, "The text has a character the sandbox cannot type.");
        var page = await RenderInControl();

        await page.Find(".dv-sandbox-type input").InputAsync(new ChangeEventArgs { Value = "sample" });
        await page.Find("form.dv-sandbox-type").SubmitAsync();

        page.WaitForAssertion(() => page.Find(".dv-sandbox-screen [role=alert]").TextContent.Should().Contain("cannot type"));
        Sent<SandboxInputRequest>($"{sandbox}/input").Should().ContainSingle().Which.Kind.Should().Be("type");
        page.Find(".dv-sandbox-type input").GetAttribute("value").Should().Be("sample");
    }

    [Theory]
    [InlineData("tap")]
    [InlineData("text")]
    [InlineData("key")]
    [InlineData("scroll")]
    public async Task Anything_a_person_sends_brings_a_new_picture_at_once_instead_of_on_the_timer(string input)
    {
        Answer(Status("running"));
        var page = await RenderInControl();
        Pictures().Should().Be(1);

        await SendOne(page, input);

        (await PicturesWithin(2)).Should().Be(2);
    }

    [Fact]
    public async Task Inputs_in_quick_succession_never_pile_up_picture_requests()
    {
        Answer(Status("running"));
        var page = await RenderInControl();
        var gate = Server.Hold("POST", $"{sandbox}/screen");

        await SendOne(page, "key");
        (await PicturesWithin(2)).Should().Be(2);
        for (var press = 0; press < 4; press++)
        {
            await SendOne(page, "key");
        }

        await Elapse(TimeSpan.FromMilliseconds(700));
        Pictures().Should().Be(2, "one picture is in flight, and the inputs behind it wait for it");
        gate.SetResult();
        (await PicturesWithin(3)).Should().Be(3);
        await Elapse(TimeSpan.FromMilliseconds(700));
        Pictures().Should().Be(3, "the inputs made while one was in flight add one picture, not one each");
    }

    #endregion

    #region Zoom

    [Fact]
    public void The_picture_has_named_zoom_controls_and_starts_fitted()
    {
        Answer(Status("running"));

        var page = RenderSandbox();

        page.WaitForAssertion(() => page.Find("[role=group][aria-label=Zoom]"));
        var zoom = page.Find("[role=group][aria-label=Zoom]");
        zoom.QuerySelectorAll("button").Select(button => button.GetAttribute("aria-label") ?? button.TextContent.Trim())
            .Should().Equal("Zoom out", "Zoom in", "Fit screen");
        zoom.QuerySelector("[aria-live=polite]")!.TextContent.Should().Be("100%");
        ZoomButton(page, "Zoom out").HasAttribute("disabled").Should().BeTrue();
        ZoomButton(page, "Fit screen").HasAttribute("disabled").Should().BeTrue();
        ZoomButton(page, "Zoom in").HasAttribute("disabled").Should().BeFalse();
        page.Find(".dv-sandbox-layer").GetAttribute("style").Should().Contain("scale(1)");
        page.Find(".dv-sandbox-frame").GetAttribute("style").Should().Contain("aspect-ratio: 1024 / 768");
    }

    [Fact]
    public async Task Zoom_in_enlarges_around_the_middle_and_fit_shows_the_whole_screen_again()
    {
        Answer(Status("running"));
        var page = RenderSandbox();
        page.WaitForAssertion(() => page.Find("[role=group][aria-label=Zoom]"));

        await ZoomButton(page, "Zoom in").ClickAsync(new MouseEventArgs());
        await ZoomButton(page, "Zoom in").ClickAsync(new MouseEventArgs());

        page.Find("[role=group][aria-label=Zoom] [aria-live=polite]").TextContent.Should().Be("200%");
        page.Find(".dv-sandbox-layer").GetAttribute("style").Should().Contain("translate(-50%, -50%) scale(2)");
        page.Find(".dv-sandbox-frame").ClassList.Should().Contain("dv-sandbox-frame-zoomed");
        ZoomButton(page, "Zoom out").HasAttribute("disabled").Should().BeFalse();

        await ZoomButton(page, "Fit screen").ClickAsync(new MouseEventArgs());

        page.Find("[role=group][aria-label=Zoom] [aria-live=polite]").TextContent.Should().Be("100%");
        page.Find(".dv-sandbox-layer").GetAttribute("style").Should().Contain("translate(0%, 0%) scale(1)");
        page.Find(".dv-sandbox-frame").ClassList.Should().NotContain("dv-sandbox-frame-zoomed");
    }

    [Fact]
    public async Task Zoomed_in_view_only_the_picture_takes_the_focus_and_the_arrow_keys_move_it()
    {
        Answer(Status("running"));
        var page = RenderSandbox();
        page.WaitForAssertion(() => page.Find("[role=group][aria-label=Zoom]"));
        page.Find("div.dv-sandbox-picture").HasAttribute("tabindex").Should().BeFalse("at fit there is nothing to move");

        await ZoomButton(page, "Zoom in").ClickAsync(new MouseEventArgs());
        await ZoomButton(page, "Zoom in").ClickAsync(new MouseEventArgs());
        var picture = page.Find("div.dv-sandbox-picture");
        picture.GetAttribute("tabindex").Should().Be("0");
        picture.GetAttribute("aria-label").Should().Be("Enlarged screen. Arrow keys move the view.");
        page.Find("img.dv-sandbox-image").GetAttribute("alt").Should().StartWith("Sample bot's sandbox screen");

        await picture.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight", ShiftKey = true });

        page.Find(".dv-sandbox-layer").GetAttribute("style").Should().Contain("translate(-90%, -50%) scale(2)");
        Server.Sent("POST", $"{sandbox}/input").Should().BeEmpty("looking around sends nothing to the sandbox");
    }

    [Fact]
    public async Task A_tap_on_the_enlarged_picture_clicks_the_sandbox_pixel_under_it()
    {
        Answer(Status("running"));
        module.Setup<double[]?>("size", _ => true).SetResult([400, 300]);
        var page = await RenderInControl();
        await ZoomButton(page, "Zoom in").ClickAsync(new MouseEventArgs());
        await ZoomButton(page, "Zoom in").ClickAsync(new MouseEventArgs());

        await page.Find("button.dv-sandbox-picture").ClickAsync(new MouseEventArgs { Detail = 1, OffsetX = 0, OffsetY = 0 });

        page.WaitForAssertion(() => Sent<SandboxInputRequest>($"{sandbox}/input").Should().ContainSingle()
            .Which.Should().Be(new SandboxInputRequest("click", X: 256, Y: 192, Button: "left", Twice: false)));
    }

    [Fact]
    public async Task A_pinch_reported_by_the_script_is_kept_in_bounds_and_taps_follow_it()
    {
        Answer(Status("running"));
        module.Setup<double[]?>("size", _ => true).SetResult([328, 246]);
        var page = await RenderInControl();

        var screen = page.FindComponent<SandboxScreen>();
        await screen.InvokeAsync(() => screen.Instance.ViewChanged(6, -5, -5));

        page.Find("[role=group][aria-label=Zoom] [aria-live=polite]").TextContent.Should().Be("400%");
        page.Find(".dv-sandbox-layer").GetAttribute("style").Should().Contain("translate(-300%, -300%) scale(4)");
        ZoomButton(page, "Zoom in").HasAttribute("disabled").Should().BeTrue();
        await page.Find("button.dv-sandbox-picture").ClickAsync(new MouseEventArgs { Detail = 1, OffsetX = 0, OffsetY = 0 });
        page.WaitForAssertion(() => Sent<SandboxInputRequest>($"{sandbox}/input").Should().ContainSingle()
            .Which.Should().Match<SandboxInputRequest>(input => input.X == 768 && input.Y == 576));
    }

    [Fact]
    public async Task Zoomed_in_the_view_follows_the_keyboard_pointer()
    {
        Answer(Status("running"));
        var page = await RenderInControl();
        await ZoomButton(page, "Zoom in").ClickAsync(new MouseEventArgs());
        await ZoomButton(page, "Zoom in").ClickAsync(new MouseEventArgs());

        for (var press = 0; press < 8; press++)
        {
            await page.Find("button.dv-sandbox-picture").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowRight", ShiftKey = true });
        }

        page.Find("[id^=dv-sandbox-pointer-words]").TextContent.Should().Be("The pointer is at 1023, 384 of 1024 by 768.");
        page.Find(".dv-sandbox-layer").GetAttribute("style").Should().Contain("translate(-100%, -50%) scale(2)");
    }

    #endregion

    #region Terminal

    [Fact]
    public async Task A_command_runs_and_its_exit_code_and_output_are_listed_with_the_commands_of_this_visit()
    {
        Answer(Status("running") with { Desktop = false });
        Server.Answer("POST", $"{sandbox}/commands", new SandboxCommandResponse(0, "hello\n", string.Empty, false, false, false));
        var page = RenderSandbox();
        page.WaitForAssertion(() => page.Find(".dv-sandbox-run input"));

        await page.Find(".dv-sandbox-run input").InputAsync(new ChangeEventArgs { Value = "echo hello" });
        await page.Find("form.dv-sandbox-run").SubmitAsync();

        page.WaitForAssertion(() => page.Find(".dv-sandbox-exit").TextContent.Should().Be("Exit code 0."));
        page.Find("ol.dv-sandbox-history").GetAttribute("aria-label").Should().Be("Commands run on this visit");
        page.Find(".dv-sandbox-command").TextContent.Should().Be("echo hello");
        page.Find(".dv-sandbox-entry code").TextContent.Should().Be("hello\n");
        Sent<SandboxCommandRequest>($"{sandbox}/commands").Should().Equal(new SandboxCommandRequest("echo hello"));
    }

    [Fact]
    public async Task A_refused_command_says_why_beside_it()
    {
        Answer(Status("running") with { Desktop = false });
        Server.Refuse("POST", $"{sandbox}/commands", HttpStatusCode.Conflict, "This bot has no sandbox.");
        var page = RenderSandbox();
        page.WaitForAssertion(() => page.Find(".dv-sandbox-run input"));

        await page.Find(".dv-sandbox-run input").InputAsync(new ChangeEventArgs { Value = "ls" });
        await page.Find("form.dv-sandbox-run").SubmitAsync();

        page.WaitForAssertion(() => page.Find(".dv-sandbox-entry [role=alert]").TextContent.Should().Contain("This bot has no sandbox."));
    }

    #endregion

    #region Helpers

    private static BotSandboxStatus Status(string state, DateTimeOffset? lastUsed = default) =>
        new(Available: true, HasSandbox: true, state, lastUsed ?? (state is "none" ? null : now.AddMinutes(-3.5)), Desktop: true, BotWorking: false, DesktopOffered: true);

    private void Answer(BotSandboxStatus status) => Server.Answer("GET", sandbox, status);

    private IRenderedComponent<BotSandbox> RenderSandbox() => Render<BotSandbox>(parameters => parameters.Add(page => page.BotId, botId));

    private async Task<IRenderedComponent<BotSandbox>> RenderInControl()
    {
        var page = RenderSandbox();
        page.WaitForAssertion(() => page.Find("img.dv-sandbox-image"));
        await page.Find(".dv-sandbox-controlbar input[role=switch]").ChangeAsync(new ChangeEventArgs { Value = true });
        page.WaitForAssertion(() => page.Find("button.dv-sandbox-picture"));
        return page;
    }

    private static IElement EnterAfterSwitch(IRenderedComponent<BotSandbox> page) => page.Find(".dv-sandbox-options input[role=switch]");

    private static IElement ZoomButton(IRenderedComponent<BotSandbox> page, string name) =>
        page.FindAll("[role=group][aria-label=Zoom] button").Single(button => (button.GetAttribute("aria-label") ?? button.TextContent.Trim()) == name);

    private static IElement WakeButton(IRenderedComponent<BotSandbox> page) => page.Find(".dv-sandbox-wake button");

    private int Pictures() => Server.Sent("POST", $"{sandbox}/screen").Count();

    private static IElement Paragraph(IRenderedComponent<BotSandbox> page, string start) =>
        page.FindAll("p").Single(paragraph => paragraph.TextContent.Trim().StartsWith(start, StringComparison.Ordinal));

    private static IElement Link(IRenderedComponent<BotSandbox> page, string name) =>
        page.FindAll("a").Single(link => link.TextContent.Trim() == name);

    /// <summary>The text as a person reads it: runs of white space are one space, and none is added where there is none.</summary>
    private static string Words(IElement element) => string.Join(' ', element.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// The pictures asked for, once there are <paramref name="expected"/> or the page's clock has moved on by
    /// <paramref name="within"/> (a second by default: well inside the two seconds the timer waits). The clock moves in
    /// small steps and the page gets a moment after each. A request held by the server renders nothing, so this looks
    /// instead of waiting for a render.
    /// </summary>
    private async Task<int> PicturesWithin(int expected, TimeSpan? within = null)
    {
        var until = time.GetUtcNow() + (within ?? TimeSpan.FromSeconds(1));

        while (Pictures() < expected && time.GetUtcNow() < until)
        {
            await Elapse(step);
        }

        for (var settle = 0; settle < 20 && Pictures() < expected; settle++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), Xunit.TestContext.Current.CancellationToken);
        }

        return Pictures();
    }

    /// <summary>Moves the page's clock on by <paramref name="span"/> in small steps, giving the page a moment after each.</summary>
    private async Task Elapse(TimeSpan span)
    {
        for (var moved = TimeSpan.Zero; moved < span; moved += step)
        {
            time.Advance(step);
            await Task.Delay(TimeSpan.FromMilliseconds(5), Xunit.TestContext.Current.CancellationToken);
        }
    }

    private static async Task SendOne(IRenderedComponent<BotSandbox> page, string input)
    {
        switch (input)
        {
            case "tap":
                await page.Find("button.dv-sandbox-picture").ClickAsync(new MouseEventArgs { Detail = 1, OffsetX = 10, OffsetY = 10 });
                break;
            case "text":
                await page.Find(".dv-sandbox-type input").InputAsync(new ChangeEventArgs { Value = "sample" });
                await page.Find("form.dv-sandbox-type").SubmitAsync();
                break;
            case "key":
                await page.Find("[role=group][aria-label=Keys] button[aria-label=Escape]").ClickAsync(new MouseEventArgs());
                break;
            default:
                await page.FindAll("[role=group][aria-label=Scroll] button")[0].ClickAsync(new MouseEventArgs());
                break;
        }
    }

    private static IElement PowerButton(IRenderedComponent<BotSandbox> page, string name) =>
        page.FindAll("[role=group][aria-label=Power] button").Single(button => button.TextContent.Trim() == name);

    private static IElement DialogButton(IRenderedComponent<BotSandbox> page, string name) =>
        page.FindAll("dialog .dv-dialog-actions button").Single(button => button.TextContent.Trim() == name);

    private List<T> Sent<T>(string path) =>
        [.. Server.Sent("POST", path).Select(body => JsonSerializer.Deserialize<T>(body, JsonSerializerOptions.Web)!)];

    private static byte[] Png(int width, int height)
    {
        var png = new byte[33];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(png, 0);
        "IHDR"u8.CopyTo(png.AsSpan(12));
        png[16] = (byte)(width >> 24);
        png[17] = (byte)(width >> 16);
        png[18] = (byte)(width >> 8);
        png[19] = (byte)width;
        png[20] = (byte)(height >> 24);
        png[21] = (byte)(height >> 16);
        png[22] = (byte)(height >> 8);
        png[23] = (byte)height;
        return png;
    }

    #endregion
}
