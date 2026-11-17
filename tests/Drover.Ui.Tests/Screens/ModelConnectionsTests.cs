namespace Drover.Ui.Tests.Screens;

public sealed class ModelConnectionsTests : ScreenTest
{
    #region Private Fields

    private static readonly Guid connectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly string connections = $"/api/spaces/{SpaceId}/model-connections";

    private static readonly string connectionPath = $"{connections}/{connectionId}";

    private static readonly ModelConnectionSummary connection =
        new(connectionId, "Sample provider", "venice", null, true, "ready", null, [], null, null);

    #endregion

    #region Public Methods

    [Fact]
    public void An_owner_can_add_and_test_a_provider_then_replace_its_key()
    {
        SignedIn();
        Server.Answer("GET", connections, Array.Empty<ModelConnectionSummary>())
            .Answer("POST", connections, connection)
            .Answer("POST", $"{connectionPath}/test", new ConnectionTestResponse(true, "Connection works.", null, connection))
            .Answer("PUT", $"{connectionPath}/key", connection);

        var page = Render<Drover.Ui.Pages.Models.ModelConnections>();
        page.FindAll("button").Single(button => button.TextContent.Trim() == "Add a model connection").Click();
        var firstKey = Guid.NewGuid().ToString("N");
        page.Find("input[type=password]").Input(firstKey);
        page.Find("form").Submit();
        page.WaitForAssertion(() => page.Markup.Should().Contain("Connection works.").And.NotContain(firstKey));

        page.FindAll("button").Single(button => button.TextContent.Trim() == "Replace key").Click();
        var replacement = Guid.NewGuid().ToString("N");
        page.Find("input[type=password]").Input(replacement);
        page.Find("form").Submit();
        page.WaitForAssertion(() => page.Markup.Should().Contain("is replaced").And.NotContain(replacement));
        Server.Requests.Should().Contain(request =>
            request.Method == "PUT" && request.Path == $"{connectionPath}/key" && request.Body.Contains(replacement));
    }

    [Fact]
    public void Loading_an_empty_list_explains_how_to_add_the_first_connection()
    {
        SignedIn();
        Server.Answer("GET", connections, Array.Empty<ModelConnectionSummary>());
        var loading = Server.Hold("GET", connections);

        var page = Render<Drover.Ui.Pages.Models.ModelConnections>();
        page.WaitForAssertion(() => page.Find(".dv-models-hint[role=status]").TextContent.Should().Be("Loading the model connections."));
        loading.SetResult();

        page.WaitForAssertion(() => page.Find(".dv-empty-title").TextContent.Should().Be("No model connection yet"));
        page.Find(".dv-empty button").TextContent.Trim().Should().Be("Add a model connection");
    }

    [Fact]
    public void A_connection_list_error_offers_a_retry()
    {
        SignedIn();
        Server.Answer("GET", connections, HttpStatusCode.ServiceUnavailable);

        var page = Render<Drover.Ui.Pages.Models.ModelConnections>();

        page.WaitForAssertion(() => page.Find(".dv-empty-title").TextContent.Should().Be("Drover could not load the model connections"));
        page.Find(".dv-empty button").TextContent.Trim().Should().Be("Try again");
    }

    [Fact]
    public void A_member_can_view_models_and_cannot_change_the_connection()
    {
        var entry = new ModelCatalogResponse("model-a", "Example model",
            new(null, null, true, true, false, false, [], false, false),
            new(null, null, null, "usd"), null, [], "available", DateTimeOffset.UnixEpoch);
        Server.Answer("GET", "/api/session", Person with { Spaces = [Person.Spaces[0] with { Role = "member" }] })
            .Answer("GET", connections, new[] { connection })
            .Answer("GET", $"{connectionPath}/models", new[] { entry });

        var page = Render<Drover.Ui.Pages.Models.ModelConnections>();
        page.FindAll("button").Single(button => button.TextContent.Trim() == "View models").Click();

        page.WaitForAssertion(() => page.Find("ul[aria-label='Models from Sample provider']").TextContent
            .Should().Contain("Example model").And.Contain("model-a"));
        page.FindAll("button").Select(button => button.TextContent.Trim())
            .Should().NotContain(["Add a model connection", "Test connection", "Replace key", "Refresh models"]);
    }

    #endregion
}
