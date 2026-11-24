namespace Drover.Presentation.Tests.ViewModels;

/// <summary>The setup flow against a sample server, including refusals, retries, and connection reuse.</summary>
public sealed class SetupViewModelTests : IDisposable
{
    #region Private Fields

    private static readonly Guid connectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000003");

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000004");

    private static readonly Guid otherConnectionId = Guid.Parse("0196f1a0-0000-7000-8000-000000000005");

    private static readonly string space = $"/api/spaces/{Samples.Session().Spaces[0].Id}";

    private readonly FakeServer server = new();

    private readonly Navigator navigator = new();

    private readonly SessionState session;

    #endregion

    #region Constructors

    public SetupViewModelTests()
    {
        session = new(server.CreateApi(), new ShareInbox());
        server.Answer("GET", "/api/session", Samples.Session());
    }

    #endregion

    #region Properties

    private static Drover.Contracts.Models.ModelConnectionSummary Connection =>
        new(connectionId, "Sample connection", "venice", null, true, "connected", null, [], null, null);

    private static Drover.Contracts.Models.ModelConnectionSummary OtherConnection =>
        new(otherConnectionId, "models.example.test", "openai_compatible", "https://models.example.test/v1", true, "connected", null, [], null, null);

    private static BotSummary Lead => new(botId, "Sample lead", "sample-lead", "Sample instructions", connectionId, "sample-model", "active");

    #endregion

    #region Public Methods

    public void Dispose() => server.Dispose();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_account_sends_a_setup_code_only_when_required(bool requiresCode)
    {
        server.Answer("GET", "/api/setup", new SetupStatus(requiresCode));
        server.Answer("POST", "/api/setup", Samples.Session(), HttpStatusCode.Created);
        var model = Account();
        await model.Open();
        model.DisplayName = " Sample owner ";
        model.Email = " sample@example.test ";
        model.Password = "Sample password with spaces";
        model.SetupCode = " SAMPLE-CODE ";

        await model.Submit();

        var sent = JsonSerializer.Deserialize<SetupRequest>(server.Sent("POST", "/api/setup").Single(), JsonSerializerOptions.Web)!;
        sent.Should().Be(new SetupRequest("sample@example.test", "Sample owner", "Sample password with spaces", requiresCode ? "SAMPLE-CODE" : null));
        session.Current.Should().BeEquivalentTo(Samples.Session());
        navigator.Current.Should().Be(Addresses.SetupModel);
        model.Busy.Should().BeFalse();
    }

    [Fact]
    public async Task A_server_already_set_up_goes_to_sign_in()
    {
        await Account().Open();

        navigator.Current.Should().Be(Addresses.SignIn);
    }

    [Fact]
    public async Task An_unreachable_server_can_be_checked_again()
    {
        server.Unreachable = true;
        var model = Account();
        await model.Open();
        model.Problem.Should().Be(Failures.Unreachable);
        server.Unreachable = false;
        server.Answer("GET", "/api/setup", new SetupStatus(true));

        await model.CheckServer();

        model.Problem.Should().BeNull();
        model.Status.Should().NotBeNull();
    }

    [Fact]
    public async Task A_rejected_setup_code_keeps_the_account_form_and_the_servers_sentence()
    {
        server.Answer("GET", "/api/setup", new SetupStatus(true));
        server.Refuse("POST", "/api/setup", HttpStatusCode.Forbidden, "Sample setup code was refused.");
        var model = Account();
        await model.Open();
        model.DisplayName = "Sample owner";

        await model.Submit();

        model.Problem.Should().Be("Sample setup code was refused.");
        model.DisplayName.Should().Be("Sample owner");
        model.Busy.Should().BeFalse();
    }

    [Fact]
    public async Task Retesting_a_corrected_key_updates_the_existing_connection()
    {
        var path = $"{space}/model-connections";
        server.Answer("GET", path, Array.Empty<Drover.Contracts.Models.ModelConnectionSummary>());
        server.Answer("POST", path, Connection, HttpStatusCode.Created);
        server.Answer("PUT", $"{path}/{connectionId}/key", Connection);
        server.Answer("POST", $"{path}/{connectionId}/test", new Drover.Contracts.Models.ConnectionTestResponse(false, "Sample rejected key.", "unauthorized", Connection));
        var model = ConnectionStep();
        await model.Open();
        model.ApiKey = "sample-initial-key";
        await model.Test();
        model.Passed.Should().BeFalse();
        model.ApiKey = "sample-corrected-key";
        server.Answer("POST", $"{path}/{connectionId}/test", new Drover.Contracts.Models.ConnectionTestResponse(true, "Sample connected.", null, Connection));

        await model.Test();

        server.Sent("POST", path).Should().ContainSingle();
        server.Sent("PUT", $"{path}/{connectionId}/key").Single().Should().Contain("sample-corrected-key");
        model.Passed.Should().BeTrue();
        model.Outcome.Should().Be("Sample connected.");
        model.Continue();
        navigator.Current.Should().Be(Addresses.SetupBot);
    }

