namespace Drover.Presentation.Tests.ViewModels;

public sealed class ShareToViewModelTests : ServerScreenTest
{
    #region Private Fields

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly string space = $"/api/spaces/{SpaceId}";

    private readonly ShareTray tray = new();

    #endregion

    #region Constructors

    public ShareToViewModelTests()
    {
        Services.AddSingleton(tray);
    }

    #endregion

    #region Properties

    private static BotSummary Marshal { get; } =
        new(botId, "Marshal", "marshal", "Sample instructions", Guid.Parse("0196f1a0-0000-7000-8000-000000000003"), "sample-default", "active");

    #endregion

    #region Public Methods

    [Fact]
    public async Task With_nothing_shared_the_screen_says_so_and_points_back_to_the_crew()
    {
        SignedIn();
        var screen = Make<ShareToViewModel>();

        await screen.Start();

        ShareToViewModel.Heading.Should().Be("Share to Drover");
        screen.ShowNothing.Should().BeTrue();
        screen.ShowShared.Should().BeFalse();
        ShareToViewModel.NothingCause.Should().Be("Nothing is waiting to be shared. Share text or a file to Drover from another app.");
        ShareToViewModel.GoToCrewLabel.Should().Be("Go to your crew");
        screen.ShowBots.Should().BeFalse();
        Server.Requests.Should().NotContain(request => request.Path == $"{space}/bots", "there is nothing to place, so the crew is not asked for");
    }

    [Fact]
    public async Task What_was_shared_is_shown_and_choosing_a_bot_opens_its_conversation_with_the_share_for_it_alone()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/bots", new[] { Marshal });
        var shared = new SharedContent("Sample words from another app", [new ShareFile("notes.txt", 3)]);
        tray.Offer(shared);
        var screen = Make<ShareToViewModel>();
        await screen.Start();

        screen.HasText.Should().BeTrue();
        screen.Excerpt.Should().Be("Sample words from another app");
        screen.Files.Select(file => $"{file.Name} {Words.Size(file.Size)}").Should().Equal("notes.txt 3 bytes");
        screen.ShowBots.Should().BeTrue();
        ShareToViewModel.WhatYouSharedLabel.Should().Be("What you shared");
        ShareToViewModel.Ask.Should().Be("Which bot gets this?");
        ShareToViewModel.Note.Should().Contain("Nothing is sent until you send it.");
        screen.Choices.Select(choice => choice.Name).Should().Equal("Marshal");
        screen.Choices[0].Hue.Should().Be(BotIdentity.Hue(botId));
        screen.Choose(screen.Choices[0]);

        Navigator.Current.Should().Be($"/talk/{botId}");
        tray.TakeFor(Guid.NewGuid()).Should().BeNull("no other conversation gets it");
        tray.TakeFor(botId).Should().Be(shared);
        tray.TakeFor(botId).Should().BeNull("it is handed over once");
    }

    [Fact]
    public async Task Long_text_is_shown_from_its_start_and_all_of_it_goes_into_the_draft()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/bots", new[] { Marshal });
        var words = new string('a', 500);
        tray.Offer(new SharedContent(words, []));
        var screen = Make<ShareToViewModel>();

        await screen.Start();

        screen.Excerpt.Should().Be($"{new string('a', 400)}…");
        screen.HasFiles.Should().BeFalse();
        tray.Waiting!.Text.Should().Be(words);
    }

    [Fact]
    public async Task A_share_of_files_alone_shows_no_text()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/bots", new[] { Marshal });
        tray.Offer(new SharedContent(string.Empty, [new ShareFile("notes.txt", 3)]));
        var screen = Make<ShareToViewModel>();

        await screen.Start();

        screen.HasText.Should().BeFalse();
        screen.HasFiles.Should().BeTrue();
    }

    [Fact]
    public async Task Discarding_forgets_what_was_shared()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/bots", new[] { Marshal });
        tray.Offer(new SharedContent("Sample words from another app", []));
        var screen = Make<ShareToViewModel>();
        await screen.Start();

        screen.DiscardCommand.Execute(null);

        tray.Waiting.Should().BeNull();
        Navigator.Current.Should().Be(Addresses.Home);
        ShareToViewModel.DiscardLabel.Should().Be("Discard what you shared");
    }

    [Fact]
    public async Task A_crew_that_cannot_be_loaded_says_so_and_keeps_what_was_shared()
    {
        SignedIn();
        Server.Refuse("GET", $"{space}/bots", HttpStatusCode.BadRequest, "Sample outage.");
        tray.Offer(new SharedContent("Sample words from another app", []));
        var screen = Make<ShareToViewModel>();

        await screen.Start();

        screen.ShowFailed.Should().BeTrue();
        screen.Problem.Should().Be("Sample outage.");
        ShareToViewModel.FailedTitle.Should().Be("Drover could not load your crew");
        tray.Waiting.Should().NotBeNull();

        Server.Answer("GET", $"{space}/bots", new[] { Marshal });
        await screen.LoadCommand.ExecuteAsync(null);
        screen.ShowBots.Should().BeTrue();
    }

    [Fact]
    public async Task A_space_with_no_bot_says_to_make_one_first()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/bots", Array.Empty<BotSummary>());
        tray.Offer(new SharedContent("Sample words from another app", []));
        var screen = Make<ShareToViewModel>();

        await screen.Start();

        screen.ShowNoBots.Should().BeTrue();
        ShareToViewModel.NoBotsCause.Should().Be("There is no bot to share this with yet. Make one first; what you shared waits here.");
    }

    [Fact]
    public async Task While_the_crew_loads_the_screen_says_so()
    {
        SignedIn();
        var holding = Server.Hold("GET", $"{space}/bots");
        Server.Answer("GET", $"{space}/bots", new[] { Marshal });
        tray.Offer(new SharedContent("Sample words from another app", []));
        var screen = Make<ShareToViewModel>();

        var started = screen.Start();
        await WaitUntil(() => screen.ShowLoading);

        ShareToViewModel.LoadingNote.Should().Be("Loading your crew.");
        holding.SetResult();
        await started;
        screen.ShowLoading.Should().BeFalse();
    }

    [Fact]
    public void A_new_share_takes_the_place_of_one_that_was_never_placed()
    {
        tray.Offer(new SharedContent("Sample first", []));
        tray.SendTo(botId);

        tray.Offer(new SharedContent("Sample second", []));

        tray.TakeFor(botId).Should().BeNull("the choice belonged to the share that was replaced");
        tray.Waiting!.Text.Should().Be("Sample second");
    }

    #endregion
}
