namespace Drover.Presentation.Tests.ViewModels;

/// <summary>The Add a connection form of the Models screen with the named providers the server offers.</summary>
public sealed class ModelConnectionsViewModelTests : IDisposable
{
    #region Private Fields

    private const string PresetsPath = "/api/model-provider-presets";

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
        server.Answer("GET", PresetsPath, SampleProviders.All);
        var model = Models();

        await model.Open();

        model.Named.Options.Select(option => option.Label).Should().Equal(
            "Venice", "Sample hosted", "Sample keyless", "Sample optional", "Another compatible server");
    }

    [Fact]
    public async Task A_named_provider_hides_the_address_until_asked()
    {
        server.Answer("GET", PresetsPath, SampleProviders.All);
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
        server.Answer("GET", PresetsPath, SampleProviders.All);
        var model = Models();
        await model.Open();

        model.ChooseProvider("sample-keyless");

        model.Named.ShowsKey.Should().BeFalse();
        model.Named.ReachNote.Should().Be("Drover's server must be able to reach this address.");
    }

    [Fact]
    public async Task A_failed_read_of_the_list_still_offers_another_compatible_server()
    {
        server.Answer("GET", PresetsPath, HttpStatusCode.InternalServerError);
        var model = Models();

        await model.Open();

        model.Named.State.Should().Be(NamedProvidersState.Failed);
        model.Named.Options.Select(option => option.Label).Should().Equal("Venice", "Another compatible server");
        model.Connections.Should().NotBeNull("the connections load without the list");

        server.Answer("GET", PresetsPath, SampleProviders.All);
        await model.RetryProviders();

        model.Named.State.Should().Be(NamedProvidersState.Loaded);
    }

    [Fact]
    public async Task A_connection_added_from_a_named_provider_is_compatible_and_takes_the_providers_name()
    {
        var path = $"{space}/model-connections";
        var created = new ModelConnectionSummary(
            Guid.NewGuid(), "Sample hosted", "openai_compatible", "https://hosted.example.test/v1", true, "pending", null, [], null, null);
        server.Answer("GET", PresetsPath, SampleProviders.All);
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

    #endregion

    #region Private Methods

    private ModelConnectionsViewModel Models() =>
        new(server.CreateApi(), session, new(server.CreateApi(), session, NullLogger<SpaceVoice>.Instance), NullLogger<ModelConnectionsViewModel>.Instance);

    #endregion
}