    [Fact]
    public async Task Choosing_another_provider_clears_the_previous_test()
    {
        var path = $"{space}/model-connections";
        server.Answer("GET", path, Array.Empty<Drover.Contracts.Models.ModelConnectionSummary>());
        server.Answer("POST", path, Connection, HttpStatusCode.Created);
        server.Answer("POST", $"{path}/{connectionId}/test", new Drover.Contracts.Models.ConnectionTestResponse(true, "Sample connected.", null, Connection));
        var model = ConnectionStep();
        await model.Open();
        await model.Test();

        model.ChooseProvider("openai_compatible");

        model.Passed.Should().BeFalse();
        model.Outcome.Should().BeNull();
    }

    [Fact]
    public async Task Retesting_with_the_same_values_creates_no_second_connection()
    {
        var path = $"{space}/model-connections";
        server.Answer("GET", path, Array.Empty<Drover.Contracts.Models.ModelConnectionSummary>());
        server.Answer("POST", path, Connection, HttpStatusCode.Created);
        server.Answer("POST", $"{path}/{connectionId}/test", new Drover.Contracts.Models.ConnectionTestResponse(true, "Sample connected.", null, Connection));
        var model = ConnectionStep();
        await model.Open();
        model.ApiKey = "sample-key";

        await model.Test();
        await model.Test();

        server.Sent("POST", path).Should().ContainSingle("the step keeps the connection its first test made");
        server.Sent("PUT", $"{path}/{connectionId}/key").Should().BeEmpty();
        model.Passed.Should().BeTrue();
    }

    [Fact]
    public async Task Switching_the_provider_and_back_reuses_the_connection_that_matches()
    {
        var path = $"{space}/model-connections";
        server.Answer("POST", $"{path}/{connectionId}/test", new Drover.Contracts.Models.ConnectionTestResponse(true, "Sample connected.", null, Connection));
        server.Answer("POST", $"{path}/{otherConnectionId}/test", new Drover.Contracts.Models.ConnectionTestResponse(true, "Sample connected.", null, OtherConnection));
        server.Answer("PUT", $"{path}/{connectionId}/key", Connection);
        server.Answer("GET", path, Array.Empty<Drover.Contracts.Models.ModelConnectionSummary>());
        server.Answer("POST", path, Connection, HttpStatusCode.Created);
        var model = ConnectionStep();
        await model.Open();
        model.ApiKey = "sample-key";
        await model.Test();

        model.ChooseProvider("openai_compatible");
        model.BaseUrl = "https://models.example.test/v1";
        model.ApiKey = "sample-other-key";
        server.Answer("GET", path, new[] { Connection });
        server.Answer("POST", path, OtherConnection, HttpStatusCode.Created);
        await model.Test();

        model.ChooseProvider("venice");
        model.ApiKey = "sample-key";
        server.Answer("GET", path, new[] { Connection, OtherConnection });
        await model.Test();

        server.Sent("POST", path).Should().HaveCount(2, "one connection per provider and address chosen, never a second for the same pair");
        model.Connection.Should().BeEquivalentTo(Connection);
        model.Passed.Should().BeTrue();
    }

    [Fact]
    public async Task Reopening_the_step_reuses_the_connection_it_made_and_stores_a_retyped_key()
    {
        var path = $"{space}/model-connections";
        server.Answer("GET", path, Array.Empty<Drover.Contracts.Models.ModelConnectionSummary>());
        server.Answer("POST", path, Connection, HttpStatusCode.Created);
        server.Answer("POST", $"{path}/{connectionId}/test", new Drover.Contracts.Models.ConnectionTestResponse(true, "Sample connected.", null, Connection));
        var first = ConnectionStep();
        await first.Open();
        first.ApiKey = "sample-key";
        await first.Test();
        first.Continue();

        var again = ConnectionStep();
        await again.Open();
        server.Answer("GET", path, new[] { Connection });
        server.Answer("PUT", $"{path}/{connectionId}/key", Connection);
        again.ApiKey = "sample-key";

        await again.Test();

        server.Sent("POST", path).Should().ContainSingle("the step made the connection once, across both visits");
        server.Sent("PUT", $"{path}/{connectionId}/key").Single().Should().Contain("sample-key");
        again.Connection.Should().BeEquivalentTo(Connection);
        again.Passed.Should().BeTrue();
    }

