namespace Drover.Presentation.Tests.ViewModels;

/// <summary>The Connect a model step with the named providers the server offers.</summary>
public sealed class ConnectModelViewModelTests : IDisposable
{
    #region Private Fields

    private const string PresetsPath = "/api/model-provider-presets";

    private static readonly string space = $"/api/spaces/{Samples.Session().Spaces[0].Id}";

    private readonly FakeServer server = new();

    private readonly SessionState session;

    #endregion

    #region Constructors

    public ConnectModelViewModelTests()
    {
        session = new(server.CreateApi(), new ShareInbox());
        server.Answer("GET", "/api/session", Samples.Session());
    }

    #endregion

    #region Public Methods

    public void Dispose() => server.Dispose();

    [Fact]
    public async Task The_provider_choice_holds_the_servers_named_providers()
    {
        server.Answer("GET", PresetsPath, SampleProviders.All);
        var model = Step();

        await model.Open();

        model.Named.Options.Select(option => option.Label).Should().Equal(
            "Venice", "Sample hosted", "Sample keyless", "Sample optional", "Another compatible server");
        model.Named.Options.Select(option => option.Value).Should().Equal(
            "venice", "sample-hosted", "sample-keyless", "sample-optional", "openai_compatible");
        model.Named.State.Should().Be(NamedProvidersState.Loaded);
    }

    [Fact]
    public async Task A_server_with_no_named_providers_offers_the_two_choices_of_before()
    {
        server.Answer("GET", PresetsPath, Array.Empty<ModelProviderPresetResponse>());
        var model = Step();

        await model.Open();

        model.Named.Options.Select(option => option.Label).Should().Equal("Venice", "Another compatible server");
    }

    [Fact]
    public async Task A_named_provider_hides_the_address_until_asked()
    {
        server.Answer("GET", PresetsPath, SampleProviders.All);
        var model = Step();
        await model.Open();

        model.ChooseProvider("sample-hosted");

        model.Kind.Should().Be("openai_compatible");
        model.BaseUrl.Should().Be("https://hosted.example.test/v1");
        model.Named.ShowsAddress.Should().BeFalse();
        model.Named.OffersAddressChange.Should().BeTrue();
        model.Named.KeyLinkText.Should().Be("Get a key from Sample hosted");
        model.Named.KeyLinkAddress.Should().Be("https://keys.example.test/");
        model.Named.ReachNote.Should().BeNull();

        model.ChangeAddressCommand.Execute(null);

        model.Named.ShowsAddress.Should().BeTrue();
        model.Named.OffersAddressChange.Should().BeFalse();
        model.BaseUrl.Should().Be("https://hosted.example.test/v1");
    }

    [Fact]
    public async Task A_provider_with_no_key_shows_no_key_field()
    {
        server.Answer("GET", PresetsPath, SampleProviders.All);
        var model = Step();
        await model.Open();

        model.ChooseProvider("sample-keyless");

        model.Named.ShowsKey.Should().BeFalse();
        model.Named.ReachNote.Should().Be("Drover's server must be able to reach this address.");
        model.Named.KeyLinkText.Should().BeNull();
    }

    [Theory]
    [InlineData("http://keys.example.test/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/page")]
    public async Task A_key_page_that_is_not_an_absolute_https_address_is_not_linked(string page)
    {
        server.Answer("GET", PresetsPath, new[] { new ModelProviderPresetResponse("sample-odd", "Sample odd", "https://odd.example.test/v1", "required", page, false) });
        var model = Step();
        await model.Open();

        model.ChooseProvider("sample-odd");

        model.Named.KeyLinkAddress.Should().BeNull();
        model.Named.KeyLinkText.Should().BeNull();
    }

    [Fact]
    public async Task A_provider_whose_key_is_optional_says_so()
    {
        server.Answer("GET", PresetsPath, SampleProviders.All);
        var model = Step();
        await model.Open();

        model.ChooseProvider("sample-optional");

        model.Named.ShowsKey.Should().BeTrue();
        model.Named.KeyLabel.Should().Be("Key (optional)");
        model.ChooseProvider("sample-hosted");
        model.Named.KeyLabel.Should().Be("Key");
    }

