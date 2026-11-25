namespace Drover.Presentation.Tests.ViewModels;

/// <summary>
/// The list of bots against a stand-in server: the cases of the web client's screen tests, asked of the view model.
/// Every name and value here is a sample.
/// </summary>
public sealed class BotListViewModelTests : ViewModelTest
{
    #region Private Fields

    private static readonly Guid connectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly string space = $"/api/spaces/{SpaceId}";

    #endregion

    #region Properties

    private static ModelConnectionSummary Connection { get; } =
        new(connectionId, "Sample connection", "venice", null, true, "connected", "sample-default", [], null, null);

    private static BotSummary Marshal { get; } = new(botId, "Marshal", "marshal", "Sample instructions", connectionId, "sample-default", "paused");

    #endregion

    #region Public Methods

    [Fact]
    public async Task Each_bot_is_a_link_to_its_editor_with_its_handle_model_and_state_in_words()
    {
        Space(bots: [Marshal]);
        var list = Make<BotListViewModel>();

        await list.Open();

        list.State.Should().Be(BotListState.Listed);
        BotListViewModel.Heading.Should().Be("Bots");
        list.CanEdit.Should().BeTrue("an owner opens the editor from the bot's name");
        var row = list.Crew.Should().ContainSingle().Subject;
        row.Bot.Name.Should().Be("Marshal");
        Addresses.Bot(row.Bot.Id).Should().Be(Addresses.Bot(botId));
        list.Facts(row.Bot).Should().Be("@marshal · sample-default");
        (row.Bot.Status is "paused").Should().BeTrue();
        BotListViewModel.PausedBadge.Should().Be("Paused");
    }

    [Fact]
    public async Task New_bot_opens_an_unsaved_draft_and_stores_nothing()
    {
        Space(bots: [Marshal]);
        var list = Make<BotListViewModel>();
        await list.Open();
        list.ShowsNewBot.Should().BeTrue();
        BotListViewModel.NewBotLabel.Should().Be("New bot");

        list.CreateCommand.Execute(null);

        Navigator.Current.Should().Be("/bots/new");
        Server.Requests.Should().NotContain(request => request.Method == "POST", "nothing is stored until the person saves");
    }

    [Fact]
    public async Task A_bot_can_be_made_under_another_and_the_list_sets_it_in_beneath()
    {
        var helperId = Guid.Parse("0196f1a0-0000-7000-8000-000000000008");
        var helper = Marshal with { Id = helperId, Name = "Sample helper", Handle = "sample-helper", Status = "active", ParentBotId = botId, MadeByBotId = botId };
        Space(bots: [Marshal, helper]);
        var list = Make<BotListViewModel>();
        await list.Open();

        var top = list.Crew.Should().ContainSingle().Subject;
        BotListViewModel.BeneathName(top.Bot).Should().Be("Bots under Marshal");
        var beneath = top.Children.Should().ContainSingle().Subject;
        beneath.Bot.Name.Should().Be("Sample helper");
        list.Facts(beneath.Bot).Should().Be("@sample-helper · sample-default · made by Marshal");
        list.ShowsAddUnder.Should().BeTrue();
        BotSummary[] rows = [top.Bot, beneath.Bot];
        rows.Select(BotListViewModel.AddUnderLabel).Should().Equal("New bot under Marshal", "New bot under Sample helper");

        list.CreateCommand.Execute(helperId);

        Navigator.Current.Should().Be($"/bots/new?parent={helperId}", "the draft keeps its parent");
        Server.Requests.Should().NotContain(request => request.Method == "POST");
    }

    [Fact]
    public async Task A_space_with_no_bot_offers_to_make_one()
    {
        Space(bots: []);
        var list = Make<BotListViewModel>();

        await list.Open();

        list.State.Should().Be(BotListState.NoBot);
        BotListViewModel.NoBotTitle.Should().Be("No bot yet");
        BotListViewModel.MakeBotLabel.Should().Be("Make a bot");
        list.ShowsNewBot.Should().BeFalse("the empty state holds the one action");
    }

    [Fact]
    public async Task A_space_with_no_model_says_what_is_missing_and_links_to_it()
    {
        Server.Answer("GET", "/api/session", Person);
        Server.Answer("GET", $"{space}/model-connections", Array.Empty<ModelConnectionSummary>());
        Server.Answer("GET", $"{space}/bots", Array.Empty<BotSummary>());
        var list = Make<BotListViewModel>();

        await list.Open();

        list.State.Should().Be(BotListState.NoModel);
        BotListViewModel.NoModelTitle.Should().Be("No model is connected");
        BotListViewModel.NoModelAddress.Should().Be(Addresses.SetupModel);
    }

    [Fact]
    public async Task A_member_sees_the_bots_and_is_told_who_changes_them()
    {
        Server.Answer("GET", "/api/session", Person with { Spaces = [new(SpaceId, "personal", "Sam Sample", "sam", "member")] });
        Server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection]);
        Server.Answer("GET", $"{space}/bots", (BotSummary[])[Marshal]);
        var list = Make<BotListViewModel>();

        await list.Open();

        list.Crew.Should().ContainSingle().Which.Bot.Name.Should().Be("Marshal");
        list.CanEdit.Should().BeFalse("a member cannot open the editor");
        list.ShowsNewBot.Should().BeFalse();
        list.ShowsAddUnder.Should().BeFalse();
        BotListViewModel.MemberHint.Should().Contain("Owners and admins");
    }

    [Fact]
    public async Task A_member_in_a_space_with_no_bot_is_told_who_makes_one()
    {
        Server.Answer("GET", "/api/session", Person with { Spaces = [new(SpaceId, "personal", "Sam Sample", "sam", "member")] });
        Server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection]);
        Server.Answer("GET", $"{space}/bots", Array.Empty<BotSummary>());
        var list = Make<BotListViewModel>();

        await list.Open();

        list.State.Should().Be(BotListState.NoBotForMember);
        BotListViewModel.NoBotForMember.Should().Be("This space has no bots yet. An owner or admin of the space can create one.");
    }

    [Fact]
    public async Task A_server_that_cannot_be_reached_says_so()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/bots", HttpStatusCode.ServiceUnavailable);
        Server.Answer("GET", $"{space}/model-connections", HttpStatusCode.ServiceUnavailable);
        var list = Make<BotListViewModel>();

        await list.Open();

        list.State.Should().Be(BotListState.Failed);
        BotListViewModel.LoadFailedTitle.Should().Be("Drover could not load the bots");
        list.LoadProblem.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_list_says_it_is_loading_until_the_server_answers()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection]);
        Server.Answer("GET", $"{space}/bots", (BotSummary[])[Marshal]);
        var held = Server.Hold("GET", $"{space}/bots");
        var list = Make<BotListViewModel>();

        var opening = list.Open();
        list.State.Should().Be(BotListState.Loading);
        BotListViewModel.Loading.Should().Be("Loading the bots.");

        held.SetResult();
        await opening;
        list.State.Should().Be(BotListState.Listed);
    }

    #endregion

    #region Helpers

    private void Space(BotSummary[] bots)
    {
        SignedIn();
        Server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection]);
        Server.Answer("GET", $"{space}/bots", bots);
    }

    #endregion
}
