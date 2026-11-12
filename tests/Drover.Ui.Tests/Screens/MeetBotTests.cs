namespace Drover.Ui.Tests.Screens;

public sealed class MeetBotTests : ScreenTest
{
    #region Private Fields

    private static readonly Guid connectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly string space = $"/api/spaces/{SpaceId}";

    #endregion

    #region Constructors

    public MeetBotTests()
    {
        SignedIn();
        Server.Answer("GET", $"{space}/bots", Array.Empty<BotSummary>());
        Server.Answer("GET", $"{space}/model-connections", new[] { Connection });
    }

    #endregion

    #region Properties

    private static ModelConnectionSummary Connection { get; } =
        new(connectionId, "Sample connection", "venice", null, true, "connected", null, [], null, null);

    private static BotSummary Atlas { get; } = new(botId, "Atlas", "atlas", "Sample instructions", connectionId, "sample-default", "active");

    #endregion

    #region Public Methods

    [Fact]
    public void The_step_asks_one_thing_the_model_and_starts_on_the_providers_default()
    {
        ProviderLists(Model("sample-large", []), Model("sample-default", ["default"]));

        var page = Render<MeetBot>();

        page.WaitForAssertion(() => page.Find("select").GetAttribute("value").Should().Be("sample-default"));
        page.Find("h1").TextContent.Should().Be("Meet your first bot");
        page.Find("[aria-current=step]").TextContent.Should().Contain("Meet your first bot");
        page.Find($"label[for={page.Find("select").Id}]").TextContent.Should().Be("Model");
        page.FindAll("option").Select(option => option.TextContent).Should().Equal("sample-default", "sample-large");
        page.Find("button[type=submit]").TextContent.Trim().Should().Be("Create Atlas");
    }

    [Fact]
    public void An_endpoint_that_lists_no_models_asks_for_the_name_instead()
    {
        ProviderLists();

        var page = Render<MeetBot>();

        page.WaitForAssertion(() => page.Find(".dv-field-label").TextContent.Should().Be("Model name"));
        page.Find(".dv-field-hint").TextContent.Should().Be("This endpoint does not list its models. Type the name it expects.");
        page.FindAll("select").Should().BeEmpty();
    }

    [Fact]
    public void Creating_the_bot_shows_it_with_its_name_its_model_and_a_way_into_a_conversation()
    {
        ProviderLists(Model("sample-default", ["default"]));
        Server.Answer("POST", $"{space}/bots", Atlas, HttpStatusCode.Created);
        var page = Render<MeetBot>();
        page.WaitForElement("form");

        page.Find("form").Submit();

        page.WaitForAssertion(() => page.Find("[role=img]").GetAttribute("aria-label").Should().Be("Atlas"));
        var sent = JsonSerializer.Deserialize<CreateBotRequest>(Server.Sent("POST", $"{space}/bots").Single(), JsonSerializerOptions.Web)!;
        sent.Name.Should().Be("Atlas");
        sent.ModelConnectionId.Should().Be(connectionId);
        sent.Model.Should().Be("sample-default");
        page.Find(".dv-meet-model code").TextContent.Should().Be("sample-default");
        var start = page.Find("a.dv-meet-start");
        start.TextContent.Should().Be("Start a conversation");
        start.GetAttribute("href").Should().Be("/new");
    }

    [Fact]
    public void A_refused_bot_says_why_and_keeps_the_form()
    {
        ProviderLists(Model("sample-default", ["default"]));
        Server.Refuse("POST", $"{space}/bots", HttpStatusCode.BadRequest, "Choose a model for the bot.");
        var page = Render<MeetBot>();
        page.WaitForElement("form");

        page.Find("form").Submit();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Trim().Should().Be("Choose a model for the bot."));
        page.Find("button[type=submit]").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void A_space_that_already_has_a_bot_meets_that_bot_and_makes_no_other()
    {
        Server.Answer("GET", $"{space}/bots", new[] { Atlas });

        var page = Render<MeetBot>();

        page.WaitForAssertion(() => page.Find(".dv-meet-name").TextContent.Should().Be("Atlas"));
        page.FindAll("form").Should().BeEmpty();
    }

    [Fact]
    public void With_no_model_connected_the_step_says_so_and_links_to_the_step_before()
    {
        Server.Answer("GET", $"{space}/model-connections", Array.Empty<ModelConnectionSummary>());

        var page = Render<MeetBot>();

        page.WaitForAssertion(() => page.Find(".dv-empty-title").TextContent.Should().Be("No model is connected"));
        page.Find(".dv-empty-action").GetAttribute("href").Should().Be("/setup/model");
    }

    [Fact]
    public void A_server_that_cannot_be_reached_offers_to_try_again()
    {
        Server.Answer("GET", $"{space}/bots", HttpStatusCode.InternalServerError);

        var page = Render<MeetBot>();

        page.WaitForAssertion(() => page.Find("[role=alert]").TextContent.Trim().Should().StartWith("The server failed to answer."));
        page.FindAll("button").Select(button => button.TextContent.Trim()).Should().Equal("Try again");
    }

    #endregion

    #region Private Methods

    private static ModelCatalogResponse Model(string id, string[] traits) =>
        new(id, id, new(null, null, true, true, false, false, [], false, false), new(null, null, null, "usd"), null, traits, "available", DateTimeOffset.UnixEpoch);

    private void ProviderLists(params ModelCatalogResponse[] models) =>
        Server.Answer("POST", $"{space}/model-connections/{connectionId}/models/refresh", new ModelRefreshResponse(true, null, null, models));

    #endregion
}
