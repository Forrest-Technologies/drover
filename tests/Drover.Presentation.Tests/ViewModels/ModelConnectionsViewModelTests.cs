namespace Drover.Presentation.Tests.ViewModels;

/// <summary>The Add a connection form of the Models screen with the named providers the server offers.</summary>
public sealed class ModelConnectionsViewModelTests : IDisposable
{
    #region Private Fields

    private const string PresetsPath = "/api/model-provider-kinds";

    private static readonly string space = $"/api/spaces/{Samples.Session().Spaces[0].Id}";

    private readonly FakeServer server = new();

    private readonly SessionState session;

    #endregion

    #region Constructors

    public ModelConnectionsViewModelTests()
    {
        session = new(server.CreateApi(), new ShareInbox());
        server.Answer("GET", "/api/session", Samples.Session());
        server.Answer("GET", $"{space}/model-connections", Array.Empty<ModelConnectionSummary>());
    }

    #endregion

    #region Public Methods

    public void Dispose() => server.Dispose();

    [Fact]
    public async Task The_provider_choice_holds_the_servers_named_providers()
    {
        server.Answer("GET", PresetsPath, SampleProviders.Kinds(SampleProviders.All));
        var model = Models();

        await model.Open();

        model.Named.Options.Select(option => option.Label).Should().Equal(
            "Venice", "Sample hosted", "Sample keyless", "Sample optional", "Another compatible server");
    }

    [Fact]
    public async Task A_named_provider_hides_the_address_until_asked()
    {
        server.Answer("GET", PresetsPath, SampleProviders.Kinds(SampleProviders.All));
        var model = Models();
        await model.Open();
        model.StartAdding();

        model.ChooseProvider("sample-hosted");

        model.Kind.Should().Be("openai_compatible");
        model.BaseUrl.Should().Be("https://hosted.example.test/v1");
        model.Named.ShowsAddress.Should().BeFalse();
        model.Named.KeyLinkText.Should().Be("Get a key from Sample hosted");

        model.ChangeAddress();

        model.Named.ShowsAddress.Should().BeTrue();
        model.BaseUrl.Should().Be("https://hosted.example.test/v1");
    }

    [Fact]
    public async Task A_provider_with_no_key_shows_no_key_field()
    {
        server.Answer("GET", PresetsPath, SampleProviders.Kinds(SampleProviders.All));
        var model = Models();
        await model.Open();

        model.ChooseProvider("sample-keyless");

        model.Named.ShowsKey.Should().BeFalse();
        model.Named.ReachNote.Should().Be("Drover's server must be able to reach this address.");
    }

    [Fact]
    public async Task A_failed_read_of_the_list_draws_no_form_until_it_is_read_again()
    {
        server.Answer("GET", PresetsPath, HttpStatusCode.InternalServerError);
        var model = Models();

        await model.Open();

        model.Named.State.Should().Be(NamedProvidersState.Failed);
        model.Named.ShowsForm.Should().BeFalse();
        model.Connections.Should().NotBeNull("the connections load without the list");

        server.Answer("GET", PresetsPath, SampleProviders.Kinds(SampleProviders.All));
        await model.RetryProviders();

        model.Named.State.Should().Be(NamedProvidersState.Loaded);
    }

    [Fact]
    public async Task A_connection_of_a_kind_the_server_no_longer_offers_is_shown_with_its_notice()
    {
        var gone = new ModelConnectionSummary(
            Guid.NewGuid(), "Sample old", "sample-gone", null, true, "ready", null, [], null, null);
        server.Answer("GET", $"{space}/model-connections", new[] { gone });
        server.Answer("GET", PresetsPath, SampleProviders.Kinds());
        var model = Models();

        await model.Open();

        model.IsOff(gone).Should().BeTrue();
        model.StatusOf(gone).Should().Be((BadgeKind.Neutral, "Off"));
        model.OffWords(gone).Should().Be("The operator of this server switched sample-gone off.");
    }

    [Fact]
    public async Task A_server_that_offers_no_kind_draws_a_state_in_place_of_the_form()
    {
        server.Answer("GET", PresetsPath, Array.Empty<ModelProviderKindResponse>());
        var model = Models();

        await model.Open();
        model.StartAdding();
        await model.Add();

        model.Named.ShowsForm.Should().BeFalse();
        model.Named.ShowsState.Should().BeTrue();
        model.Named.StateTitle.Should().Be("This server offers no model provider");
        server.Sent("POST", $"{space}/model-connections").Should().BeEmpty();
    }

