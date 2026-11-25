namespace Drover.Presentation.Tests.ViewModels.Bots;

/// <summary>
/// The sandbox screen's view models against a stand-in server: each state it can be in, the power controls and their
/// confirming dialogs, taking control of the screen, and the terminal. The screen's picture is put under the Screen
/// tab the way the page does it: it is there while that tab shows, is given the state the page reads, and tells the
/// page when the person started the sandbox. Every name and value here is a sample.
/// </summary>
public sealed class BotSandboxViewModelTests : IAsyncDisposable
{
    #region Private Fields

    private static readonly Guid spaceId = Samples.Session().Spaces[0].Id;

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly string sandbox = $"/api/spaces/{spaceId}/bots/{botId}/sandbox";

    private static readonly byte[] picture = Png(1024, 768);

    /// <summary>The screen's clock, moved by hand: the state reads and the picture's own timer wait on it.</summary>
    private static readonly DateTimeOffset now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeServer server = new();

    private readonly Navigator navigator = new();

    private readonly ServiceProvider services;

    private readonly FakeTimeProvider time = new(now);

    private readonly ToastService toasts;

    /// <summary>How far the screen's clock moves at a time while a test waits on it.</summary>
    private readonly TimeSpan step = TimeSpan.FromMilliseconds(20);

    private readonly Lock placing = new();

    private readonly List<Task> closing = [];

    private BotSandboxViewModel? page;

    private SandboxScreenViewModel? screen;

    private int headingFocused;

    private int keptTyping;

    #endregion

    #region Constructors

    public BotSandboxViewModelTests()
    {
        toasts = new(time);
        services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(server.CreateApi())
            .AddSingleton(new SessionState(server.CreateApi(), new ShareInbox()))
            .AddSingleton(navigator)
            .AddSingleton<TimeProvider>(time)
            .AddSingleton(toasts)
            .BuildServiceProvider();
        server.Answer("GET", "/api/session", Samples.Session());
        server.Answer("GET", $"/api/spaces/{spaceId}/bots/{botId}", new BotSummary(botId, "Sample bot", "sample", "Sample instructions", Guid.Parse("0196f1a0-0000-7000-8000-000000000003"), "sample-model", "active"));
        server.AnswerBytes("POST", $"{sandbox}/screen", picture, "image/png");
        server.Answer("POST", $"{sandbox}/input", HttpStatusCode.NoContent);
    }

    #endregion

    #region Properties

    private BotSandboxViewModel Page => page ?? throw new InvalidOperationException("The screen is not open yet.");

    /// <summary>The picture under the Screen tab. It is there only while that tab shows.</summary>
    private SandboxScreenViewModel Screen => screen ?? throw new InvalidOperationException("The Screen tab is not showing.");

    #endregion

    #region Interface Implementations

    public async ValueTask DisposeAsync()
    {
        if (page is not null)
        {
            await page.DisposeAsync();
        }

        if (screen is not null)
        {
            await screen.DisposeAsync();
        }

        await Task.WhenAll(closing);
        await services.DisposeAsync();
        server.Dispose();
    }

    #endregion

    #region States

    [Fact]
    public async Task A_running_sandbox_with_a_screen_shows_its_state_in_words_its_power_controls_and_the_picture()
    {
        Answer(Status("running"));

        await Open();

        await Eventually(() => Screen.Picture.Should().NotBeNull());
        Screen.ViewPictureName.Should().StartWith("Sample bot's sandbox screen");
        Page.Shows.Should().Be(BotSandboxContent.Sandbox);
        Page.Heading.Should().Be("Sample bot's sandbox");
        Page.PageTitle.Should().Be("Sample bot's sandbox · Drover");
        Page.BackLabel.Should().Be("Back to Sample bot");
        Page.StateWords.Should().Be("Running");
        Page.StateBadge.Should().Be(BadgeKind.Ok);
        Page.LastUsed.Should().Be("Used 3 minutes ago");
        (Page.OffersResume, Page.CanPause, Page.CanReplace).Should().Be((false, true, true));
        Page.Views.Select(tab => tab.Label).Should().Equal("Screen", "Terminal");
        server.Addresses.Should().Contain($"POST {sandbox}/screen?wake=false");
        (Screen.PictureWidth, Screen.PictureHeight).Should().Be((1024, 768));
        Screen.Picture.Should().Equal(picture);
        Screen.ControlWords.Should().Contain("View only");
    }

    [Fact]
    public async Task A_paused_sandbox_offers_resume_and_does_not_ask_for_the_screen()
    {
        Answer(Status("paused"));

        await Open();

        Page.StateWords.Should().Be("Paused");
        Page.OffersResume.Should().BeTrue();
        Screen.ScreenNote.Should().Contain("Resume it to see the screen");
        Screen.DesktopReady.Should().BeFalse("the control switch shows only with the picture");
        Requests("POST", $"{sandbox}/screen").Should().BeEmpty("looking at a paused sandbox would resume it");
    }

    [Fact]
    public async Task Pausing_discards_the_old_picture_and_input_and_resuming_returns_to_view_only()
    {
        Answer(Status("running"));
        await OpenInControl();
        Screen.SetPasswordMode(true);
        Screen.Typed = "unsent sample";

        server.Answer("POST", $"{sandbox}/pause", Status("paused"));
        await Page.Pause();
        (Screen.Picture, Screen.DesktopReady, Screen.Control).Should().Be((null, false, false));

        server.Answer("POST", $"{sandbox}/resume", Status("running"));
        await Page.Resume();
        (await PicturesWithin(2, TimeSpan.FromSeconds(3))).Should().Be(2);
        await Eventually(() => Screen.DesktopReady.Should().BeTrue());
        Screen.Control.Should().BeFalse();
        Screen.SetControl(true);
        Screen.Typed.Should().BeEmpty();
        Screen.PasswordMode.Should().BeFalse();
        Screen.TypeLabel.Should().Be("Text to type");
        Requests("POST", $"{sandbox}/input").Should().BeEmpty();
        toasts.Toasts.Select(toast => toast.Message).Should().Equal("The sandbox is paused. Its files are kept.", "The sandbox is running again.");
    }

    [Fact]
    public async Task No_sandbox_yet_says_how_one_starts_and_explains_the_disabled_controls()
    {
        Answer(Status("none", lastUsed: null));

        await Open();

        Page.StateWords.Should().Be("No sandbox yet");
        Page.LastUsed.Should().Be("Not used yet");
        (Page.OffersResume, Page.CanPause, Page.CanReplace).Should().Be((false, false, false));
        Page.PowerHint.Should().Be("Nothing to pause, rebuild, or turn off yet.");
        Screen.ScreenNote.Should().Contain("It starts when the bot first uses it.");
        Requests("POST", $"{sandbox}/screen").Should().BeEmpty();
    }

    [Fact]
    public async Task A_bot_whose_last_start_did_not_finish_says_so_and_offers_to_start_it()
    {
        Answer(Status("none", lastUsed: null) with { StartFailed = true });

        await Open();

        Page.StateWords.Should().Be("Start did not finish");
        Screen.ScreenNote.Should().Be("The last start of this sandbox did not finish. Start it here to see why.");
        Screen.WakeLabel.Should().Be("Start the sandbox");
        Requests("POST", $"{sandbox}/screen").Should().BeEmpty("looking alone starts nothing");
    }

    [Fact]
    public async Task A_sandbox_being_replaced_says_so_in_words()
    {
        Answer(Status("replacing", lastUsed: null));

        await Open();

        Page.StateWords.Should().Be("Being replaced");
        Page.StateBadge.Should().Be(BadgeKind.Warn);
        Page.PowerHint.Should().Contain("The old sandbox is being taken down.");
    }

    [Fact]
    public async Task A_sandbox_without_a_screen_has_only_the_terminal()
    {
        Answer(Status("running") with { Desktop = false });

        await Open();

        Page.Views.Select(tab => tab.Label).Should().Equal("Terminal");
        Page.View.Should().Be(BotSandboxViewModel.TerminalView);
        BotSandboxViewModel.CommandLabel.Should().Be("Shell command");
        screen.Should().BeNull("there is no picture to show");
    }