    [Fact]
    public async Task Meeting_an_existing_bot_does_not_create_another_or_refresh_models()
    {
        server.Answer("GET", $"{space}/bots", new[] { Lead });
        var model = MeetStep();

        await model.Open();

        model.Bot.Should().BeEquivalentTo(Lead);
        model.Loaded.Should().BeTrue();
        server.Requests.Should().NotContain(request => request.Method == "POST");
    }

    [Fact]
    public async Task Meet_your_first_bot_lists_models_from_the_working_connection()
    {
        var failedId = Guid.Parse("0196f1a0-0000-7000-8000-00000000000c");
        var workingId = Guid.Parse("0196f1a0-0000-7000-8000-00000000000d");
        static ModelCatalogResponse Catalog(string model) =>
            new(model, model, new(null, null, false, true, false, false, [], false, false), new(null, null, null, "usd"), null, [], "available", DateTimeOffset.UnixEpoch);
        server.Answer("GET", $"{space}/bots", Array.Empty<BotSummary>());
        server.Answer("GET", $"{space}/model-connections", (ModelConnectionSummary[])[
            new(failedId, "Just failed", "venice", null, true, "failed", null, [], null, DateTimeOffset.UtcNow.AddMinutes(-1)),
            new(workingId, "Working", "venice", null, true, "ready", null, [], null, DateTimeOffset.UtcNow.AddDays(-1))]);
        server.Answer("POST", $"{space}/model-connections/{failedId}/models/refresh", new ModelRefreshResponse(true, null, null, [Catalog("sample-failed")]));
        server.Answer("POST", $"{space}/model-connections/{workingId}/models/refresh", new ModelRefreshResponse(true, null, null, [Catalog("sample-working")]));
        var model = MeetStep();

        await model.Open();

        model.Connection!.Id.Should().Be(workingId, "a connection whose test just failed is not chosen over one that works");
        model.Models.Select(entry => entry.Model).Should().Equal("sample-working");
    }

    [Fact]
    public async Task A_provider_with_no_catalog_allows_a_typed_model_and_creates_the_named_lead()
    {
        server.Answer("GET", $"{space}/bots", Array.Empty<BotSummary>());
        server.Answer("GET", $"{space}/model-connections", new[] { Connection });
        server.Answer("POST", $"{space}/model-connections/{connectionId}/models/refresh", new Drover.Contracts.Models.ModelRefreshResponse(true, null, null, []));
        server.Answer("POST", $"{space}/bots", Lead, HttpStatusCode.Created);
        var model = MeetStep();
        await model.Open();
        model.Models.Should().BeEmpty();
        model.Name = " Sample lead ";
        model.Model = " sample-model ";

        await model.Create();

        var sent = JsonSerializer.Deserialize<CreateBotRequest>(server.Sent("POST", $"{space}/bots").Single(), JsonSerializerOptions.Web)!;
        sent.Name.Should().Be("Sample lead");
        sent.Model.Should().Be("sample-model");
        sent.Instructions.Should().StartWith("You are Sample lead, the lead of this person's crew of bots");
        sent.ParentBotId.Should().BeNull();
        model.Bot.Should().BeEquivalentTo(Lead);
    }

    [Fact]
    public async Task With_no_connection_the_first_bot_step_preserves_an_honest_empty_state()
    {
        server.Answer("GET", $"{space}/bots", Array.Empty<BotSummary>());
        server.Answer("GET", $"{space}/model-connections", Array.Empty<Drover.Contracts.Models.ModelConnectionSummary>());
        var model = MeetStep();

        await model.Open();

        model.Loaded.Should().BeTrue();
        model.Connection.Should().BeNull();
        model.Bot.Should().BeNull();
        model.LoadProblem.Should().BeNull();
    }

    #endregion

    #region Private Methods

    private CreateAccountViewModel Account() => new(server.CreateApi(), session, navigator, NullLogger<CreateAccountViewModel>.Instance);

    private ConnectModelViewModel ConnectionStep() => new(server.CreateApi(), session, navigator, NullLogger<ConnectModelViewModel>.Instance);

    private MeetBotViewModel MeetStep() => new(server.CreateApi(), session, NullLogger<MeetBotViewModel>.Instance);

    #endregion
}