    [Fact]
    public async Task A_failed_read_of_the_list_still_offers_another_compatible_server()
    {
        server.Answer("GET", PresetsPath, HttpStatusCode.InternalServerError);
        var model = Step();

        await model.Open();

        model.Named.State.Should().Be(NamedProvidersState.Failed);
        model.Named.ShowsFailure.Should().BeTrue();
        NamedProviders.FailedWords.Should().Be("Could not read the provider list.");
        model.Named.Options.Select(option => option.Label).Should().Equal("Venice", "Another compatible server");

        model.ChooseProvider("openai_compatible");
        model.BaseUrl = "https://models.example.test/v1";
        model.Named.ShowsAddress.Should().BeTrue();

        server.Answer("GET", PresetsPath, SampleProviders.All);
        await model.RetryProvidersCommand.ExecuteAsync(null);

        model.Named.State.Should().Be(NamedProvidersState.Loaded);
        model.Named.Options.Should().HaveCount(5);
        model.Named.Choice.Should().Be("openai_compatible");
    }

    [Fact]
    public async Task The_list_is_loading_until_the_server_answers()
    {
        var gate = server.Hold("GET", PresetsPath);
        server.Answer("GET", PresetsPath, SampleProviders.All);
        var model = Step();

        var opening = model.Open();

        await Task.Yield();
        model.Named.ShowsLoading.Should().BeTrue();
        gate.SetResult();
        await opening;
        model.Named.ShowsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task A_connection_made_from_a_named_provider_is_compatible_with_its_address_and_name()
    {
        var path = $"{space}/model-connections";
        var created = new ModelConnectionSummary(
            Guid.NewGuid(), "Sample hosted", "openai_compatible", "https://hosted.example.test/v1", true, "pending", null, [], null, null);
        server.Answer("GET", PresetsPath, SampleProviders.All);
        server.Answer("GET", path, Array.Empty<ModelConnectionSummary>());
        server.Answer("POST", path, created, HttpStatusCode.Created);
        server.Answer("POST", $"{path}/{created.Id}/test", new ConnectionTestResponse(true, "Sample connected.", null, created));
        var model = Step();
        await model.Open();
        model.ChooseProvider("sample-hosted");
        model.ApiKey = "sample-key";

        await model.Test();

        var sent = JsonSerializer.Deserialize<CreateModelConnectionRequest>(server.Sent("POST", path).Single(), JsonSerializerOptions.Web)!;
        sent.Should().BeEquivalentTo(new CreateModelConnectionRequest(
            "openai_compatible", "Sample hosted", "https://hosted.example.test/v1", "sample-key", null));
        model.Passed.Should().BeTrue();
    }

    [Fact]
    public async Task A_provider_with_no_key_sends_none_even_when_one_was_typed_before()
    {
        var path = $"{space}/model-connections";
        var created = new ModelConnectionSummary(
            Guid.NewGuid(), "Sample keyless", "openai_compatible", "http://localhost:9/v1", false, "pending", null, [], null, null);
        server.Answer("GET", PresetsPath, SampleProviders.All);
        server.Answer("GET", path, Array.Empty<ModelConnectionSummary>());
        server.Answer("POST", path, created, HttpStatusCode.Created);
        server.Answer("POST", $"{path}/{created.Id}/test", new ConnectionTestResponse(true, "Sample connected.", null, created));
        var model = Step();
        await model.Open();
        model.ApiKey = "sample-key";
        model.ChooseProvider("sample-keyless");

        await model.Test();

        JsonSerializer.Deserialize<CreateModelConnectionRequest>(server.Sent("POST", path).Single(), JsonSerializerOptions.Web)!
            .ApiKey.Should().BeNull();
    }

    #endregion

    #region Private Methods

    private ConnectModelViewModel Step() => new(server.CreateApi(), session, new Navigator(), NullLogger<ConnectModelViewModel>.Instance);

    #endregion
}

/// <summary>Providers made up for the tests, named as samples. The real list is the server's.</summary>
public static class SampleProviders
{
    #region Properties

    public static ModelProviderPresetResponse[] All { get; } =
    [
        new("sample-hosted", "Sample hosted", "https://hosted.example.test/v1", "required", "https://keys.example.test/", false),
        new("sample-keyless", "Sample keyless", "http://localhost:9/v1", "none", null, true),
        new("sample-optional", "Sample optional", "http://localhost:10/v1", "optional", null, true),
    ];

    #endregion
}
