namespace Drover.Presentation.Tests.ViewModels;

public sealed class ModelConnectionsViewModelTests : ServerScreenTest
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
    public async Task An_owner_can_add_and_test_a_provider_then_replace_its_key()
    {
        SignedIn();
        Server.Answer("GET", connections, Array.Empty<ModelConnectionSummary>())
            .Answer("POST", connections, connection)
            .Answer("POST", $"{connectionPath}/test", new ConnectionTestResponse(true, "Connection works.", null, connection))
            .Answer("PUT", $"{connectionPath}/key", connection);
        var screen = Make<ModelConnectionsViewModel>();
        await screen.Start();

        screen.StartAddingCommand.Execute(null);
        screen.Adding.Should().BeTrue();
        var firstKey = Guid.NewGuid().ToString("N");
        screen.ApiKey = firstKey;
        await screen.AddCommand.ExecuteAsync(null);

        screen.Notice.Should().Be("Connection works.").And.NotContain(firstKey);
        screen.NoticeKind.Should().Be(NoteKind.Ok);
        screen.Adding.Should().BeFalse();
        screen.ApiKey.Should().BeEmpty("a key that was sent is not kept");
        var card = screen.Cards.Should().ContainSingle().Subject;
        card.StatusWords.Should().Be("Working");

        card.StartReplacingCommand.Execute(null);
        card.Replacing.Should().BeTrue();
        var replacement = Guid.NewGuid().ToString("N");
        card.NewKey = replacement;
        await card.SaveKeyCommand.ExecuteAsync(null);

        screen.Notice.Should().Contain("is replaced").And.NotContain(replacement);
        card.Replacing.Should().BeFalse();
        card.NewKey.Should().BeEmpty();
        Server.Requests.Should().Contain(request =>
            request.Method == "PUT" && request.Path == $"{connectionPath}/key" && request.Body.Contains(replacement));
    }

    [Fact]
    public async Task A_compatible_endpoint_without_a_name_is_called_by_its_host_and_sends_its_address()
    {
        SignedIn();
        Server.Answer("GET", connections, Array.Empty<ModelConnectionSummary>())
            .Answer("POST", connections, connection)
            .Answer("POST", $"{connectionPath}/test", new ConnectionTestResponse(true, "Connection works.", null, connection));
        var screen = Make<ModelConnectionsViewModel>();
        await screen.Start();
        screen.StartAddingCommand.Execute(null);

        screen.Kind = "openai_compatible";
        screen.BaseUrl = " https://models.example.test/v1 ";
        await screen.AddCommand.ExecuteAsync(null);

        var sent = Server.Sent("POST", connections).Single();
        sent.Should().Contain("\"name\":\"models.example.test\"").And.Contain("\"baseUrl\":\"https://models.example.test/v1\"");
    }

    [Fact]
    public async Task A_refused_connection_says_why_and_keeps_the_form_open()
    {
        SignedIn();
        Server.Answer("GET", connections, Array.Empty<ModelConnectionSummary>());
        Server.Refuse("POST", connections, HttpStatusCode.BadRequest, "Enter the address of the server, starting with http:// or https://.");
        var screen = Make<ModelConnectionsViewModel>();
        await screen.Start();
        screen.StartAddingCommand.Execute(null);
        screen.Kind = "openai_compatible";

        await screen.AddCommand.ExecuteAsync(null);

        screen.AddProblem.Should().Be("Enter the address of the server, starting with http:// or https://.");
        screen.Adding.Should().BeTrue();
        screen.AddingBusy.Should().BeFalse();
        screen.ShowEmptyToAdd.Should().BeFalse("while the form is open it is the only thing to do");
        screen.ShowEmptyReadOnly.Should().BeFalse();
    }

    [Fact]
    public async Task Loading_an_empty_list_explains_how_to_add_the_first_connection()
    {
        SignedIn();
        Server.Answer("GET", connections, Array.Empty<ModelConnectionSummary>());
        var loading = Server.Hold("GET", connections);
        var screen = Make<ModelConnectionsViewModel>();

        var started = screen.Start();
        await WaitUntil(() => screen.ShowLoading);
        ModelConnectionsViewModel.LoadingNote.Should().Be("Loading the model connections.");
        loading.SetResult();
        await started;

        screen.ShowLoading.Should().BeFalse();
        screen.ShowEmptyToAdd.Should().BeTrue();
        ModelConnectionsViewModel.EmptyTitle.Should().Be("No model connection yet");
        ModelConnectionsViewModel.EmptyCause.Should().Be("Bots think with a model you bring. Add a connection, then choose it in a bot's Model section.");
        ModelConnectionsViewModel.AddLabel.Should().Be("Add a model connection");
        screen.ShowAddButton.Should().BeFalse("the empty state holds the one way to add the first");
    }

    [Fact]
    public async Task A_connection_list_error_offers_a_retry()
    {
        SignedIn();
        Server.Answer("GET", connections, HttpStatusCode.ServiceUnavailable);
        var screen = Make<ModelConnectionsViewModel>();

        await screen.Start();

        screen.ShowFailed.Should().BeTrue();
        screen.LoadProblem.Should().NotBeNullOrEmpty();
        ModelConnectionsViewModel.LoadFailedTitle.Should().Be("Drover could not load the model connections");

        Server.Answer("GET", connections, new[] { connection });
        await screen.LoadCommand.ExecuteAsync(null);
        screen.ShowList.Should().BeTrue();
        screen.ShowFailed.Should().BeFalse();
    }

    [Fact]
    public async Task A_member_can_view_models_and_cannot_change_the_connection()
    {
        var entry = new ModelCatalogResponse("model-a", "Example model",
            new(null, null, true, true, false, false, [], false, false),
            new(null, null, null, "usd"), null, [], "available", DateTimeOffset.UnixEpoch);
        SignedInAs("member");
        Server.Answer("GET", connections, new[] { connection })
            .Answer("GET", $"{connectionPath}/models", new[] { entry });
        var screen = Make<ModelConnectionsViewModel>();
        await screen.Start();
        var card = screen.Cards.Single();

        await card.ViewCatalogCommand.ExecuteAsync(null);

        card.Catalog.Should().Equal(new CatalogLine("Example model", "model-a", null));
        card.CatalogLabel.Should().Be("Models from Sample provider");
        card.ShowCatalog.Should().BeTrue();
        screen.CanEdit.Should().BeFalse();
        card.CanEdit.Should().BeFalse("a member has no Test, Replace key, or Refresh models");
        card.ShowRefresh.Should().BeFalse();
        screen.ShowAddButton.Should().BeFalse();
    }

    [Fact]
    public async Task A_member_of_a_space_with_no_connection_is_told_who_can_add_one()
    {
        SignedInAs("member");
        Server.Answer("GET", connections, Array.Empty<ModelConnectionSummary>());
        var screen = Make<ModelConnectionsViewModel>();

        await screen.Start();

        screen.ShowEmptyReadOnly.Should().BeTrue();
        screen.ShowEmptyToAdd.Should().BeFalse();
        ModelConnectionsViewModel.EmptyReadOnlyNote.Should().Be("This space has no model connections yet. An owner or admin of the space can add one.");
    }

    [Fact]
    public async Task Each_connection_says_in_words_whether_it_works_and_what_is_known_of_it()
    {
        SignedIn();
        var failed = connection with { Id = Guid.Parse("0196f1a0-0000-7000-8000-000000000005"), Name = "Quiet provider", Status = "failed", Kind = "openai_compatible", HasKey = false };
        var untested = connection with { Id = Guid.Parse("0196f1a0-0000-7000-8000-000000000006"), Name = "New provider", Status = "untested" };
        Server.Answer("GET", connections, new[] { connection, failed, untested });
        var screen = Make<ModelConnectionsViewModel>();

        await screen.Start();

        screen.Cards.Select(card => card.StatusWords).Should().Equal("Working", "Not working", "Not tested");
        screen.Cards.Select(card => card.StatusKind).Should().Equal(BadgeKind.Ok, BadgeKind.Danger, BadgeKind.Neutral);
        screen.Cards[0].Facts.Should().Be("venice · key stored · never checked");
        screen.Cards[1].Facts.Should().Be("compatible endpoint · no key · never checked");
        screen.ShowAddButton.Should().BeTrue();
    }

    [Fact]
    public async Task A_catalog_that_lists_nothing_and_a_model_that_is_not_available_say_so()
    {
        SignedIn();
        var gone = new ModelCatalogResponse("model-b", "Retired model",
            new(null, null, true, true, false, false, [], false, false),
            new(null, null, null, "usd"), null, [], "deprecated", DateTimeOffset.UnixEpoch);
        Server.Answer("GET", connections, new[] { connection }).Answer("GET", $"{connectionPath}/models", Array.Empty<ModelCatalogResponse>());
        var screen = Make<ModelConnectionsViewModel>();
        await screen.Start();
        var card = screen.Cards.Single();

        await card.ViewCatalogCommand.ExecuteAsync(null);

        card.ShowNoModels.Should().BeTrue();
        ModelConnectionCard.NoModelsNote.Should().Be("This connection has no listed models. You can enter a model ID in the bot’s Model section.");
        card.ShowRefresh.Should().BeTrue("an owner can refresh a list that is shown");

        Server.Answer("POST", $"{connectionPath}/models/refresh", new ModelRefreshResponse(true, null, null, [gone]));
        await card.RefreshModelsCommand.ExecuteAsync(null);

        card.Catalog.Should().Equal(new CatalogLine("Retired model", "model-b", "(deprecated)"));
        screen.Notice.Should().Be("Model list refreshed.");
    }

    [Fact]
    public async Task A_request_that_fails_is_said_on_its_connection_and_nothing_else_is_changed()
    {
        SignedIn();
        Server.Answer("GET", connections, new[] { connection });
        Server.Refuse("POST", $"{connectionPath}/test", HttpStatusCode.BadRequest, "Sample outage.");
        var screen = Make<ModelConnectionsViewModel>();
        await screen.Start();

        await screen.Cards.Single().TestCommand.ExecuteAsync(null);

        screen.Cards.Single().Problem.Should().Be("Sample outage.");
        screen.Notice.Should().BeNull();
        screen.ChangePending.Should().BeFalse();
    }

    #endregion

    #region Private Methods

    #endregion
}