    [Fact]
    public async Task A_sandbox_without_a_screen_says_why_and_links_to_the_setting_that_gives_it_one()
    {
        Answer(Status("running") with { Desktop = false });

        await Open();

        Page.NoScreenNote.Should().Be("This sandbox has no screen. Turn on Desktop in the Sandbox section of the bot's settings to give it one.");
        Page.NoScreenLinksToSettings.Should().BeTrue();
        BotSandboxViewModel.SettingsLinkLabel.Should().Be("Open its settings");
    }

    [Fact]
    public async Task On_a_server_that_offers_no_screens_the_sandbox_says_so_and_offers_no_setting()
    {
        Answer(Status("running") with { Desktop = false, DesktopOffered = false });

        await Open();

        Page.NoScreenNote.Should().Be("This server does not offer sandboxes with a screen. Its operator decides that.");
        Page.NoScreenLinksToSettings.Should().BeFalse("no setting of the bot gives it a screen on this server");
        Page.Views.Select(tab => tab.Label).Should().Equal("Terminal");
    }

    [Fact]
    public async Task A_starting_sandbox_says_the_screen_shows_once_it_runs_and_does_not_ask_for_it_yet()
    {
        Answer(Status("starting"));

        await Open();

        Screen.ScreenNote.Should().Be("The sandbox is starting. The screen shows once it runs.");
        Page.PowerHint.Should().BeNull("the screen tab says it already");
        Requests("POST", $"{sandbox}/screen").Should().BeEmpty();
    }

    [Fact]
    public async Task While_the_first_picture_is_on_its_way_the_screen_says_so()
    {
        Answer(Status("running"));
        var gate = server.Hold("POST", $"{sandbox}/screen");

        await Open();

        await Eventually(() => Screen.ProgressNote.Should().Be("Getting the screen."));
        gate.SetResult();
        await Eventually(() => Screen.DesktopReady.Should().BeTrue());
        Screen.ProgressNote.Should().BeNull();
    }

    [Fact]
    public async Task A_screen_held_for_a_person_says_so_without_a_name_and_an_admin_can_stop_that_run_after_saying_yes()
    {
        var holding = Guid.Parse("0196f1a0-0000-7000-8000-0000000000d1");
        var since = new DateTimeOffset(2026, 1, 5, 9, 30, 0, TimeSpan.Zero);
        Answer(Status("running") with { Hold = new SandboxHold(holding, ForYou: false, since) });
        server.Answer("POST", $"/api/spaces/{spaceId}/runs/{holding}/cancel", new RunSummary(holding, Guid.Empty, botId, holding, "cancelled", null, null, 1, 0, new(0, 0, 0, 0), null, null));
        await Open();

        Page.HoldWords.Should()
            .Be($"A bot asked another member of this space to take over this screen on {since.ToLocalTime().ToString("d MMM, HH:mm", CultureInfo.CurrentCulture)}. The bots that share this sandbox wait until it is handed back.");
        BotSandboxViewModel.StopHoldLabel.Should().Be("Stop the run that holds it");
        Page.StoppingHold = true;
        BotSandboxViewModel.StopHoldTitle.Should().Be("Stop the run that holds the screen");
        server.Sent("POST", $"/api/spaces/{spaceId}/runs/{holding}/cancel").Should().BeEmpty("nothing stops before the person says yes");
        Answer(Status("running"));
        await Page.StopHold();

        server.Sent("POST", $"/api/spaces/{spaceId}/runs/{holding}/cancel").Should().ContainSingle();
        Page.StoppingHold.Should().BeFalse();
        Page.HoldWords.Should().BeNull();
        toasts.Toasts.Select(toast => toast.Message).Should().Equal("The run stopped. The screen is free.");

        // The control the person used is gone, so the focus goes to the screen's heading.
        headingFocused.Should().Be(1);
    }

    [Fact]
    public async Task A_run_that_has_not_stopped_yet_says_it_is_stopping_and_offers_no_second_stop()
    {
        var holding = Guid.Parse("0196f1a0-0000-7000-8000-0000000000d2");
        Answer(Status("running") with { Hold = new SandboxHold(holding, ForYou: false, null) });
        server.Answer("POST", $"/api/spaces/{spaceId}/runs/{holding}/cancel", new RunSummary(holding, Guid.Empty, botId, holding, "waiting", null, null, 1, 0, new(0, 0, 0, 0), null, null));
        await Open();
        Page.HoldIsStopping.Should().BeFalse();

        Page.StoppingHold = true;
        await Page.StopHold();

        Page.HoldIsStopping.Should().BeTrue("the stop was asked for once");
        BotSandboxViewModel.StoppingNote.Should().Be("Stopping the run. The screen frees once it stops.");
        toasts.Toasts.Should().BeEmpty("the run has not stopped yet");
        headingFocused.Should().Be(1);
    }

    [Fact]
    public async Task A_screen_held_for_this_person_says_what_to_do_next()
    {
        Answer(Status("running") with { Hold = new SandboxHold(Guid.NewGuid(), ForYou: true, null) });

        await Open();

        Page.HoldWords.Should()
            .Be("A bot asked you to take over this screen. The bots that share this sandbox wait until it is handed back. Do it on the screen below, then answer in the conversation.");
    }

    [Fact]
    public async Task A_picture_the_server_refuses_says_why_in_its_words()
    {
        Answer(Status("running"));
        server.Refuse("POST", $"{sandbox}/screen", HttpStatusCode.Conflict, "The sandbox's screen did not start.");

        await Open();

        await Eventually(() => Screen.ScreenProblem.Should().Be("The sandbox's screen did not start. The screen is tried again shortly."));
        Screen.Picture.Should().BeNull();
        Screen.ProgressNote.Should().BeNull("the problem says what is happening");
    }

    [Fact]
    public async Task An_unreachable_picture_gives_only_the_automatic_retry_as_its_next_step()
    {
        Answer(Status("running"));
        server.Fail("POST", $"{sandbox}/screen", new HttpRequestException("connection refused"));
        await Open();

        await Eventually(() => Screen.ScreenProblem.Should().Be("Drover could not reach its server. The screen is tried again shortly."));
    }

    [Fact]
    public async Task A_picture_that_takes_too_long_says_so_and_the_screen_keeps_refreshing()
    {
        Answer(Status("running"));
        server.TimeOut("POST", $"{sandbox}/screen");
        await Open();
        await Eventually(() => Screen.ScreenProblem.Should().Be("Drover's server did not answer in time. The screen is tried again shortly."));

        server.AnswerBytes("POST", $"{sandbox}/screen", picture, "image/png");
        (await PicturesWithin(2, SandboxWords.NextPictureAfter(1))).Should().Be(2, "the picture is asked for again on its own after a failure");

        await Eventually(() => Screen.Picture.Should().NotBeNull());
        Screen.ScreenProblem.Should().BeNull("the picture came, so the problem is gone");
    }

    [Fact]
    public async Task A_sandbox_the_bot_starts_while_the_screen_is_open_shows_its_picture_after_the_next_read()
    {
        Answer(Status("none"));
        await Open();
        Screen.ScreenNote.Should().Contain("No sandbox yet.");
        Requests("POST", $"{sandbox}/screen").Should().BeEmpty("there is nothing to look at yet");

        Answer(Status("running"));
        time.Advance(TimeSpan.FromSeconds(10));

        await Eventually(() => Page.StateWords.Should().Be("Running"));
        (await PicturesWithin(1, SandboxWords.NextPictureAfter(0))).Should().Be(1, "the picture comes within its own timer once the state reads running");
        await Eventually(() => Screen.Picture.Should().NotBeNull());
    }

    [Fact]
    public async Task No_sandbox_yet_offers_to_start_it_and_a_tap_brings_the_screen_and_reads_the_state_again()
    {
        Answer(Status("none", lastUsed: null));
        await Open();
        Screen.WakeLabel.Should().Be("Start the sandbox");
        Answer(Status("running"));

        Screen.Wake();

        Screen.ProgressNote.Should().Be("Starting the sandbox. A new one can take a minute.");
        (await PicturesWithin(1)).Should().Be(1, "asking for the screen makes the sandbox");
        await Eventually(() => Screen.DesktopReady.Should().BeTrue());
        Page.StateWords.Should().Be("Running");
        Requests("GET", sandbox).Should().HaveCount(2, "the state is read again at once, not on its ten-second timer");
        Screen.WakeLabel.Should().BeNull("a running sandbox needs no start");
    }

