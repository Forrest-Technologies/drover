namespace Drover.Ui.Tests.Screens;

/// <summary>The list of bots against a stand-in server. Every name and value here is a sample.</summary>
public sealed class BotListTests : ScreenTest
{
    #region Private Fields

    private static readonly Guid connectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly string space = $"/api/spaces/{SpaceId}";

    #endregion

    #region Properties

    private static ModelConnectionSummary Connection { get; } =
        new(connectionId, "Sample connection", "venice", null, true, "connected", "sample-default", [], null, null);

    private static BotSummary Atlas { get; } = new(botId, "Atlas", "atlas", "Sample instructions", connectionId, "sample-default", "paused");

    #endregion

    #region Public Methods

    [Fact]
    public void Each_bot_is_a_link_to_its_editor_with_its_handle_model_and_state_in_words()
    {
        Space(bots: [Atlas]);

        var page = Render<BotList>();

        page.WaitForAssertion(() => page.Find("a.dv-bot-name").TextContent.Should().Be("Atlas"));
        page.Find("h1").TextContent.Should().Be("Bots");
        page.Find("a.dv-bot-name").GetAttribute("href").Should().Be(Addresses.Bot(botId));
        page.Find(".dv-bot-facts").TextContent.Should().Be("@atlas · sample-default");
        page.Find(".dv-bot .dv-badge").TextContent.Should().Be("Paused");
    }

    [Fact]
    public async Task New_bot_makes_one_on_the_connection_and_opens_its_editor()
    {
        Space(bots: [Atlas]);
        Server.Answer("POST", $"{space}/bots", Atlas with { Id = Guid.Parse("0196f1a0-0000-7000-8000-000000000007"), Name = "New bot" }, HttpStatusCode.Created);
        var page = Render<BotList>();
        page.WaitForElement(".dv-bots-header .dv-button");

        await page.Find(".dv-bots-header .dv-button").ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => Address.Should().Be("bots/0196f1a0-0000-7000-8000-000000000007"));
        Server.Sent("POST", $"{space}/bots").Should().ContainSingle().Which.Should().Contain("\"name\":\"New bot\"").And.Contain("\"model\":\"sample-default\"");
    }

    [Fact]
    public void A_space_with_no_bot_offers_to_make_one()
    {
        Space(bots: []);

        var page = Render<BotList>();

        page.WaitForAssertion(() => page.Find(".dv-empty-title").TextContent.Should().Be("No bot yet"));
        page.Find(".dv-empty .dv-button").TextContent.Trim().Should().Be("Make a bot");
    }

    [Fact]
    public void A_space_with_no_model_says_what_is_missing_and_links_to_it()
    {
        Server.Answer("GET", "/api/session", Person);
        Server.Answer("GET", $"{space}/model-connections", Array.Empty<ModelConnectionSummary>());
        Server.Answer("GET", $"{space}/bots", Array.Empty<BotSummary>());

        var page = Render<BotList>();

        page.WaitForAssertion(() => page.Find(".dv-empty-title").TextContent.Should().Be("No model is connected"));
        page.Find(".dv-empty-action").GetAttribute("href").Should().Be(Addresses.SetupModel);
    }

    [Fact]
    public void A_member_sees_the_bots_and_is_told_who_changes_them()
    {
        Server.Answer("GET", "/api/session", Person with { Spaces = [new(SpaceId, "personal", "Sam Sample", "sam", "member")] });
        Server.Answer("GET", $"{space}/model-connections", new[] { Connection });
        Server.Answer("GET", $"{space}/bots", new[] { Atlas });

        var page = Render<BotList>();

        page.WaitForAssertion(() => page.Find(".dv-bot-name").TextContent.Should().Be("Atlas"));
        page.FindAll("a.dv-bot-name").Should().BeEmpty("a member cannot open the editor");
        page.FindAll(".dv-bots-header .dv-button").Should().BeEmpty();
        page.Find(".dv-bots-hint").TextContent.Should().Contain("Owners and admins");
    }

    [Fact]
    public void A_server_that_cannot_be_reached_says_so()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/bots", HttpStatusCode.ServiceUnavailable);
        Server.Answer("GET", $"{space}/model-connections", HttpStatusCode.ServiceUnavailable);

        var page = Render<BotList>();

        page.WaitForAssertion(() => page.Find(".dv-empty-title").TextContent.Should().Be("Drover could not load the bots"));
    }

    #endregion

    #region Helpers

    private void Space(BotSummary[] bots)
    {
        SignedIn();
        Server.Answer("GET", $"{space}/model-connections", new[] { Connection });
        Server.Answer("GET", $"{space}/bots", bots);
    }

    #endregion
}
