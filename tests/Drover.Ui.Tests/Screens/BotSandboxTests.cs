namespace Drover.Ui.Tests.Screens;

/// <summary>
/// The sandbox screen against a stand-in server: each state it can be in, the power controls and their confirming
/// dialogs, taking control of the screen, and the terminal. Every name and value here is a sample.
/// </summary>
public sealed class BotSandboxTests : ScreenTest
{
    #region Private Fields

    private const string ModulePath = "./_content/Drover.Ui/Pages/Bots/BotSandbox.razor.js";

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly string sandbox = $"/api/spaces/{SpaceId}/bots/{botId}/sandbox";

    private static readonly byte[] picture = Png(1024, 768);

    private readonly BunitJSModuleInterop module;

    #endregion

    #region Constructors

    public BotSandboxTests()
    {
        SignedIn();
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
        page.Find(".dv-sandbox-screen").TextContent.Should().Contain("It starts when the bot first uses it, or when you run a command in the terminal.");
        Server.Sent("POST", $"{sandbox}/screen").Should().BeEmpty();
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
    public async Task Taking_control_says_so_and_offers_named_controls_for_typing_keys_and_scrolling()
    {
        Answer(Status("running"));
        var page = await RenderInControl();

        page.Find(".dv-sandbox-screen [role=status]").TextContent.Should().Contain("You have control");
        var screen = page.Find("button.dv-sandbox-picture");
        screen.GetAttribute("aria-label").Should().Contain("Tap to click there").And.Contain("arrow keys move the pointer");
        page.Find("#dv-sandbox-pointer-words").TextContent.Should().Be("The pointer is at 512, 384 of 1024 by 768.");
        page.FindAll(".dv-sandbox-type label").Select(label => label.TextContent).Should().Equal("Text to type");
        var keys = page.Find("[role=group][aria-label=Keys]");
        keys.QuerySelectorAll("button.dv-button").Select(button => button.GetAttribute("aria-label"))
            .Should().Equal("Enter", "Tab", "Escape", "Backspace", "Arrow left", "Arrow up", "Arrow down", "Arrow right");
        keys.QuerySelector(".dv-menu-trigger")!.TextContent.Should().Be("Key combinations");
        page.Find("[role=group][aria-label=Scroll]").QuerySelectorAll("button").Select(button => button.TextContent.Trim()).Should().Equal("Scroll up", "Scroll down");
        page.Find(".dv-segmented legend").TextContent.Should().Be("What a tap on the screen does");
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
        page.Find("#dv-sandbox-pointer-words").TextContent.Should().Be("The pointer is at 528, 448 of 1024 by 768.");
    }

    [Fact]
    public async Task Typed_text_and_keys_are_sent_as_the_person_gave_them()
    {
        Answer(Status("running"));
        var page = await RenderInControl();
        await EnterAfterSwitch(page).ChangeAsync(new ChangeEventArgs { Value = false });

        await page.Find(".dv-sandbox-type input").InputAsync(new ChangeEventArgs { Value = "sample text" });
        await page.Find("form.dv-sandbox-type").SubmitAsync();
        page.WaitForAssertion(() => Sent<SandboxInputRequest>($"{sandbox}/input").Should().ContainSingle());
        await page.Find("[role=group][aria-label=Keys] button[aria-label=Enter]").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => Sent<SandboxInputRequest>($"{sandbox}/input").Should().BeEquivalentTo(
            [new SandboxInputRequest("type", Text: "sample text"), new SandboxInputRequest("key", Keys: ["enter"])],
            options => options.WithStrictOrdering()));
    }

    [Fact]
    public async Task While_the_bot_is_working_taking_control_says_it_is_working_there_too()
    {
        Answer(Status("running") with { BotWorking = true });
        var page = await RenderInControl();

        page.FindAll(".dv-sandbox-state .dv-badge").Select(badge => badge.TextContent).Should().Contain("Bot working");
        page.Find(".dv-sandbox-screen").TextContent.Should().Contain("Sample bot is working in this sandbox right now.");
    }

    [Fact]
    public async Task Text_is_sent_from_the_keyboards_own_action_key_and_Enter_follows_it_by_default()
    {
        Answer(Status("running"));
        var page = await RenderInControl();
        var field = page.Find(".dv-sandbox-type input");

        field.GetAttribute("enterkeyhint").Should().Be("send");
        EnterAfterSwitch(page).HasAttribute("checked").Should().BeTrue();
        page.Find(".dv-sandbox-options .dv-switch-label").TextContent.Should().Be("Press Enter after");
        page.Find(".dv-sandbox-type button[type=submit]").TextContent.Trim().Should().Be("Send");

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

        await Task.Delay(TimeSpan.FromMilliseconds(700), Xunit.TestContext.Current.CancellationToken);
        Pictures().Should().Be(2, "one picture is in flight, and the inputs behind it wait for it");
        gate.SetResult();
        (await PicturesWithin(3)).Should().Be(3);
        await Task.Delay(TimeSpan.FromMilliseconds(700), Xunit.TestContext.Current.CancellationToken);
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
            .Should().Equal("Zoom out", "Zoom in", "Fit");
        zoom.QuerySelector("[aria-live=polite]")!.TextContent.Should().Be("100%");
        ZoomButton(page, "Zoom out").HasAttribute("disabled").Should().BeTrue();
        ZoomButton(page, "Fit").HasAttribute("disabled").Should().BeTrue();
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

        await ZoomButton(page, "Fit").ClickAsync(new MouseEventArgs());

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

        await page.InvokeAsync(() => page.Instance.ViewChanged(6, -5, -5));

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

        page.Find("#dv-sandbox-pointer-words").TextContent.Should().Be("The pointer is at 1023, 384 of 1024 by 768.");
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
        new(Available: true, HasSandbox: true, state, lastUsed ?? (state is "none" ? null : DateTimeOffset.UtcNow.AddMinutes(-3.5)), Desktop: true, BotWorking: false);

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

    private int Pictures() => Server.Sent("POST", $"{sandbox}/screen").Count();

    /// <summary>
    /// The pictures asked for, once there are <paramref name="expected"/> or a second has passed: well inside the two
    /// seconds the timer waits. A request held by the server renders nothing, so this looks instead of waiting for a render.
    /// </summary>
    private async Task<int> PicturesWithin(int expected)
    {
        for (var look = 0; look < 50 && Pictures() < expected; look++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), Xunit.TestContext.Current.CancellationToken);
        }

        return Pictures();
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
