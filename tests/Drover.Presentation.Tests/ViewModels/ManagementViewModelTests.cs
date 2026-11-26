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

    private readonly SpaceVoice voice;

    private readonly Navigator navigator = new();

    #endregion

    #region Constructors

    public ManagementViewModelTests()
    {
        session = new(server.CreateApi(), new ShareInbox());
        voice = new(server.CreateApi(), session, NullLogger<SpaceVoice>.Instance);
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
        server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection]);
        server.Answer("GET", $"{space}/bots", Array.Empty<BotSummary>());
        var model = new BotListViewModel(server.CreateApi(), session, navigator, new ToastService(TimeProvider.System), NullLogger<BotListViewModel>.Instance);
        await model.Open();

        model.Create(parentId);

        navigator.Current.Should().Be(Addresses.NewBot(parentId));
        server.Requests.Should().NotContain(request => request.Method == "POST");
    }

    [Fact]
    public async Task Replacing_a_model_key_clears_the_input_and_preserves_other_connections()
    {
        server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection, Connection with { Id = parentId, Name = "Sample second" }]);
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
    public async Task A_change_to_a_connection_has_the_voice_options_read_again()
    {
        server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection]);
        server.Answer("GET", $"{space}/voice/options", new VoiceOptionsResponse(new([], null), new([], null), new([], null), new(null, null, null)));
        server.Answer("POST", $"{space}/model-connections/{Connection.Id}/test", new Drover.Contracts.Models.ConnectionTestResponse(true, "Sample connected.", null, Connection));
        var model = Models();
        await model.Open();
        await voice.Load();
        await voice.Load();
        server.Requests.Count(request => request.Path.EndsWith("/voice/options", StringComparison.Ordinal)).Should().Be(1);

        await model.Test(Connection);
        await voice.Load();

        server.Requests.Count(request => request.Path.EndsWith("/voice/options", StringComparison.Ordinal)).Should().Be(2);
    }

    [Fact]
    public async Task Replacing_a_key_closes_on_success_and_keeps_the_reason_on_refusal()
    {
        server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection]);
        server.Answer("PUT", $"{space}/model-connections/{connectionId}/key", Connection with { HasKey = true });
        var model = Models();
        await model.Open();
        model.StartReplacing(Connection);
        model.NewKey = " sample-new-key ";

        await model.ReplaceKey(Connection);

        model.Replacing.Should().BeNull();
        model.NewKey.Should().BeEmpty();

        server.Refuse("PUT", $"{space}/model-connections/{connectionId}/key", HttpStatusCode.BadRequest, "Sample key refused.");
        model.StartReplacing(Connection);
        model.NewKey = "another-key";

        await model.ReplaceKey(Connection);

        model.Replacing.Should().Be(connectionId);
        model.NewKey.Should().Be("another-key");
        model.KeyProblem.Should().Be("Sample key refused.");
        model.Problems.Should().NotContainKey(connectionId, "a dialog refusal is not a card error");
        model.ChangePending.Should().BeFalse();
    }

    [Fact]
    public async Task Cancelling_an_add_or_a_key_replacement_clears_every_typed_secret()
    {
        server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection]);
        var model = Models();
        await model.Open();

        model.StartAdding();
        model.ApiKey = "sample-add-key";
        model.CancelAdding();
        model.Adding.Should().BeFalse();
        model.ApiKey.Should().BeEmpty();

        model.StartReplacing(Connection);
        model.NewKey = "sample-replacement-key";
        model.CancelReplacing();
        model.Replacing.Should().BeNull();
        model.NewKey.Should().BeEmpty();
    }

    [Fact]
    public async Task A_member_is_offered_no_owner_actions_on_a_model_connection()
    {
        server.Answer("GET", "/api/session", Samples.Session("member"));
        server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection]);
        var memberSession = new SessionState(server.CreateApi(), new ShareInbox());
        var asMember = new ModelConnectionsViewModel(
            server.CreateApi(),
            memberSession,
            new(server.CreateApi(), memberSession, NullLogger<SpaceVoice>.Instance),
            NullLogger<ModelConnectionsViewModel>.Instance);
        await asMember.Open();

        asMember.CanEdit.Should().BeFalse("a member sees cards without owner actions and no add action");
        asMember.Connections.Should().ContainSingle();
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
        server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection]);
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
    public async Task Only_an_admin_can_edit_what_a_model_can_do()
    {
        server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection]);
        var model = Models();
        await model.Open();

        model.CanEdit.Should().BeTrue();

        server.Answer("GET", "/api/session", Samples.Session("member"));
        var memberSession = new SessionState(server.CreateApi(), new ShareInbox());
        var asMember = new ModelConnectionsViewModel(
            server.CreateApi(),
            memberSession,
            new(server.CreateApi(), memberSession, NullLogger<SpaceVoice>.Instance),
            NullLogger<ModelConnectionsViewModel>.Instance);
        await asMember.Open();

        asMember.CanEdit.Should().BeFalse("the server decides, and a member is not offered the menu entry");
    }

    [Fact]
    public async Task The_sheet_opens_filled_with_what_is_known_and_says_where_each_value_came_from()
    {
        var entry = Entry("meta/plain", vision: false) with
        {
            Capabilities = new ModelCapabilitiesResponse(8192, null, true, true, true, false, [], false, false, VisionSetByOperator: true),
            Price = new ModelPriceResponse(0.5m, null, null, "USD"),
            OperatorSet = ["vision", "inputPerMillion"],
        };
        server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection]);
        var model = Models();
        await model.Open();

        model.OpenSettings(Connection, entry);

        model.EditingSettings.Should().BeTrue();
        model.Settings.ContextSize.Should().Be("8192");
        model.Settings.InputPrice.Should().Be("0.5");
        model.Settings.Caption("contextSize").Should().Be("From the provider");
        model.Settings.Caption("vision").Should().Be("Set here");
        model.Settings.Caption("inputPerMillion").Should().Be("Set here");
        model.Settings.Caption("outputPerMillion").Should().BeNull("nothing is known, so nothing is said");
        model.Settings.Caption("toolCalling").Should().BeNull("every model is assumed to call tools, which is not the provider's word");
        model.Settings.OffersUseProvidersValues.Should().BeTrue();
    }

    [Fact]
    public async Task Effort_levels_show_only_while_the_model_thinks_before_answering()
    {
        server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection]);
        var model = Models();
        await model.Open();
        model.OpenSettings(Connection, Entry("plain", vision: false));

        model.Settings.ShowsEfforts.Should().BeFalse();

        model.Settings.Reasoning = true;

        model.Settings.ShowsEfforts.Should().BeTrue();
    }

    [Fact]
    public async Task Saving_sends_only_what_was_changed_and_what_was_already_set_and_the_answer_replaces_its_row()
    {
        var entry = Entry("meta/plain", vision: false) with
        {
            Capabilities = new ModelCapabilitiesResponse(8192, null, true, true, true, false, [], false, false),
            OperatorSet = [],
        };
        var answer = entry with
        {
            Capabilities = entry.Capabilities with { MaxOutputSize = 4096 },
            OperatorSet = ["maxOutputSize"],
        };
        server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection]);
        server.Answer("GET", $"{space}/model-connections/{connectionId}/models", (ModelCatalogResponse[])[entry, Entry("other", vision: false)]);
        server.Answer("PUT", $"{space}/model-connections/{connectionId}/model-settings", answer);
        var model = Models();
        await model.Open();
        await model.ShowCatalog(Connection);
        model.OpenSettings(Connection, entry);
        model.Settings.MaxOutputSize = "4096";
        model.Settings.OutputPrice = "1.25";

        await model.SaveSettings();

        var sent = JsonSerializer.Deserialize<SetModelSettingsRequest>(
            server.Sent("PUT", $"{space}/model-connections/{connectionId}/model-settings").Single(),
            JsonSerializerOptions.Web)!;
        sent.Model.Should().Be("meta/plain");
        sent.Capabilities.Should().Be(new ModelAbilitiesRequest(MaxOutputSize: 4096));
        sent.Pricing.Should().Be(new ModelPricingRequest(OutputPerMillion: 1.25m, Currency: "USD"));
        model.EditingSettings.Should().BeFalse();
        model.Catalog!.Select(row => (row.Model, row.Capabilities.MaxOutputSize)).Should().Equal(("meta/plain", 4096), ("other", null));
        model.Notice.Should().Contain("was updated");
    }

    [Theory]
    [InlineData("ContextSize", "0", "Enter a size of 1 token or more.")]
    [InlineData("MaxOutputSize", "ten", "Enter a size of 1 token or more.")]
    [InlineData("InputPrice", "-1", "Enter a price of zero or more per million tokens.")]
    [InlineData("OutputPrice", "0.1234567", "Enter a price of zero or more per million tokens.")]
    [InlineData("Currency", "US", "Enter a three letter currency code, such as USD.")]
    public async Task A_value_out_of_range_stays_on_the_form_with_its_sentence_and_nothing_is_sent(string field, string typed, string sentence)
    {
        server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection]);
        var model = Models();
        await model.Open();
        model.OpenSettings(Connection, Entry("plain", vision: false));
        typeof(ModelSettingsForm).GetProperty(field)!.SetValue(model.Settings, typed);

        await model.SaveSettings();

        model.Settings.Problems[field].Should().Be(sentence);
        model.EditingSettings.Should().BeTrue();
        server.Sent("PUT", $"{space}/model-connections/{connectionId}/model-settings").Should().BeEmpty();
    }

    [Fact]
    public async Task A_size_above_ten_million_is_accepted_on_the_form()
    {
        server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection]);
        var model = Models();
        await model.Open();
        model.OpenSettings(Connection, Entry("plain", vision: false));
        model.Settings.ContextSize = "20000000";

        model.Settings.Check().Should().BeTrue();
        model.Settings.Request().Capabilities!.ContextSize.Should().Be(20_000_000);
    }

    [Fact]
    public async Task A_refused_save_keeps_the_sheet_open_with_the_servers_sentence()
    {
        server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection]);
        server.Refuse("PUT", $"{space}/model-connections/{connectionId}/model-settings", HttpStatusCode.BadRequest, "Sample model is refused.");
        var model = Models();
        await model.Open();
        model.OpenSettings(Connection, Entry("plain", vision: false));
        model.Settings.Vision = true;

        await model.SaveSettings();

        model.EditingSettings.Should().BeTrue();
        model.SettingsProblem.Should().Be("Sample model is refused.");
        model.ChangePending.Should().BeFalse();
    }

    [Fact]
    public async Task Using_the_providers_values_asks_first_then_clears_and_reads_the_catalog_again()
    {
        var entry = Entry("plain", vision: true) with { OperatorSet = ["vision"] };
        server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection]);
        server.Answer("GET", $"{space}/model-connections/{connectionId}/models", (ModelCatalogResponse[])[Entry("plain", vision: false)]);
        server.Answer("DELETE", $"{space}/model-connections/{connectionId}/model-settings", HttpStatusCode.NoContent);
        var model = Models();
        await model.Open();
        model.OpenSettings(Connection, entry);

        model.AskUseProvidersValues();
        model.ConfirmingClear.Should().BeTrue();
        model.KeepSettings();
        model.ConfirmingClear.Should().BeFalse();
        server.Sent("DELETE", $"{space}/model-connections/{connectionId}/model-settings").Should().BeEmpty("keeping them sends nothing");

        model.AskUseProvidersValues();
        await model.UseProvidersValues();

        server.Sent("DELETE", $"{space}/model-connections/{connectionId}/model-settings").Should().HaveCount(1);
        server.Addresses.Should().Contain($"DELETE {space}/model-connections/{connectionId}/model-settings?model=plain");
        model.Catalog!.Single().Capabilities.Vision.Should().BeFalse();
        (model.EditingSettings, model.ConfirmingClear).Should().Be((false, false));
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
        server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[Connection]);
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

    private ModelConnectionsViewModel Models() => new(server.CreateApi(), session, voice, NullLogger<ModelConnectionsViewModel>.Instance);

    #endregion
}