    [Fact]
    public async Task A_paused_sandbox_is_resumed_from_the_screen_and_says_so_while_it_comes_back()
    {
        Answer(Status("paused"));
        var gate = server.Hold("POST", $"{sandbox}/screen");
        await Open();
        Screen.WakeLabel.Should().Be("Resume the sandbox");

        Screen.Wake();

        (await PicturesWithin(1)).Should().Be(1, "asking for the screen resumes the sandbox");
        server.Addresses.Should().Contain($"POST {sandbox}/screen");
        Screen.ProgressNote.Should().Be("Resuming the sandbox.");
        Screen.WakeLabel.Should().BeNull("one start at a time");
        Screen.ScreenNote.Should().BeNull();
        Answer(Status("running"));
        gate.SetResult();
        await Eventually(() => Screen.DesktopReady.Should().BeTrue());
    }

    [Fact]
    public async Task A_start_the_server_refuses_says_why_in_its_words_and_only_a_tap_tries_again()
    {
        var refusal = "The server's sandbox provider key does not work. Tell the server's operator.";
        Answer(Status("none", lastUsed: null));
        server.Refuse("POST", $"{sandbox}/screen", HttpStatusCode.Conflict, refusal);
        await Open();
        Screen.WakeLabel.Should().Be("Start the sandbox");
        Answer(Status("starting"));

        Screen.Wake();

        (await PicturesWithin(1)).Should().Be(1);
        await Eventually(() => Screen.ScreenProblem.Should().Be(refusal));
        await Eventually(() => Screen.WakeLabel.Should().Be("Try again"));
        await Elapse(TimeSpan.FromSeconds(3));
        Pictures().Should().Be(1, "each try asks the provider for a sandbox, so only a tap tries again");

        server.AnswerBytes("POST", $"{sandbox}/screen", picture, "image/png");
        Answer(Status("running"));
        Screen.Wake();

        (await PicturesWithin(2)).Should().Be(2);
        await Eventually(() => Screen.DesktopReady.Should().BeTrue());
        Screen.ScreenProblem.Should().BeNull("the picture came, so the problem is gone");
    }

    [Fact]
    public async Task A_state_read_that_takes_too_long_does_not_stop_the_reads_after_it()
    {
        Answer(Status("none"));
        await Open();
        Page.StateWords.Should().Be("No sandbox yet");

        server.TimeOut("GET", sandbox);
        time.Advance(TimeSpan.FromSeconds(10));
        await Eventually(() => Requests("GET", sandbox).Should().HaveCount(2));
        Answer(Status("running"));
        time.Advance(TimeSpan.FromSeconds(10));

        await Eventually(() => Page.StateWords.Should().Be("Running"));
        (await PicturesWithin(1, SandboxWords.NextPictureAfter(0))).Should().Be(1);
        await Eventually(() => Screen.Picture.Should().NotBeNull());
    }

    [Fact]
    public async Task A_picture_answered_with_a_page_says_so_and_the_screen_keeps_refreshing()
    {
        Answer(Status("running"));
        server.AnswerPage("POST", $"{sandbox}/screen");
        await Open();
        await Eventually(() => Screen.ScreenProblem.Should().Be("Drover's server sent an answer this page could not read. The screen is tried again shortly."));
        Screen.Picture.Should().BeNull("a page is not a picture of the screen");

        server.AnswerBytes("POST", $"{sandbox}/screen", picture, "image/png");
        (await PicturesWithin(2, SandboxWords.NextPictureAfter(1))).Should().Be(2);

        await Eventually(() => Screen.Picture.Should().NotBeNull());
    }

    [Theory]
    [InlineData("page")]
    [InlineData("unexpected")]
    public async Task A_state_read_that_fails_in_any_way_does_not_stop_the_reads_after_it(string failure)
    {
        Answer(Status("none"));
        await Open();
        Page.StateWords.Should().Be("No sandbox yet");

        if (failure is "page")
        {
            server.AnswerPage("GET", sandbox);
        }
        else
        {
            server.Fail("GET", sandbox, new InvalidOperationException("A failure no screen expects."));
        }

        time.Advance(TimeSpan.FromSeconds(10));
        await Eventually(() => Requests("GET", sandbox).Should().HaveCount(2));
        Answer(Status("running"));
        time.Advance(TimeSpan.FromSeconds(10));

        await Eventually(() => Page.StateWords.Should().Be("Running"));
        (await PicturesWithin(1, SandboxWords.NextPictureAfter(0))).Should().Be(1);
    }

    [Fact]
    public async Task A_server_without_a_provider_says_what_is_missing_and_links_to_the_status_page()
    {
        Answer(Status("none") with { Available = false });

        await Open();

        Page.Shows.Should().Be(BotSandboxContent.NoProvider);
        BotSandboxViewModel.NoProviderTitle.Should().Be("No sandbox provider is set up");
        BotSandboxViewModel.NoProviderCause.Should().Contain("The server's operator adds a provider key");
        screen.Should().BeNull();
    }

    [Fact]
    public async Task A_bot_without_a_sandbox_links_to_its_settings()
    {
        Answer(Status("none") with { HasSandbox = false });

        await Open();

        Page.Shows.Should().Be(BotSandboxContent.NoSandbox);
        Page.NoSandboxTitle.Should().Be("Sample bot has no sandbox");
        BotSandboxViewModel.NoSandboxCause.Should().Be("Give it one in the Sandbox section of its settings.");
    }

    [Fact]
    public async Task While_the_state_is_on_its_way_the_screen_says_it_is_loading()
    {
        Answer(Status("running"));
        var gate = server.Hold("GET", sandbox);

        var opening = Open();

        await Eventually(() => Page.Shows.Should().Be(BotSandboxContent.Loading));
        BotSandboxViewModel.LoadingNote.Should().Be("Loading the sandbox.");
        gate.SetResult();
        await opening;
        Page.Heading.Should().Be("Sample bot's sandbox");
    }

    [Fact]
    public async Task A_state_that_cannot_be_read_says_what_failed_and_offers_to_try_again()
    {
        server.Refuse("GET", sandbox, HttpStatusCode.Conflict, "The sandbox could not be read.");

        await Open();

        Page.Shows.Should().Be(BotSandboxContent.LoadFailed);
        BotSandboxViewModel.LoadFailedTitle.Should().Be("Drover could not load this sandbox");
        Page.LoadProblem.Should().Be("The sandbox could not be read.");

        Answer(Status("running"));
        await Page.LoadCommand.ExecuteAsync(null);
        Page.Shows.Should().Be(BotSandboxContent.Sandbox);
    }

    [Fact]
    public async Task A_person_who_may_not_change_the_bot_is_told_so()
    {
        server.Answer("GET", sandbox, HttpStatusCode.Forbidden);

        await Open();

        Page.Shows.Should().Be(BotSandboxContent.NotPermitted);
        BotSandboxViewModel.NotPermittedTitle.Should().Be("You cannot use this sandbox");
    }

    [Fact]
    public async Task A_bot_that_is_not_there_says_so()
    {
        server.Answer("GET", $"/api/spaces/{spaceId}/bots/{botId}", HttpStatusCode.NotFound);

        await Open();

        Page.Shows.Should().Be(BotSandboxContent.Missing);
        BotSandboxViewModel.MissingTitle.Should().Be("This bot is not here");
    }

    #endregion

    #region Power

    [Fact]
    public async Task Rebuild_asks_first_says_what_is_lost_and_cancel_sends_nothing()
    {
        Answer(Status("running"));
        server.Answer("POST", $"{sandbox}/rebuild", Status("running"));
        await Open();

        Page.Rebuilding = true;

        BotSandboxViewModel.RebuildTitle.Should().Be("Rebuild the sandbox");
        Page.RebuildBody.Should().Be("Sample bot gets a fresh sandbox now. Everything in this one is lost: its files, open windows, and anything installed.");
        Page.Rebuilding = false;
        server.Sent("POST", $"{sandbox}/rebuild").Should().BeEmpty();

        Page.Rebuilding = true;
        await Page.Rebuild();

        Page.Rebuilding.Should().BeFalse();
        server.Sent("POST", $"{sandbox}/rebuild").Should().ContainSingle();
    }

