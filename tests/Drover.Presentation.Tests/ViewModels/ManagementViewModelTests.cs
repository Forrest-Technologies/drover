namespace Drover.Presentation.Tests.ViewModels;

/// <summary>Native management screens use the same server contracts and keep role and failure states distinct.</summary>
public sealed class ManagementViewModelTests : IDisposable
{
    #region Private Fields

    private static readonly Guid connectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly Guid parentId = Guid.Parse("0196f1a0-0000-7000-8000-000000000005");

    private static readonly string space = $"/api/spaces/{Samples.Session().Spaces[0].Id}";

    private readonly FakeServer server = new();

    private readonly SessionState session;

    private readonly Navigator navigator = new();

    #endregion

    #region Constructors

    public ManagementViewModelTests()
    {
        session = new(server.CreateApi());
        server.Answer("GET", "/api/session", Samples.Session());
    }

    #endregion

    #region Properties

    private static Drover.Contracts.Models.ModelConnectionSummary Connection =>
        new(connectionId, "Sample connection", "venice", null, true, "connected", "sample-model", [], null, null);

    #endregion

    #region Public Methods

    public void Dispose() => server.Dispose();

    [Fact]
    public async Task A_member_does_not_request_the_admin_status_report()
    {
        server.Answer("GET", "/api/session", Samples.Session("member"));
        var model = Status();

        await model.Open();

        model.CanSee.Should().BeFalse();
        server.Requests.Should().NotContain(request => request.Path.EndsWith("/status", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Status_keeps_server_and_space_rows_separate_and_rechecks()
    {
        server.Answer("GET", $"{space}/status", new Drover.Contracts.Status.StatusReport(true,
        [
            new("server", "Sample database", "ok", "Sample working.", null),
            new("space", "Sample models", "problem", "Sample refused.", DateTimeOffset.UnixEpoch),
        ]));
        var model = Status();
        await model.Open();

        model.ServerRows.Select(row => row.Name).Should().Equal("Sample database");
        model.SpaceRows.Select(row => row.Name).Should().Equal("Sample models");
        await model.Load();

        server.Requests.Count(request => request.Path == $"{space}/status").Should().Be(2);
        model.Loading.Should().BeFalse();
    }

    [Fact]
    public async Task A_failed_status_request_can_be_retried_without_losing_the_reason()
    {
        server.Refuse("GET", $"{space}/status", HttpStatusCode.ServiceUnavailable, "Sample status unavailable.");
        var model = Status();
        await model.Open();
        model.LoadProblem.Should().Be("The server failed to answer. Try again, and check the server log if it keeps happening.");
        server.Answer("GET", $"{space}/status", new Drover.Contracts.Status.StatusReport(false, []));

        await model.Load();

        model.LoadProblem.Should().BeNull();
        model.Report!.ServerRowsShown.Should().BeFalse();
    }

    [Fact]
    public async Task A_new_bot_opens_an_unsaved_draft_beneath_the_chosen_parent_and_stores_nothing()
    {
        server.Answer("GET", $"{space}/model-connections", new[] { Connection });
        server.Answer("GET", $"{space}/bots", Array.Empty<BotSummary>());
        var model = new BotListViewModel(server.CreateApi(), session, navigator, NullLogger<BotListViewModel>.Instance);
        await model.Open();

        model.Create(parentId);

        navigator.Current.Should().Be(Addresses.NewBot(parentId));
        server.Requests.Should().NotContain(request => request.Method == "POST");
    }

    [Fact]
    public async Task Replacing_a_model_key_clears_the_input_and_preserves_other_connections()
    {
        server.Answer("GET", $"{space}/model-connections", new[] { Connection, Connection with { Id = parentId, Name = "Sample second" } });
        server.Answer("PUT", $"{space}/model-connections/{connectionId}/key", Connection with { HasKey = true });
        var model = Models();
        await model.Open();
        model.StartReplacing(Connection);
        model.NewKey = " sample-new-key ";

        await model.ReplaceKey(Connection);

        model.NewKey.Should().BeEmpty();
        model.Replacing.Should().BeNull();
        model.Connections.Should().HaveCount(2);
        model.NoticeIsProblem.Should().BeFalse();
        server.Sent("PUT", $"{space}/model-connections/{connectionId}/key").Single().Should().Contain("sample-new-key");
    }

    [Fact]
    public async Task A_freshly_tested_connections_catalog_opens_with_its_models_without_a_refresh()
    {
        Drover.Contracts.Models.ModelCatalogResponse[] catalog =
        [
            new(
                "sample-model",
                "Sample model",
                new(null, null, true, true, false, false, [], false, false),
                new(1m, 2m, null, "usd"),
                "private",
                ["default"],
                "available",
                DateTimeOffset.UnixEpoch),
        ];
        server.Answer("GET", $"{space}/model-connections", new[] { Connection });
        server.Answer("POST", $"{space}/model-connections/{connectionId}/test", new Drover.Contracts.Models.ConnectionTestResponse(true, "Sample connected.", null, Connection));
        server.Answer("GET", $"{space}/model-connections/{connectionId}/models", catalog);
        var model = Models();
        await model.Open();

        await model.Test(Connection);
        await model.ShowCatalog(Connection);

        model.Catalog.Should().BeEquivalentTo(catalog);
        server.Sent("POST", $"{space}/model-connections/{connectionId}/models/refresh").Should().BeEmpty("a passing test stocks the catalog; the page presses no refresh");
    }

    [Fact]
    public async Task Only_an_admin_is_offered_to_mark_a_compatible_endpoints_model_and_a_providers_own_models_offer_no_toggle()
    {
        var compatible = Connection with { Kind = "openai_compatible" };
        server.Answer("GET", $"{space}/model-connections", new[] { Connection, compatible });
        var model = Models();
        await model.Open();

        model.CanMarkVision(compatible).Should().BeTrue();
        model.CanMarkVision(Connection).Should().BeFalse("a provider that reports its own capabilities keeps its own value");

        server.Answer("GET", "/api/session", Samples.Session("member"));
        var asMember = new ModelConnectionsViewModel(server.CreateApi(), new(server.CreateApi()), NullLogger<ModelConnectionsViewModel>.Instance);
        await asMember.Open();

        asMember.CanMarkVision(compatible).Should().BeFalse("the server decides, and a member is not offered the control");
    }

    [Fact]
    public async Task Marking_a_model_sends_the_model_and_the_answer_replaces_only_its_row_in_the_shown_catalog()
    {
        var compatible = Connection with { Kind = "openai_compatible" };
        server.Answer("GET", $"{space}/model-connections", new[] { compatible });
        server.Answer("GET", $"{space}/model-connections/{connectionId}/models", new[] { Entry("meta/plain", vision: false), Entry("other", vision: false) });
        server.Answer("PUT", $"{space}/model-connections/{connectionId}/models/vision", Entry("meta/plain", vision: true));
        var model = Models();
        await model.Open();
        await model.ShowCatalog(compatible);

        await model.SetVision(compatible, model.Catalog![0], readsImages: true);

        model.Catalog.Select(row => (row.Model, row.Capabilities.Vision)).Should().Equal(("meta/plain", true), ("other", false));
        model.NoticeIsProblem.Should().BeFalse();
        model.Notice.Should().Contain("reading pictures");
        JsonSerializer.Deserialize<SetModelVisionRequest>(server.Sent("PUT", $"{space}/model-connections/{connectionId}/models/vision").Single(), JsonSerializerOptions.Web)
            .Should().Be(new SetModelVisionRequest("meta/plain", true));
        ModelConnectionsViewModel.VisionLabel(model.Catalog[0]).Should().Be("meta/plain reads pictures");
    }

    [Fact]
    public async Task A_refused_mark_reports_on_the_connection_and_leaves_the_catalog_as_it_was()
    {
        var compatible = Connection with { Kind = "openai_compatible" };
        server.Answer("GET", $"{space}/model-connections", new[] { compatible });
        server.Answer("GET", $"{space}/model-connections/{connectionId}/models", new[] { Entry("plain", vision: false) });
        server.Refuse("PUT", $"{space}/model-connections/{connectionId}/models/vision", HttpStatusCode.NotFound, "Sample model is gone.");
        var model = Models();
        await model.Open();
        await model.ShowCatalog(compatible);

        await model.SetVision(compatible, model.Catalog![0], readsImages: true);

        model.Catalog[0].Capabilities.Vision.Should().BeFalse();
        model.Problems.Should().ContainKey(connectionId);
        model.ChangePending.Should().BeFalse();
    }

    [Fact]
    public void A_model_the_catalog_says_reads_pictures_says_so_in_words_for_everyone()
    {
        ModelConnectionsViewModel.VisionWords(Entry("plain", vision: true)).Should().Be("reads pictures");
        ModelConnectionsViewModel.VisionWords(Entry("plain", vision: false)).Should().BeNull();
    }

    [Fact]
    public async Task A_refused_connection_action_reports_only_on_its_own_row()
    {
        server.Answer("GET", $"{space}/model-connections", new[] { Connection });
        server.Refuse("POST", $"{space}/model-connections/{connectionId}/test", HttpStatusCode.BadRequest, "Sample connection refused.");
        var model = Models();
        await model.Open();

        await model.Test(Connection);

        model.Problems[connectionId].Should().Be("Sample connection refused.");
        model.Notice.Should().BeNull();
        model.ChangePending.Should().BeFalse();
    }

    #endregion

    #region Private Methods

    private static ModelCatalogResponse Entry(string id, bool vision) =>
        new(
            id,
            id,
            new ModelCapabilitiesResponse(null, null, true, true, vision, false, [], false, false, VisionSetByOperator: vision),
            new ModelPriceResponse(null, null, null, "USD"),
            null,
            [],
            "available",
            DateTimeOffset.UnixEpoch);

    private StatusPageViewModel Status() => new(server.CreateApi(), session, NullLogger<StatusPageViewModel>.Instance);

    private ModelConnectionsViewModel Models() => new(server.CreateApi(), session, NullLogger<ModelConnectionsViewModel>.Instance);

    #endregion
}
