namespace Drover.Presentation.Tests.ViewModels;

/// <summary>
/// The share screen against a stand-in server and the one share inbox: the cases of the web client's screen tests,
/// asked of the view model, and the states the web screen shows without a test of its own. Every name and word here is
/// a sample.
/// </summary>
public sealed class ShareToViewModelTests : ViewModelTest
{
    #region Private Fields

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly string space = $"/api/spaces/{SpaceId}";

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

        await screen.Open();

        ShareToViewModel.Heading.Should().Be("Share to Drover");
        ShareToViewModel.Title.Should().Be("Share to Drover · Drover");
        screen.ShowNothing.Should().BeTrue();
        screen.ShowShared.Should().BeFalse();
        ShareToViewModel.NothingCause.Should().Be("Nothing is waiting to be shared. Share text or a file to Drover from another app.");
        ShareToViewModel.GoToCrewLabel.Should().Be("Go to your crew");
        screen.ShowBots.Should().BeFalse();
        screen.Choices.Should().BeEmpty();
        Server.Requests.Should().NotContain(request => request.Path == $"{space}/bots", "there is nothing to place, so the crew is not asked for");
    }

    [Fact]
    public async Task A_share_waiting_without_a_signed_in_person_is_not_shown()
    {
        Server.Answer("GET", "/api/session", HttpStatusCode.Unauthorized);
        Inbox.Offer(new SharedDraft("Sample words from another app", []));
        var screen = Make<ShareToViewModel>();

        await screen.Open();

        screen.ShowNothing.Should().BeTrue();
        screen.ShowShared.Should().BeFalse();
    }

    [Fact]
    public async Task What_was_shared_is_shown_and_choosing_a_bot_opens_its_conversation_with_the_share_for_it_alone()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/bots", new[] { Marshal });
        var shared = new SharedDraft("Sample words from another app", [new SamplePickedFile("notes.txt", "one")]);
        Inbox.Offer(shared);
        var screen = Make<ShareToViewModel>();
        await screen.Open();

        ShareToViewModel.WhatYouSharedLabel.Should().Be("What you shared");
        screen.HasText.Should().BeTrue();
        screen.Excerpt.Should().Be("Sample words from another app");
        screen.Files.Select(file => $"{file.Name} {file.Size}").Should().Equal("notes.txt 3 bytes");
        screen.ShowBots.Should().BeTrue();
        ShareToViewModel.Ask.Should().Be("Which bot gets this?");
        ShareToViewModel.Note.Should().Contain("Nothing is sent until you send it.");
        screen.Choices.Select(choice => choice.Name).Should().Equal("Marshal");
        screen.Choices[0].Hue.Should().Be(BotIdentity.Hue(botId));

        screen.Choices[0].ChooseCommand.Execute(null);

        Navigator.Current.Should().Be(Addresses.Talk(botId));
        Inbox.TakeFor(Guid.NewGuid()).Should().BeNull("no other conversation gets it");
        Inbox.TakeFor(botId).Should().Be(shared);
        Inbox.TakeFor(botId).Should().BeNull("it is handed over once");
    }

    [Fact]
    public async Task Long_text_is_shown_from_its_start_and_all_of_it_goes_into_the_draft()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/bots", new[] { Marshal });
        var words = new string('a', 500);
        Inbox.Offer(new SharedDraft(words, []));
        var screen = Make<ShareToViewModel>();

        await screen.Open();

        screen.Excerpt.Should().Be($"{new string('a', 400)}…");
        screen.HasFiles.Should().BeFalse();
        Inbox.Waiting!.Text.Should().Be(words);
    }

    [Fact]
    public async Task A_share_of_files_alone_shows_no_text()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/bots", new[] { Marshal });
        Inbox.Offer(new SharedDraft(string.Empty, [new SamplePickedFile("notes.txt", "one")]));
        var screen = Make<ShareToViewModel>();

        await screen.Open();

        screen.HasText.Should().BeFalse();
        screen.HasFiles.Should().BeTrue();
    }

    [Fact]
    public async Task Discarding_forgets_what_was_shared()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/bots", new[] { Marshal });
        Inbox.Offer(new SharedDraft("Sample words from another app", []));
        var screen = Make<ShareToViewModel>();
        await screen.Open();
        Navigator.NavigateTo("/share");

        screen.DiscardCommand.Execute(null);

        ShareToViewModel.DiscardLabel.Should().Be("Discard what you shared");
        Inbox.Waiting.Should().BeNull();
        Navigator.Current.Should().Be(Addresses.Home);
    }

    [Fact]
    public async Task A_crew_that_cannot_be_loaded_says_so_and_keeps_what_was_shared()
    {
        SignedIn();
        Server.Refuse("GET", $"{space}/bots", HttpStatusCode.ServiceUnavailable, "Sample outage.");
        Inbox.Offer(new SharedDraft("Sample words from another app", []));
        var screen = Make<ShareToViewModel>();

        await screen.Open();

        screen.ShowFailed.Should().BeTrue();
        screen.ShowBots.Should().BeFalse();
        ShareToViewModel.FailedTitle.Should().Be("Drover could not load your crew");
        ShareToViewModel.TryAgain.Should().Be("Try again");
        screen.Problem.Should().NotBeNullOrWhiteSpace();
        Inbox.Waiting.Should().NotBeNull();

        Server.Answer("GET", $"{space}/bots", new[] { Marshal });
        await screen.LoadCommand.ExecuteAsync(null);

        screen.ShowFailed.Should().BeFalse();
        screen.ShowBots.Should().BeTrue();
    }

    [Fact]
    public async Task A_space_with_no_bot_says_to_make_one_first()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/bots", Array.Empty<BotSummary>());
        Inbox.Offer(new SharedDraft("Sample words from another app", []));
        var screen = Make<ShareToViewModel>();

        await screen.Open();

        screen.ShowNoBots.Should().BeTrue();
        screen.ShowBots.Should().BeFalse();
        ShareToViewModel.NoBotsCause.Should().Be("There is no bot to share this with yet. Make one first; what you shared waits here.");
    }

    [Fact]
    public async Task While_the_crew_loads_the_screen_says_so()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/bots", new[] { Marshal });
        var held = Server.Hold("GET", $"{space}/bots");
        Inbox.Offer(new SharedDraft("Sample words from another app", []));
        var screen = Make<ShareToViewModel>();

        var opening = screen.Open();
        await Eventually(() => Server.Requests.Any(request => request.Path == $"{space}/bots"));

        screen.ShowLoading.Should().BeTrue();
        ShareToViewModel.LoadingNote.Should().Be("Loading your crew.");
        held.SetResult();
        await opening;
        screen.ShowLoading.Should().BeFalse();
        screen.ShowBots.Should().BeTrue();
    }

    #endregion
}