    [Fact]
    public async Task Turn_off_asks_first_says_what_is_lost_and_shows_the_state_it_left()
    {
        Answer(Status("running"));
        server.Answer("POST", $"{sandbox}/turn-off", Status("none"));
        await Open();

        Page.TurningOff = true;

        BotSandboxViewModel.TurnOffTitle.Should().Be("Turn off the sandbox");
        Page.TurnOffBody.Should().Contain("everything in it is lost").And.Contain("Sample bot or you use it");
        Page.TurningOff = false;
        server.Sent("POST", $"{sandbox}/turn-off").Should().BeEmpty();

        Page.TurningOff = true;
        await Page.TurnOff();

        Page.StateWords.Should().Be("No sandbox yet");
        server.Sent("POST", $"{sandbox}/turn-off").Should().ContainSingle();
    }

    [Fact]
    public async Task Pause_sends_pause_and_a_refusal_is_shown_in_the_servers_words()
    {
        Answer(Status("running"));
        server.Refuse("POST", $"{sandbox}/pause", HttpStatusCode.Conflict, "The sandbox provider is not available right now. Try again in a moment.");
        await Open();

        await Page.Pause();

        Page.ActionProblem.Should().Contain("not available right now");
        Page.Busy.Should().BeFalse();
    }

    #endregion

    #region Taking Control

    [Fact]
    public async Task A_terminal_link_opens_the_terminal_even_when_the_bot_has_a_desktop()
    {
        Answer(Status("running"));

        await Open(BotSandboxViewModel.TerminalView);

        Page.View.Should().Be(BotSandboxViewModel.TerminalView);
        Page.Views.Select(tab => tab.Label).Should().Equal("Screen", "Terminal");
        screen.Should().BeNull();
    }

    [Fact]
    public async Task Desktop_input_names_where_clicks_and_keys_go()
    {
        Answer(Status("running"));
        await OpenInControl();

        SandboxScreenViewModel.ControlLabel.Should().Be("Send desktop input");
        Screen.ControlWords.Should().Contain("Clicks and keys go to the sandbox");
        Screen.ControlPictureName.Should().Contain("Click or tap the desktop").And.Contain("arrow keys move the pointer");
        Screen.PointerWords.Should().Be("The pointer is at 512, 384 of 1024 by 768.");
        Screen.TypeLabel.Should().Be("Text to type");
        SandboxScreenViewModel.KeysLabel.Should().Be("Keys");
        SandboxScreenViewModel.Keys.Select(key => key.Name)
            .Should().Equal("Enter", "Tab", "Escape", "Backspace", "Arrow left", "Arrow up", "Arrow down", "Arrow right");
        SandboxScreenViewModel.Keys.Select(key => key.Label).Should().Equal("Enter", "Tab", "Esc", "Backspace", "Left", "Up", "Down", "Right");
        SandboxScreenViewModel.CombinationsLabel.Should().Be("Key combinations");
        SandboxScreenViewModel.Combinations.Select(key => key.Label)
            .Should().Equal("Ctrl+C", "Ctrl+V", "Ctrl+X", "Ctrl+Z", "Ctrl+A", "Ctrl+L", "Alt+Tab", "Alt+F4");
        SandboxScreenViewModel.ScrollLabel.Should().Be("Scroll");
        SandboxScreenViewModel.ClickModeLabel.Should().Be("Desktop click type");
        SandboxScreenViewModel.ClickModes.Select(mode => mode.Label).Should().Equal("Click", "Right click", "Double click");
    }

    [Fact]
    public async Task A_tap_on_the_picture_clicks_where_it_landed_on_the_sandboxs_own_screen()
    {
        Answer(Status("running"));
        await OpenInControl();

        await Screen.Tap(164, 123, 328, 246);

        Sent<SandboxInputRequest>($"{sandbox}/input").Should().ContainSingle()
            .Which.Should().Be(new SandboxInputRequest("click", X: 512, Y: 384, Button: "left", Twice: false));
    }

    [Theory]
    [InlineData("right", "right", false)]
    [InlineData("double", "left", true)]
    public async Task The_click_type_chosen_is_the_click_sent(string mode, string button, bool twice)
    {
        Answer(Status("running"));
        await OpenInControl();
        Screen.ClickMode = mode;

        await Screen.Tap(0, 0, 328, 246);

        Sent<SandboxInputRequest>($"{sandbox}/input").Should().ContainSingle()
            .Which.Should().Be(new SandboxInputRequest("click", X: 0, Y: 0, Button: button, Twice: twice));
    }

    [Fact]
    public async Task While_only_looking_a_tap_sends_nothing()
    {
        Answer(Status("running"));
        await Open();
        await Eventually(() => Screen.DesktopReady.Should().BeTrue());

        await Screen.Tap(10, 10, 328, 246);
        await Screen.Press(["enter"]);

        Requests("POST", $"{sandbox}/input").Should().BeEmpty("the person has not turned on desktop input");
    }

    [Fact]
    public async Task With_the_keyboard_the_arrows_move_the_pointer_and_enter_clicks_there()
    {
        Answer(Status("running"));
        await OpenInControl();

        Screen.MovePointer("ArrowRight", far: false).Should().BeTrue();
        Screen.MovePointer("ArrowDown", far: true).Should().BeTrue();
        Screen.MovePointer("Tab", far: false).Should().BeFalse("any other key is left to the app, so Tab still leaves the picture");
        await Screen.ClickAtPointer();

        Sent<SandboxInputRequest>($"{sandbox}/input").Should().ContainSingle()
            .Which.Should().Match<SandboxInputRequest>(input => input.Kind == "click" && input.X == 528 && input.Y == 448);
        Screen.PointerWords.Should().Be("The pointer is at 528, 448 of 1024 by 768.");
    }

