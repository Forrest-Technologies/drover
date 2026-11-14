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