    [Fact]
    public async Task A_connection_of_an_offered_kind_is_not_off_and_nothing_is_said_while_the_list_is_unread()
    {
        var kept = new ModelConnectionSummary(
            Guid.NewGuid(), "Sample kept", "venice", null, true, "ready", null, [], null, null);
        server.Answer("GET", $"{space}/model-connections", new[] { kept });
        server.Answer("GET", PresetsPath, HttpStatusCode.InternalServerError);
        var model = Models();

        await model.Open();

        model.IsOff(kept).Should().BeFalse("the list could not be read, so nothing is known");
        server.Answer("GET", PresetsPath, SampleProviders.Kinds());
        await model.RetryProviders();
        model.IsOff(kept).Should().BeFalse();
        model.StatusOf(kept).Should().Be((BadgeKind.Ok, "Working"));
        model.Facts(kept).Should().StartWith("Venice · key stored");
    }

    [Fact]
    public async Task A_kind_the_client_has_never_heard_of_can_be_added()
    {
        var path = $"{space}/model-connections";
        var created = new ModelConnectionSummary(
            Guid.NewGuid(), "models.example.test", "sample-third", "https://models.example.test/v1", false, "pending", null, [], null, null);
        server.Answer("GET", PresetsPath, SampleProviders.KindsWithThird());
        server.Answer("POST", path, created, HttpStatusCode.Created);
        server.Answer("POST", $"{path}/{created.Id}/test", new ConnectionTestResponse(true, "Sample connected.", null, created));
        var model = Models();
        await model.Open();
        model.StartAdding();
        model.ChooseProvider("sample-third");
        model.BaseUrl = "https://models.example.test/v1";

        await model.Add();

        JsonSerializer.Deserialize<CreateModelConnectionRequest>(server.Sent("POST", path).Single(), JsonSerializerOptions.Web)!
            .Should().BeEquivalentTo(new CreateModelConnectionRequest(
                "sample-third", "models.example.test", "https://models.example.test/v1", null, null));
    }

    [Fact]
    public async Task A_required_field_left_empty_says_what_to_enter_and_adds_nothing()
    {
        server.Answer("GET", PresetsPath, SampleProviders.Kinds());
        var model = Models();
        await model.Open();
        model.StartAdding();

        await model.Add();

        model.AddKeyProblem.Should().Be("Enter the key.");
        server.Sent("POST", $"{space}/model-connections").Should().BeEmpty();
    }

    [Fact]
    public async Task A_connection_added_from_a_named_provider_is_compatible_and_takes_the_providers_name()
    {
        var path = $"{space}/model-connections";
        var created = new ModelConnectionSummary(
            Guid.NewGuid(), "Sample hosted", "openai_compatible", "https://hosted.example.test/v1", true, "pending", null, [], null, null);
        server.Answer("GET", PresetsPath, SampleProviders.Kinds(SampleProviders.All));
        server.Answer("POST", path, created, HttpStatusCode.Created);
        server.Answer("POST", $"{path}/{created.Id}/test", new ConnectionTestResponse(true, "Sample connected.", null, created));
        var model = Models();
        await model.Open();
        model.StartAdding();
        model.ChooseProvider("sample-hosted");
        model.ApiKey = "sample-key";

        await model.Add();

        JsonSerializer.Deserialize<CreateModelConnectionRequest>(server.Sent("POST", path).Single(), JsonSerializerOptions.Web)!
            .Should().BeEquivalentTo(new CreateModelConnectionRequest(
                "openai_compatible", "Sample hosted", "https://hosted.example.test/v1", "sample-key", null));
    }

    [Fact]
    public async Task A_connection_added_with_a_kind_of_its_own_wire_takes_its_name_its_hint_and_no_address()
    {
        var path = $"{space}/model-connections";
        var created = new ModelConnectionSummary(
            Guid.NewGuid(), "Sample own wire", "sample_own_wire", null, true, "pending", null, [], null, null);
        server.Answer("GET", PresetsPath, SampleProviders.WithOwnWire(SampleProviders.All));
        server.Answer("POST", path, created, HttpStatusCode.Created);
        server.Answer("POST", $"{path}/{created.Id}/test", new ConnectionTestResponse(true, "Sample connected.", null, created));
        var model = Models();
        await model.Open();
        model.StartAdding();
        model.ChooseProvider("sample_own_wire");
        model.BaseUrl = "https://ignored.example.test/v1";
        model.ApiKey = "sample-key";

        await model.Add();

        model.Named.KeyHint.Should().Be("Sample own wire hint.");
        model.Named.ShowsAddress.Should().BeFalse();
        JsonSerializer.Deserialize<CreateModelConnectionRequest>(server.Sent("POST", path).Single(), JsonSerializerOptions.Web)!
            .Should().BeEquivalentTo(new CreateModelConnectionRequest("sample_own_wire", "Sample own wire", null, "sample-key", null));
    }

    #endregion

    #region Private Methods

    private ModelConnectionsViewModel Models() =>
        new(server.CreateApi(), session, new(server.CreateApi(), session, NullLogger<SpaceVoice>.Instance), NullLogger<ModelConnectionsViewModel>.Instance);

    #endregion
}