    [Fact]
    public async Task Typed_text_and_keys_are_sent_as_the_person_gave_them()
    {
        Answer(Status("running"));
        await OpenInControl();
        Screen.EnterAfter.Should().BeFalse();

        Screen.Typed = "sample text";
        await Screen.TypeText();
        Sent<SandboxInputRequest>($"{sandbox}/input").Should().ContainSingle();
        await Screen.Press(SandboxScreenViewModel.Keys[0].Keys);

        Sent<SandboxInputRequest>($"{sandbox}/input").Should().BeEquivalentTo(
            [new SandboxInputRequest("type", Text: "sample text"), new SandboxInputRequest("key", Keys: ["enter"])],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task An_active_bot_run_can_use_the_same_desktop()
    {
        Answer(Status("running") with { BotWorking = true });
        await Open();
        await Eventually(() => Screen.DesktopReady.Should().BeTrue());
        Screen.WorkingNote.Should().BeNull("only someone who drives the screen shares it with the bot");

        Screen.SetControl(true);

        Page.Status!.BotWorking.Should().BeTrue();
        BotSandboxViewModel.BotWorkingWords.Should().Be("Bot working");
        Screen.WorkingNote.Should().Be("Sample bot has an active run and may use the same desktop.");
    }

    [Fact]
    public async Task The_keyboard_action_types_text_and_can_press_Enter_when_requested()
    {
        Answer(Status("running"));
        await OpenInControl();

        SandboxScreenViewModel.EnterAfterLabel.Should().Be("Press Enter after typing");
        Screen.TypeAction.Should().Be("Type text");
        Screen.EnterAfter = true;
        Screen.TypeAction.Should().Be("Type and press Enter");

        Screen.Typed = "ls -la";
        await Screen.TypeText();

        Sent<SandboxInputRequest>($"{sandbox}/input").Should().BeEquivalentTo(
            [new SandboxInputRequest("type", Text: "ls -la"), new SandboxInputRequest("key", Keys: ["enter"])],
            options => options.WithStrictOrdering());
        Screen.Typed.Should().BeEmpty();
        keptTyping.Should().Be(1, "the field keeps the focus for the next line");
    }

    [Fact]
    public async Task Text_that_was_refused_stays_in_the_field_and_no_Enter_follows_it()
    {
        Answer(Status("running"));
        server.Refuse("POST", $"{sandbox}/input", HttpStatusCode.BadRequest, "The text has a character the sandbox cannot type.");
        await OpenInControl();
        Screen.EnterAfter = true;

        Screen.Typed = "sample";
        await Screen.TypeText();

        Screen.ScreenProblem.Should().Contain("cannot type");
        Sent<SandboxInputRequest>($"{sandbox}/input").Should().ContainSingle().Which.Kind.Should().Be("type");
        Screen.Typed.Should().Be("sample");
    }

    [Theory]
    [InlineData("tap")]
    [InlineData("text")]
    [InlineData("key")]
    [InlineData("scroll")]
    public async Task Anything_a_person_sends_brings_a_new_picture_at_once_instead_of_on_the_timer(string input)
    {
        Answer(Status("running"));
        await OpenInControl();
        Pictures().Should().Be(1);

        await SendOne(input);

        (await PicturesWithin(2)).Should().Be(2);
    }

    [Fact]
    public async Task Inputs_in_quick_succession_never_pile_up_picture_requests()
    {
        Answer(Status("running"));
        await OpenInControl();
        var gate = server.Hold("POST", $"{sandbox}/screen");

        await SendOne("key");
        (await PicturesWithin(2)).Should().Be(2);
        for (var press = 0; press < 4; press++)
        {
            await SendOne("key");
        }

        await Elapse(TimeSpan.FromMilliseconds(700));
        Pictures().Should().Be(2, "one picture is in flight, and the inputs behind it wait for it");
        gate.SetResult();
        (await PicturesWithin(3)).Should().Be(3);
        await Elapse(TimeSpan.FromMilliseconds(700));
        Pictures().Should().Be(3, "the inputs made while one was in flight add one picture, not one each");
    }

    [Fact]
    public async Task A_scroll_is_sent_at_the_pointer_three_steps_at_a_time()
    {
        Answer(Status("running"));
        await OpenInControl();

        await Screen.Scroll(up: false);

        Sent<SandboxInputRequest>($"{sandbox}/input").Should().ContainSingle()
            .Which.Should().Be(new SandboxInputRequest("scroll", X: 512, Y: 384, Direction: "down", Amount: 3));
    }

    [Fact]
    public async Task While_the_app_is_hidden_no_picture_is_asked_for_and_showing_it_again_brings_one()
    {
        Answer(Status("running"));
        await Open();
        await Eventually(() => Screen.DesktopReady.Should().BeTrue());

        Screen.VisibilityChanged(shown: false);
        await Elapse(TimeSpan.FromSeconds(5));
        Pictures().Should().BeLessThanOrEqualTo(2, "at most the one that was already due");
        var hidden = Pictures();
        await Elapse(TimeSpan.FromSeconds(5));
        Pictures().Should().Be(hidden, "nobody is looking");

        Screen.VisibilityChanged(shown: true);
        (await PicturesWithin(hidden + 1, TimeSpan.FromSeconds(3))).Should().Be(hidden + 1);
    }

    [Fact]
    public async Task A_screen_held_for_the_person_turns_desktop_input_on_and_handing_it_back_turns_it_off()
    {
        Answer(Status("running"));
        await Open();
        await Eventually(() => Screen.DesktopReady.Should().BeTrue());

        Screen.Held = true;
        Screen.Control.Should().BeTrue();
        Screen.Held = false;
        Screen.Control.Should().BeFalse("the person and the bot never both drive the screen");
    }

    #endregion

    #region Zoom

    [Fact]
    public async Task The_picture_has_named_zoom_controls_and_starts_fitted()
    {
        Answer(Status("running"));

        await Open();

        await Eventually(() => Screen.DesktopReady.Should().BeTrue());
        SandboxScreenViewModel.ZoomLabel.Should().Be("Zoom");
        Screen.Zoom.Percent.Should().Be("100%");
        (Screen.Zoom.IsFit, Screen.Zoom.IsMax).Should().Be((true, false));
        Screen.Zoom.Should().Be(ScreenZoom.Fit);
    }

    [Fact]
    public async Task Zoom_in_enlarges_around_the_middle_and_fit_shows_the_whole_screen_again()
    {
        Answer(Status("running"));
        await Open();
        await Eventually(() => Screen.DesktopReady.Should().BeTrue());

        Screen.ZoomIn();
        Screen.ZoomIn();

        Screen.Zoom.Percent.Should().Be("200%");
        Screen.Zoom.Should().Be(new ScreenZoom(2, -0.5, -0.5));
        Screen.Zoom.IsFit.Should().BeFalse();

        Screen.FitScreen();

        Screen.Zoom.Percent.Should().Be("100%");
        Screen.Zoom.Should().Be(ScreenZoom.Fit);
    }

    [Fact]
    public async Task Zoomed_in_view_only_the_arrow_keys_move_the_view_and_send_nothing()
    {
        Answer(Status("running"));
        await Open();
        await Eventually(() => Screen.DesktopReady.Should().BeTrue());
        Screen.PanView("ArrowRight", far: true).Should().BeTrue();
        Screen.Zoom.Should().Be(ScreenZoom.Fit, "at fit there is nothing to move");

        Screen.ZoomIn();
        Screen.ZoomIn();
        SandboxScreenViewModel.EnlargedName.Should().Be("Enlarged screen. Arrow keys move the view.");
        Screen.ViewPictureName.Should().StartWith("Sample bot's sandbox screen");

        Screen.PanView("ArrowRight", far: true).Should().BeTrue();
        Screen.PanView("Tab", far: false).Should().BeFalse("Tab still leaves the picture");

        Screen.Zoom.Should().Be(new ScreenZoom(2, -0.9, -0.5));
        Requests("POST", $"{sandbox}/input").Should().BeEmpty("looking around sends nothing to the sandbox");
    }

    [Fact]
    public async Task A_tap_on_the_enlarged_picture_clicks_the_sandbox_pixel_under_it()
    {
        Answer(Status("running"));
        await OpenInControl();
        Screen.ZoomIn();
        Screen.ZoomIn();

        await Screen.Tap(0, 0, 400, 300);

        Sent<SandboxInputRequest>($"{sandbox}/input").Should().ContainSingle()
            .Which.Should().Be(new SandboxInputRequest("click", X: 256, Y: 192, Button: "left", Twice: false));
    }

    [Fact]
    public async Task A_pinch_is_kept_in_bounds_and_taps_follow_it()
    {
        Answer(Status("running"));
        await OpenInControl();

        Screen.ViewChanged(6, -5, -5);

        Screen.Zoom.Percent.Should().Be("400%");
        Screen.Zoom.Should().Be(new ScreenZoom(4, -3, -3));
        Screen.Zoom.IsMax.Should().BeTrue();
        await Screen.Tap(0, 0, 328, 246);
        Sent<SandboxInputRequest>($"{sandbox}/input").Should().ContainSingle()
            .Which.Should().Match<SandboxInputRequest>(input => input.X == 768 && input.Y == 576);
    }

    [Fact]
    public async Task Zoomed_in_the_view_follows_the_keyboard_pointer()
    {
        Answer(Status("running"));
        await OpenInControl();
        Screen.ZoomIn();
        Screen.ZoomIn();

        for (var press = 0; press < 8; press++)
        {
            Screen.MovePointer("ArrowRight", far: true);
        }

        Screen.PointerWords.Should().Be("The pointer is at 1023, 384 of 1024 by 768.");
        Screen.Zoom.Should().Be(new ScreenZoom(2, -1, -0.5));
    }

    #endregion

    #region Terminal

    [Fact]
    public async Task Opening_the_interactive_terminal_launches_it_then_opens_the_screen()
    {
        Answer(Status("running"));
        await Open(BotSandboxViewModel.TerminalView);
        Page.OffersInteractiveTerminal.Should().BeTrue();

        await Page.OpenTerminal();

        Page.View.Should().Be(BotSandboxViewModel.ScreenView);
        navigator.Current.Should().EndWith("?tab=screen");
        Sent<SandboxInputRequest>($"{sandbox}/input").Should().ContainSingle().Which.Kind.Should().Be("open_terminal");
        screen.Should().NotBeNull("the Screen tab shows");
    }

    [Fact]
    public async Task Choosing_a_tab_puts_it_in_the_address_without_a_step_of_history()
    {
        Answer(Status("running"));
        navigator.NavigateTo(Addresses.BotSandbox(botId));
        await Open();

        Page.SelectView(BotSandboxViewModel.TerminalView);

        navigator.Current.Should().Be(Addresses.BotTerminal(botId));
        navigator.GoBack();
        navigator.Current.Should().Be(Addresses.Home, "the tab replaced the address");
        Page.TerminalHint.Should().Be("Run a shell command for up to five minutes. Use the interactive terminal for prompts.");
        await Eventually(() => screen.Should().BeNull("the picture is not read while the terminal shows"));
    }

    [Fact]
    public async Task Password_entry_is_masked_cleared_and_never_sends_Enter()
    {
        Answer(Status("running"));
        await OpenInControl();
        Screen.PasswordMode.Should().BeFalse();
        Screen.EnterAfter = true;
        Screen.SetPasswordMode(true);
        Screen.TypeLabel.Should().Be("Password");
        Screen.TypeAction.Should().Be("Type password");
        Screen.TypeHint.Should().StartWith("Focus the remote password prompt first.");
        Screen.Typed = "sample password";
        await Screen.TypeText();

        Sent<SandboxInputRequest>($"{sandbox}/input").Should().ContainSingle()
            .Which.Should().Be(new SandboxInputRequest("password", Text: "sample password"));
        Screen.Typed.Should().BeEmpty();
    }

    [Fact]
    public async Task A_command_runs_and_its_exit_code_and_output_are_listed_with_the_commands_of_this_visit()
    {
        Answer(Status("running") with { Desktop = false });
        server.Answer("POST", $"{sandbox}/commands", new SandboxCommandResponse(0, "hello\n", string.Empty, false, false, false));
        await Open();
        Page.TerminalHint.Should().Be("Run a shell command for up to five minutes.");
        Page.CanRun.Should().BeFalse();

        Page.Command = "echo hello";
        Page.CanRun.Should().BeTrue();
        await Page.RunCommand();

        var entry = Page.Entries.Should().ContainSingle().Subject;
        SandboxWords.Outcome(entry.Result!).Should().Be("Exit code 0.");
        BotSandboxViewModel.HistoryLabel.Should().Be("Commands run on this visit");
        entry.Command.Should().Be("echo hello");
        entry.Result!.Stdout.Should().Be("hello\n");
        Page.Command.Should().BeEmpty();
        Page.RunLabel.Should().Be("Run");
        Sent<SandboxCommandRequest>($"{sandbox}/commands").Should().Equal(new SandboxCommandRequest("echo hello"));
    }

    [Fact]
    public async Task A_refused_command_says_why_beside_it()
    {
        Answer(Status("running") with { Desktop = false });
        server.Refuse("POST", $"{sandbox}/commands", HttpStatusCode.Conflict, "This bot has no sandbox.");
        await Open();

        Page.Command = "ls";
        await Page.RunCommand();

        Page.Entries.Should().ContainSingle().Which.Problem.Should().Contain("This bot has no sandbox.");
    }

    [Fact]
    public async Task A_command_that_ends_after_the_page_was_left_does_not_throw()
    {
        Answer(Status("running") with { Desktop = false });
        server.Answer(
            "POST",
            $"{sandbox}/commands",
            new SandboxCommandResponse(0, "done\n", string.Empty, false, false, false));
        var answer = server.Hold("POST", $"{sandbox}/commands");
        await Open();
        Page.Command = "sleep 10";
        var run = Page.RunCommand();
        await Eventually(() => Requests("POST", $"{sandbox}/commands").Should().ContainSingle());

        await Page.DisposeAsync();
        answer.SetResult();

        var ending = () => run;
        await ending.Should().NotThrowAsync(
            "the person left the page, and nothing that ends afterwards may reach the platform");
        Requests("GET", sandbox).Should().ContainSingle("the page that was left does not read the state again");
    }

    [Fact]
    public async Task Opening_the_terminal_window_while_leaving_does_not_throw()
    {
        Answer(Status("running"));
        server.Hold("POST", $"{sandbox}/input");
        await Open(BotSandboxViewModel.TerminalView);
        var open = Page.OpenTerminal();
        await Eventually(() => Requests("POST", $"{sandbox}/input").Should().ContainSingle());

        await Page.DisposeAsync();

        var ending = () => open;
        await ending.Should().NotThrowAsync("leaving cancels the request, which is a quiet end and not a failure");
        Page.ActionProblem.Should().BeNull("leaving is not a problem to report");
        Page.Busy.Should().BeFalse();
    }

    [Fact]
    public async Task Reading_the_state_again_after_the_page_was_left_does_not_throw()
    {
        Answer(Status("running"));
        await Open();
        await Page.DisposeAsync();

        var reading = () => Page.RefreshStatus();

        await reading.Should().NotThrowAsync("the picture can tell the page the sandbox changed after the page closed");
        Requests("GET", sandbox).Should().ContainSingle();
    }

    #endregion

    #region Gestures

    [Fact]
    public void A_press_that_stays_put_is_a_tap_and_changes_no_view()
    {
        var gesture = new ScreenGesture();

        gesture.Down(1, 100, 100, ScreenZoom.Fit);
        gesture.Move(1, 104, 103, 400, 300).Should().BeNull("it has not left the place it went down");

        gesture.Up(1).Should().Be(new ScreenGestureEnd(true, null));
        gesture.IsActive.Should().BeFalse();
    }

    [Fact]
    public void A_drag_on_a_picture_that_is_not_enlarged_moves_nothing_and_never_clicks()
    {
        var gesture = new ScreenGesture();

        gesture.Down(1, 100, 100, ScreenZoom.Fit);
        gesture.Move(1, 100, 160, 400, 300).Should().BeNull();

        gesture.Up(1).Should().Be(new ScreenGestureEnd(false, ScreenZoom.Fit));
    }

    [Fact]
    public void A_drag_on_an_enlarged_picture_moves_it_with_the_finger_and_stops_at_the_edge()
    {
        var gesture = new ScreenGesture();
        var enlarged = new ScreenZoom(2, -0.5, -0.5);

        gesture.Down(1, 200, 150, enlarged);
        gesture.Move(1, 160, 150, 400, 300).Should().Be(new ScreenZoom(2, -0.6, -0.5));
        gesture.Move(1, -400, 150, 400, 300).Should().Be(new ScreenZoom(2, -1, -0.5), "no edge shows past the frame");

        gesture.Up(1).Should().Be(new ScreenGestureEnd(false, new ScreenZoom(2, -1, -0.5)));
    }

    [Fact]
    public void A_pinch_enlarges_around_the_place_between_the_fingers_up_to_four_times()
    {
        var gesture = new ScreenGesture();

        gesture.Down(1, 150, 150, ScreenZoom.Fit);
        gesture.Down(2, 250, 150, ScreenZoom.Fit);
        var shown = gesture.Move(2, 350, 150, 400, 300);

        shown.Should().NotBeNull();
        shown!.Value.Scale.Should().Be(2);
        ScreenPoint.FromShown(250, 150, 400, 300, 1024, 768, shown.Value).Should().Be((512, 384), "what was between the fingers still is");
        gesture.Move(2, 2000, 150, 400, 300)!.Value.Scale.Should().Be(ScreenZoom.MaxScale);

        gesture.Up(2).Should().Be(default(ScreenGestureEnd), "one finger is still down");
        var end = gesture.Up(1);
        end.Tapped.Should().BeFalse("a pinch never clicks in the sandbox");
        end.View!.Value.Scale.Should().Be(ScreenZoom.MaxScale);
    }

    [Fact]
    public void A_press_the_system_takes_away_is_not_a_tap()
    {
        var gesture = new ScreenGesture();

        gesture.Down(1, 100, 100, ScreenZoom.Fit);

        gesture.Up(1, cancelled: true).Should().Be(default(ScreenGestureEnd));
        gesture.Up(7).Should().Be(default(ScreenGestureEnd), "a pointer that never went down here ends nothing");
    }

    #endregion

    #region Touch

    [Fact]
    public async Task A_touch_tap_clicks_and_a_long_press_right_clicks_at_the_pixel_under_the_finger()
    {
        Answer(Status("running"));
        await OpenInControl();

        Screen.Touch([new(ScreenTouchKind.Tap, 164, 123), new(ScreenTouchKind.LongPress, 0, 0)], 328, 246);

        await Eventually(() => Sent<SandboxInputRequest>($"{sandbox}/input").Should().Equal(
            new SandboxInputRequest("click", X: 512, Y: 384, Button: "left", Twice: false),
            new SandboxInputRequest("click", X: 0, Y: 0, Button: "right", Twice: false)));
    }

    [Fact]
    public async Task A_drag_goes_as_one_request_when_the_finger_lifts_and_is_said_once_while_held()
    {
        Answer(Status("running"));
        await OpenInControl();

        Screen.Touch([new(ScreenTouchKind.Press, 0, 0), new(ScreenTouchKind.Drag, 82, 61.5)], 328, 246);

        Screen.ControlWords.Should().Be(SandboxScreenViewModel.DragWords);
        Screen.PointerAt.Should().Be((256, 192), "the pointer follows the finger so the person sees where it lands");
        Requests("POST", $"{sandbox}/input").Should().BeEmpty("nothing is pressed until the finger lifts");

        Screen.Touch([new(ScreenTouchKind.Drag, 164, 123), new(ScreenTouchKind.Release, 164, 123)], 328, 246);

        await Eventually(() => Sent<SandboxInputRequest>($"{sandbox}/input").Should().ContainSingle().Which.Should().Be(
            new SandboxInputRequest("drag", X: 0, Y: 0, Button: "left", X2: 512, Y2: 384)));
        Screen.ControlWords.Should().Be("Clicks and keys go to the sandbox.");
    }

    [Fact]
    public async Task A_drag_that_ends_where_it_started_sends_nothing()
    {
        Answer(Status("running"));
        await OpenInControl();

        Screen.Touch([new(ScreenTouchKind.Press, 10, 10), new(ScreenTouchKind.Release, 10, 10)], 328, 246);
        await Screen.Press(["enter"]);

        Sent<SandboxInputRequest>($"{sandbox}/input").Should().ContainSingle().Which.Kind.Should().Be("key");
    }

    [Fact]
    public async Task Wheel_steps_that_arrive_while_a_scroll_is_on_its_way_go_together_in_the_next_one()
    {
        Answer(Status("running"));
        await OpenInControl();
        var gate = server.Hold("POST", $"{sandbox}/input");

        Screen.Touch([new(ScreenTouchKind.ScrollDown, 164, 123)], 328, 246);
        await Eventually(() => Requests("POST", $"{sandbox}/input").Should().HaveCount(1));
        Screen.Touch([.. Enumerable.Repeat(new ScreenTouch(ScreenTouchKind.ScrollDown, 164, 123), 3)], 328, 246);
        gate.SetResult();

        await Eventually(() => Sent<SandboxInputRequest>($"{sandbox}/input").Should().Equal(
            new SandboxInputRequest("scroll", Direction: "down", Amount: 1, X: 512, Y: 384),
            new SandboxInputRequest("scroll", Direction: "down", Amount: 3, X: 512, Y: 384)));
    }

    [Fact]
    public async Task A_drag_after_a_tap_waits_for_the_tap_and_is_never_dropped()
    {
        Answer(Status("running"));
        await OpenInControl();
        var gate = server.Hold("POST", $"{sandbox}/input");

        Screen.Touch([new(ScreenTouchKind.Tap, 0, 0)], 328, 246);
        Screen.Touch([new(ScreenTouchKind.Press, 0, 0), new(ScreenTouchKind.Release, 164, 123)], 328, 246);
        await Eventually(() => Requests("POST", $"{sandbox}/input").Should().HaveCount(1));
        gate.SetResult();

        await Eventually(() => Sent<SandboxInputRequest>($"{sandbox}/input").Select(input => input.Kind)
            .Should().Equal("click", "drag"));
    }

    [Fact]
    public async Task Too_many_touches_waiting_are_turned_away_in_words()
    {
        Answer(Status("running"));
        await OpenInControl();
        var gate = server.Hold("POST", $"{sandbox}/input");

        var most = SandboxScreenViewModel.MaxTouchesWaiting;
        Screen.Touch([.. Enumerable.Repeat(new ScreenTouch(ScreenTouchKind.Tap, 0, 0), most + 1)], 328, 246);

        Screen.ScreenProblem.Should().StartWith("The sandbox has not caught up with your touches yet.");
        gate.SetResult();
        await Eventually(() => Requests("POST", $"{sandbox}/input").Should().HaveCount(most));
    }

    [Fact]
    public async Task Ending_control_during_a_drag_sends_nothing_and_takes_the_words_away()
    {
        Answer(Status("running"));
        await OpenInControl();
        Screen.Touch([new(ScreenTouchKind.Press, 0, 0), new(ScreenTouchKind.Drag, 164, 123)], 328, 246);

        Screen.SetControl(false);
        Screen.Touch([new(ScreenTouchKind.Release, 164, 123)], 328, 246);
        Screen.Touch([new(ScreenTouchKind.Tap, 164, 123)], 328, 246);

        Screen.TouchWords.Should().BeNull();
        Screen.ControlWords.Should().StartWith("View only.");
        await Screen.TouchesSent();
        Requests("POST", $"{sandbox}/input").Should().BeEmpty("the person no longer drives the screen");
    }

    [Fact]
    public async Task A_scroll_answered_after_control_went_off_and_on_does_not_change_the_new_sessions_wheel()
    {
        Answer(Status("running"));
        await OpenInControl();
        var gate = server.Hold("POST", $"{sandbox}/input");
        Screen.Touch([new(ScreenTouchKind.ScrollDown, 164, 123)], 328, 246);
        await Eventually(() => Requests("POST", $"{sandbox}/input").Should().HaveCount(1));

        Screen.SetControl(false);
        Screen.SetControl(true);
        Screen.Touch([new(ScreenTouchKind.ScrollUp, 164, 123)], 328, 246);
        gate.SetResult();
        await Screen.TouchesSent();

        Sent<SandboxInputRequest>($"{sandbox}/input").Should().Equal(
            new SandboxInputRequest("scroll", Direction: "down", Amount: 1, X: 512, Y: 384),
            new SandboxInputRequest("scroll", Direction: "up", Amount: 1, X: 512, Y: 384));
    }

    [Fact]
    public async Task A_touch_queued_before_control_went_off_is_not_sent_when_it_comes_back_on()
    {
        Answer(Status("running"));
        await OpenInControl();
        var gate = server.Hold("POST", $"{sandbox}/input");
        Screen.Touch([new(ScreenTouchKind.Tap, 0, 0), new(ScreenTouchKind.Tap, 164, 123)], 328, 246);
        await Eventually(() => Requests("POST", $"{sandbox}/input").Should().HaveCount(1));

        Screen.SetControl(false);
        Screen.SetControl(true);
        gate.SetResult();
        await Screen.TouchesSent();

        Sent<SandboxInputRequest>($"{sandbox}/input")
            .Should().ContainSingle("the second tap was made in a session that ended")
            .Which.X.Should().Be(0);
    }

    [Fact]
    public async Task A_scroll_turned_away_because_too_many_wait_leaves_the_wheel_ready_for_the_next()
    {
        Answer(Status("running"));
        await OpenInControl();
        var gate = server.Hold("POST", $"{sandbox}/input");
        var most = SandboxScreenViewModel.MaxTouchesWaiting;
        Screen.Touch([.. Enumerable.Repeat(new ScreenTouch(ScreenTouchKind.Tap, 0, 0), most)], 328, 246);

        Screen.Touch([new(ScreenTouchKind.ScrollDown, 164, 123)], 328, 246);
        gate.SetResult();
        await Screen.TouchesSent();
        Screen.Touch([new(ScreenTouchKind.ScrollDown, 164, 123)], 328, 246);
        await Screen.TouchesSent();

        Sent<SandboxInputRequest>($"{sandbox}/input")[^1].Should().Be(
            new SandboxInputRequest("scroll", Direction: "down", Amount: 1, X: 512, Y: 384));
        Sent<SandboxInputRequest>($"{sandbox}/input").Should().HaveCount(most + 1, "the refused step was not kept");
    }

    [Fact]
    public async Task A_touch_waits_for_a_key_on_its_way_however_long_it_takes_and_is_never_dropped()
    {
        Answer(Status("running"));
        await OpenInControl();
        var gate = server.Hold("POST", $"{sandbox}/input");
        var key = Screen.Press(["enter"]);
        Screen.Touch([new(ScreenTouchKind.Tap, 164, 123)], 328, 246);
        await Eventually(() => Requests("POST", $"{sandbox}/input").Should().HaveCount(1));

        gate.SetResult();
        await key;
        await Screen.TouchesSent();

        Sent<SandboxInputRequest>($"{sandbox}/input").Select(input => input.Kind).Should().Equal("key", "click");
    }

    [Fact]
    public async Task A_failure_nobody_expected_ends_that_touch_only_and_the_next_ones_still_go()
    {
        Answer(Status("running"));
        await OpenInControl();
        server.Fail("POST", $"{sandbox}/input", new InvalidOperationException("Sample: an unexpected failure."));

        Screen.Touch([new(ScreenTouchKind.Tap, 0, 0)], 328, 246);
        await Screen.TouchesSent();
        Screen.ScreenProblem.Should().Be("A touch could not be sent to the sandbox. Try it again.");
        Screen.Sending.Should().BeFalse("the screen takes input again");

        server.Answer("POST", $"{sandbox}/input", HttpStatusCode.NoContent);
        Screen.Touch([new(ScreenTouchKind.Tap, 164, 123)], 328, 246);
        await Screen.TouchesSent();

        Requests("POST", $"{sandbox}/input").Should().HaveCount(2);
        Screen.ScreenProblem.Should().BeNull();
    }

    [Fact]
    public async Task A_scroll_declined_at_once_before_the_desktop_is_ready_leaves_the_wheel_ready_for_the_next()
    {
        Answer(Status("running"));
        var gate = server.Hold("POST", $"{sandbox}/screen");
        await Open();
        Screen.SetControl(true);
        Screen.DesktopReady.Should().BeFalse("the first picture has not arrived");

        Screen.Touch([new(ScreenTouchKind.ScrollDown, 164, 123)], 328, 246);
        await Screen.TouchesSent();
        gate.SetResult();
        await Eventually(() => Screen.DesktopReady.Should().BeTrue());
        Screen.Touch([new(ScreenTouchKind.ScrollDown, 164, 123)], 328, 246);
        await Screen.TouchesSent();

        Sent<SandboxInputRequest>($"{sandbox}/input").Should().ContainSingle().Which.Should().Be(
            new SandboxInputRequest("scroll", Direction: "down", Amount: 1, X: 512, Y: 384));
    }

    [Fact]
    public async Task A_touch_still_queued_when_the_screen_closes_is_never_sent()
    {
        Answer(Status("running"));
        await OpenInControl();
        var gate = server.Hold("POST", $"{sandbox}/input");
        Screen.Touch([new(ScreenTouchKind.Tap, 0, 0), new(ScreenTouchKind.Tap, 164, 123)], 328, 246);
        await Eventually(() => Requests("POST", $"{sandbox}/input").Should().HaveCount(1));

        var closing = Screen.Stop();
        gate.SetResult();
        await closing;
        await Screen.TouchesSent();

        Requests("POST", $"{sandbox}/input").Should().HaveCount(1, "the second tap was for a screen that went away");
    }

    [Fact]
    public async Task A_touch_still_queued_when_the_person_moves_to_another_space_is_never_sent_anywhere()
    {
        Answer(Status("running"));
        await OpenInControl();
        var person = Samples.Session();
        var otherId = Guid.Parse("00000000-0000-7000-8000-0000000000b2");
        var other = new SpaceSummary(otherId, "team", "Sample team", "team", "owner");
        var session = services.GetRequiredService<SessionState>();
        session.Begin(person with { Spaces = [.. person.Spaces, other] });
        var gate = server.Hold("POST", $"{sandbox}/input");
        Screen.Touch([new(ScreenTouchKind.Tap, 0, 0), new(ScreenTouchKind.Tap, 164, 123)], 328, 246);
        await Eventually(() => Requests("POST", $"{sandbox}/input").Should().HaveCount(1));

        session.ChooseSpace(other.Id);
        gate.SetResult();
        await Screen.TouchesSent();

        Requests("POST", $"{sandbox}/input").Should().HaveCount(1, "only the tap made before the move was sent");
        server.Requests.Should().NotContain(
            request => request.Path.Contains(other.Id.ToString(), StringComparison.Ordinal),
            "a touch made for one space never goes to another");
    }

    [Fact]
    public async Task Lifting_after_a_long_press_is_said_before_it_happens_and_the_words_go_with_the_finger()
    {
        Answer(Status("running"));
        await OpenInControl();

        Screen.LongPressReady(true);
        Screen.ControlWords.Should().Be(SandboxScreenViewModel.LongPressWords);

        Screen.LongPressReady(false);
        Screen.ControlWords.Should().Be("Clicks and keys go to the sandbox.");
    }

    #endregion

    #region Helpers

    private static BotSandboxStatus Status(string state, DateTimeOffset? lastUsed = default) =>
        new(Available: true, HasSandbox: true, state, lastUsed ?? (state is "none" ? null : now.AddMinutes(-3.5)), Desktop: true, BotWorking: false, DesktopOffered: true);

    private void Answer(BotSandboxStatus status) => server.Answer("GET", sandbox, status);

    private async Task Open(string? tab = null)
    {
        page = ActivatorUtilities.CreateInstance<BotSandboxViewModel>(services);
        page.HeadingWantsFocus += () => headingFocused++;
        page.PropertyChanged += (_, _) => PlaceScreen();
        await page.Open(botId, tab);
    }

    private async Task OpenInControl()
    {
        await Open();
        await Eventually(() => Screen.DesktopReady.Should().BeTrue());
        Screen.SetControl(true);
    }

    /// <summary>
    /// What the page does with the picture: it is under the Screen tab while that tab shows, is given the state the
    /// page reads, and tells the page to read the state again when the person started the sandbox.
    /// </summary>
    private void PlaceScreen()
    {
        lock (placing)
        {
            if (Page is { Shows: BotSandboxContent.Sandbox, View: BotSandboxViewModel.ScreenView, Bot: { } bot, Status: { } status })
            {
                if (screen is null)
                {
                    screen = ActivatorUtilities.CreateInstance<SandboxScreenViewModel>(services);
                    screen.BotId = botId;
                    screen.BotName = bot.Name;
                    screen.SandboxChanged += Page.RefreshStatus;
                    screen.KeepTyping += () => keptTyping++;
                    screen.Status = status;
                    screen.Start();
                }
                else
                {
                    screen.Status = status;
                }
            }
            else if (screen is { } gone)
            {
                screen = null;
                closing.Add(gone.Stop());
            }
        }
    }

    private int Pictures() => Requests("POST", $"{sandbox}/screen").Count;

    /// <summary>The requests so far, read by position while the screen may be sending another.</summary>
    private List<string> Requests(string method, string path)
    {
        List<string> bodies = [];
        var count = server.Requests.Count;

        for (var index = 0; index < count; index++)
        {
            var request = server.Requests[index];
            if (request.Method == method && request.Path == path)
            {
                bodies.Add(request.Body);
            }
        }

        return bodies;
    }

    /// <summary>Waits until what the screen does on its own makes the assertion hold, up to five seconds.</summary>
    private static async Task Eventually(Action assertion)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(5);

        while (Failure(assertion) is { } notYet)
        {
            if (DateTimeOffset.UtcNow >= until)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(notYet).Throw();
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
        }
    }

    private static Exception? Failure(Action assertion)
    {
        try
        {
            assertion();
            return null;
        }
        catch (Exception notYet)
        {
            return notYet;
        }
    }

    /// <summary>
    /// The pictures asked for, once there are <paramref name="expected"/> or the screen's clock has moved on by
    /// <paramref name="within"/> (a second by default: well inside the two seconds the timer waits). The clock moves in
    /// small steps and the screen gets a moment after each.
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
            await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
        }

        return Pictures();
    }

    /// <summary>Moves the screen's clock on by <paramref name="span"/> in small steps, giving the screen a moment after each.</summary>
    private async Task Elapse(TimeSpan span)
    {
        for (var moved = TimeSpan.Zero; moved < span; moved += step)
        {
            time.Advance(step);
            await Task.Delay(TimeSpan.FromMilliseconds(5), TestContext.Current.CancellationToken);
        }
    }

    private Task SendOne(string input) => input switch
    {
        "tap" => Screen.Tap(10, 10, 328, 246),
        "text" => TypeSample(),
        "key" => Screen.Press(["escape"]),
        _ => Screen.Scroll(up: true),
    };

    private Task TypeSample()
    {
        Screen.Typed = "sample";
        return Screen.TypeText();
    }

    private List<T> Sent<T>(string path) =>
        [.. Requests("POST", path).Select(body => JsonSerializer.Deserialize<T>(body, JsonSerializerOptions.Web)!)];

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
