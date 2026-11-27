namespace Drover.Presentation.Tests.ViewModels;

/// <summary>The Connect a model step with the named providers the server offers.</summary>
public sealed class ConnectModelViewModelTests : IDisposable
{
    #region Private Fields

    private const string PresetsPath = "/api/model-provider-kinds";

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
        server.Answer("GET", PresetsPath, SampleProviders.Kinds(SampleProviders.All));
        var model = Step();

        await model.Open();

        model.Named.Options.Select(option => option.Label).Should().Equal(
            "Venice", "Sample hosted", "Sample keyless", "Sample optional", "Another compatible server");
        model.Named.Options.Select(option => option.Value).Should().Equal(
            "venice", "sample-hosted", "sample-keyless", "sample-optional", "openai_compatible");
        model.Named.State.Should().Be(NamedProvidersState.Loaded);
    }

    [Fact]
    public async Task The_gemini_kind_the_server_offers_is_a_key_only_choice_with_its_helper_sentence()
    {
        server.Answer("GET", PresetsPath, new ModelProviderKindResponse[]
        {
            new("openai_compatible", "Compatible endpoint", "optional", "required", "Sample hint.", "Sample hint.", false, []),
            new("venice", "Venice", "required", "fixed", null, "Sample hint.", true, []),
            new("gemini", "Google Gemini", "required", "fixed", null, "Paste a Gemini API key. A personal plan cannot be used here.", true, []),
        });
        var model = Step();
        await model.Open();

        model.ChooseProvider("gemini");

        model.Named.Options.Select(option => option.Label).Should().Contain("Google Gemini");
        model.Kind.Should().Be("gemini");
        model.Named.ShowsAddress.Should().BeFalse();
        model.Named.OffersAddressChange.Should().BeFalse();
        model.Named.ShowsKey.Should().BeTrue();
        model.Named.KeyHint.Should().Be("Paste a Gemini API key. A personal plan cannot be used here.");
    }

    [Fact]
    public async Task A_server_with_no_named_providers_offers_the_two_choices_of_before()
    {
        server.Answer("GET", PresetsPath, SampleProviders.Kinds());
        var model = Step();

        await model.Open();

        model.Named.Options.Select(option => option.Label).Should().Equal("Venice", "Another compatible server");
    }

    [Fact]
    public async Task A_named_provider_hides_the_address_until_asked()
    {
        server.Answer("GET", PresetsPath, SampleProviders.Kinds(SampleProviders.All));
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
        server.Answer("GET", PresetsPath, SampleProviders.Kinds(SampleProviders.All));
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
        server.Answer("GET", PresetsPath, SampleProviders.Kinds(new ModelProviderPresetResponse("sample-odd", "Sample odd", "https://odd.example.test/v1", "required", page, false)));
        var model = Step();
        await model.Open();

        model.ChooseProvider("sample-odd");

        model.Named.KeyLinkAddress.Should().BeNull();
        model.Named.KeyLinkText.Should().BeNull();
    }

    [Fact]
    public async Task A_provider_whose_key_is_optional_says_so()
    {
        server.Answer("GET", PresetsPath, SampleProviders.Kinds(SampleProviders.All));
        var model = Step();
        await model.Open();

        model.ChooseProvider("sample-optional");

        model.Named.ShowsKey.Should().BeTrue();
        model.Named.KeyLabel.Should().Be("Key (optional)");
        model.ChooseProvider("sample-hosted");
        model.Named.KeyLabel.Should().Be("Key");
    }

    [Fact]
    public async Task A_failed_read_of_the_list_draws_no_form_until_it_is_read_again()
    {
        server.Answer("GET", PresetsPath, HttpStatusCode.InternalServerError);
        var model = Step();

        await model.Open();

        model.Named.State.Should().Be(NamedProvidersState.Failed);
        model.Named.ShowsFailure.Should().BeTrue();
        model.Named.ShowsForm.Should().BeFalse("the form is not drawn until the kinds are read");

        server.Answer("GET", PresetsPath, SampleProviders.Kinds(SampleProviders.All));
        await model.RetryProvidersCommand.ExecuteAsync(null);

        model.Named.State.Should().Be(NamedProvidersState.Loaded);
        model.Named.ShowsForm.Should().BeTrue();
        model.Named.Options.Should().HaveCount(5);
    }

    [Fact]
    public async Task The_list_is_loading_until_the_server_answers()
    {
        var gate = server.Hold("GET", PresetsPath);
        server.Answer("GET", PresetsPath, SampleProviders.Kinds(SampleProviders.All));
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
        server.Answer("GET", PresetsPath, SampleProviders.Kinds(SampleProviders.All));
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
    public async Task The_provider_choice_is_built_from_the_servers_kinds()
    {
        server.Answer("GET", PresetsPath, SampleProviders.KindsWithThird());
        var model = Step();

        await model.Open();

        model.Named.Options.Select(option => option.Label).Should().Equal("Venice", "Another compatible server", "Sample third");
        model.Named.Options.Select(option => option.Value).Should().Equal("venice", "openai_compatible", "sample-third");
        model.Named.UsesList.Should().BeFalse("up to four choices are segments");
        model.Named.Choice.Should().Be("venice", "the first kind in the server's order is chosen at first");

        model.ChooseProvider("sample-third");

        model.Kind.Should().Be("sample-third");
        model.Named.ShowsAddress.Should().BeTrue();
        model.Named.AddressHint.Should().Be("Sample address hint.");
        model.Named.ShowsKey.Should().BeTrue();
        model.Named.KeyLabel.Should().Be("Key (optional)");
        model.Named.KeyHint.Should().Be("Sample third key hint.");
    }

    [Fact]
    public async Task Five_or_more_choices_are_a_list_and_one_kind_is_no_choice()
    {
        server.Answer("GET", PresetsPath, SampleProviders.Kinds(SampleProviders.All));
        var model = Step();
        await model.Open();

        model.Named.Options.Should().HaveCount(5);
        model.Named.UsesList.Should().BeTrue();

        server.Answer("GET", PresetsPath, new[] { SampleProviders.Third });
        await model.RetryProvidersCommand.ExecuteAsync(null);

        model.Named.ShowsChoice.Should().BeFalse();
        model.Named.OnlyKindWords.Should().Be("This server uses Sample third.");
        model.Kind.Should().Be("sample-third");
    }

    [Fact]
    public async Task Four_choices_are_segments_and_five_are_a_list()
    {
        server.Answer("GET", PresetsPath, SampleProviders.Kinds(SampleProviders.All[0], SampleProviders.All[1]));
        var model = Step();

        await model.Open();

        model.Named.Options.Should().HaveCount(4);
        model.Named.UsesList.Should().BeFalse();
    }

    [Fact]
    public async Task A_kind_with_a_fixed_address_shows_no_address_field()
    {
        server.Answer("GET", PresetsPath, SampleProviders.Kinds());
        var model = Step();
        await model.Open();

        model.Named.ShowsAddress.Should().BeFalse();
        model.Named.AddressHint.Should().BeNull();
        model.Named.ShowsKey.Should().BeTrue();
        model.Named.KeyLabel.Should().Be("Key");

        model.ChooseProvider("openai_compatible");
        model.Named.ShowsAddress.Should().BeTrue();
    }

    [Fact]
    public async Task A_kind_that_uses_no_key_shows_no_key_field()
    {
        server.Answer("GET", PresetsPath, new[] { SampleProviders.Third with { KeyRule = ModelKeyRules.None } });
        var model = Step();

        await model.Open();

        model.Named.ShowsKey.Should().BeFalse();
    }

    [Fact]
    public async Task A_kind_whose_key_is_optional_can_continue_with_the_key_empty()
    {
        var path = $"{space}/model-connections";
        var created = new ModelConnectionSummary(
            Guid.NewGuid(), "models.example.test", "sample-third", "https://models.example.test/v1", false, "pending", null, [], null, null);
        server.Answer("GET", PresetsPath, SampleProviders.KindsWithThird());
        server.Answer("GET", path, Array.Empty<ModelConnectionSummary>());
        server.Answer("POST", path, created, HttpStatusCode.Created);
        server.Answer("POST", $"{path}/{created.Id}/test", new ConnectionTestResponse(true, "Sample connected.", null, created));
        var model = Step();
        await model.Open();
        model.ChooseProvider("sample-third");
        model.BaseUrl = "https://models.example.test/v1";

        await model.Test();

        model.KeyProblem.Should().BeNull();
        model.Passed.Should().BeTrue();
        JsonSerializer.Deserialize<CreateModelConnectionRequest>(server.Sent("POST", path).Single(), JsonSerializerOptions.Web)!
            .Should().BeEquivalentTo(new CreateModelConnectionRequest(
                "sample-third", "models.example.test", "https://models.example.test/v1", null, null));
    }

    [Fact]
    public async Task A_required_field_left_empty_says_what_to_enter_and_sends_nothing()
    {
        server.Answer("GET", PresetsPath, SampleProviders.Kinds());
        var model = Step();
        await model.Open();

        await model.Test();

        model.KeyProblem.Should().Be("Enter the key.");
        model.AddressProblem.Should().BeNull();
        server.Sent("POST", $"{space}/model-connections").Should().BeEmpty();

        model.ChooseProvider("openai_compatible");
        model.BaseUrl = string.Empty;
        await model.Test();

        model.AddressProblem.Should().Be("Enter the base address.");
    }

    [Fact]
    public async Task Changing_the_provider_keeps_a_typed_address_and_clears_a_typed_key()
    {
        server.Answer("GET", PresetsPath, SampleProviders.KindsWithThird());
        var model = Step();
        await model.Open();
        model.ChooseProvider("openai_compatible");
        model.BaseUrl = "https://models.example.test/v1";
        model.ApiKey = "sample-key";

        model.ChooseProvider("sample-third");

        model.BaseUrl.Should().Be("https://models.example.test/v1");
        model.ApiKey.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_read_of_the_kinds_says_so_and_offers_Try_again()
    {
        server.Answer("GET", PresetsPath, HttpStatusCode.InternalServerError);
        var model = Step();

        await model.Open();

        model.Named.ShowsFailure.Should().BeTrue();
        model.Named.ShowsForm.Should().BeFalse();
        model.Named.FailureWords.Should().NotBeNullOrWhiteSpace("the failure sentence is drawn under the title");
        NamedProviders.FailedTitle.Should().Be("Drover could not load the providers");
        NamedProviders.RetryWords.Should().Be("Try again");
        model.Named.OnlyKindWords.Should().BeNull("a list that could not be read does not say the server has one kind");

        server.Answer("GET", PresetsPath, SampleProviders.KindsWithThird());
        await model.RetryProvidersCommand.ExecuteAsync(null);

        model.Named.ShowsFailure.Should().BeFalse();
        model.Named.Options.Should().HaveCount(3);
    }

    [Fact]
    public async Task Continue_with_no_passing_test_saves_the_connection_untested_and_moves_on()
    {
        var path = $"{space}/model-connections";
        var created = new ModelConnectionSummary(
            Guid.NewGuid(), "Venice", "venice", null, true, "pending", null, [], null, null);
        server.Answer("GET", PresetsPath, SampleProviders.Kinds());
        server.Answer("GET", path, Array.Empty<ModelConnectionSummary>());
        server.Answer("POST", path, created, HttpStatusCode.Created);
        var navigator = new Navigator();
        var model = new ConnectModelViewModel(server.CreateApi(), session, navigator, NullLogger<ConnectModelViewModel>.Instance);
        await model.Open();
        model.ApiKey = "sample-key";

        await model.Continue();

        server.Sent("POST", path).Should().ContainSingle();
        server.Sent("POST", $"{path}/{created.Id}/test").Should().BeEmpty("nothing was tested");
        navigator.Current.Should().Be(Addresses.SetupBot);
    }

    [Fact]
    public async Task Continue_with_a_required_field_empty_says_what_to_enter_and_stays()
    {
        server.Answer("GET", PresetsPath, SampleProviders.Kinds());
        var navigator = new Navigator();
        var model = new ConnectModelViewModel(server.CreateApi(), session, navigator, NullLogger<ConnectModelViewModel>.Instance);
        await model.Open();

        await model.Continue();

        model.KeyProblem.Should().Be("Enter the key.");
        navigator.Current.Should().NotBe(Addresses.SetupBot);
    }

    [Fact]
    public async Task A_server_that_offers_no_kind_draws_a_state_in_place_of_the_form()
    {
        server.Answer("GET", PresetsPath, Array.Empty<ModelProviderKindResponse>());
        var model = Step();

        await model.Open();

        model.Named.OffersNone.Should().BeTrue();
        model.Named.ShowsForm.Should().BeFalse();
        model.Named.ShowsState.Should().BeTrue();
        model.Named.StateTitle.Should().Be("This server offers no model provider");
        model.Named.StateCause.Should().Contain("The operator of this server has switched every provider off");
        await model.Test();
        server.Sent("POST", $"{space}/model-connections").Should().BeEmpty("there is no kind to connect");
    }

    [Fact]
    public async Task A_choice_the_server_stopped_offering_falls_back_to_the_first_kind()
    {
        server.Answer("GET", PresetsPath, SampleProviders.KindsWithThird());
        var model = Step();
        await model.Open();
        model.ChooseProvider("sample-third");
        server.Answer("GET", PresetsPath, SampleProviders.Kinds());

        await model.RetryProvidersCommand.ExecuteAsync(null);

        model.Kind.Should().Be("venice");
    }

    [Fact]
    public async Task A_provider_with_no_key_sends_none_even_when_one_was_typed_before()
    {
        var path = $"{space}/model-connections";
        var created = new ModelConnectionSummary(
            Guid.NewGuid(), "Sample keyless", "openai_compatible", "http://localhost:9/v1", false, "pending", null, [], null, null);
        server.Answer("GET", PresetsPath, SampleProviders.Kinds(SampleProviders.All));
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

    [Fact]
    public async Task A_kind_with_its_own_wire_is_offered_after_venice_and_takes_a_key_and_no_address()
    {
        var path = $"{space}/model-connections";
        var created = new ModelConnectionSummary(
            Guid.NewGuid(), "Sample own wire", "sample_own_wire", null, true, "pending", null, [], null, null);
        server.Answer("GET", PresetsPath, SampleProviders.WithOwnWire(SampleProviders.All));
        server.Answer("GET", path, Array.Empty<ModelConnectionSummary>());
        server.Answer("POST", path, created, HttpStatusCode.Created);
        server.Answer("POST", $"{path}/{created.Id}/test", new ConnectionTestResponse(true, "Sample connected.", null, created));
        var model = Step();
        await model.Open();

        model.Named.Options.Select(option => option.Label).Should().Equal(
            "Venice", "Sample own wire", "Sample hosted", "Sample keyless", "Sample optional", "Another compatible server");
        model.ChooseProvider("sample_own_wire");
        model.BaseUrl = "https://ignored.example.test/v1";
        model.ApiKey = "sample-key";
        await model.Test();

        model.Kind.Should().Be("sample_own_wire");
        model.Named.ShowsAddress.Should().BeFalse();
        model.Named.ShowsKey.Should().BeTrue();
        model.Named.KeyLabel.Should().Be("Key");
        model.Named.KeyHint.Should().Be("Sample own wire hint.");
        JsonSerializer.Deserialize<CreateModelConnectionRequest>(server.Sent("POST", path).Single(), JsonSerializerOptions.Web)!
            .Should().BeEquivalentTo(new CreateModelConnectionRequest("sample_own_wire", "Sample own wire", null, "sample-key", null));
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

    /// <summary>What the server answers for its kinds: Venice, and the compatible kind with these named providers.</summary>
    public static ModelProviderKindResponse[] Kinds(params ModelProviderPresetResponse[] named) =>
    [
        new("venice", "Venice", "required", "fixed", null, "Sample hint.", true, []),
        new("openai_compatible", "Compatible endpoint", "optional", "required", "Sample hint.", "Sample hint.", false, named),
    ];

    /// <summary>A kind the client has never heard of: a required address and an optional key.</summary>
    public static ModelProviderKindResponse Third { get; } =
        new("sample-third", "Sample third", "optional", "required", "Sample address hint.", "Sample third key hint.", false, []);

    public static ModelProviderKindResponse[] KindsWithThird() => [.. Kinds(), Third];

    /// <summary>The same, with a kind that has its own wire, a fixed address and a required key.</summary>
    public static ModelProviderKindResponse[] WithOwnWire(params ModelProviderPresetResponse[] named) =>
    [
        new("venice", "Venice", "required", "fixed", null, "Sample hint.", true, []),
        new("sample_own_wire", "Sample own wire", "required", "fixed", null, "Sample own wire hint.", false, []),
        new("openai_compatible", "Compatible endpoint", "optional", "required", "Sample hint.", "Sample hint.", false, named),
    ];

    public static ModelProviderPresetResponse[] All { get; } =
    [
        new("sample-hosted", "Sample hosted", "https://hosted.example.test/v1", "required", "https://keys.example.test/", false),
        new("sample-keyless", "Sample keyless", "http://localhost:9/v1", "none", null, true),
        new("sample-optional", "Sample optional", "http://localhost:10/v1", "optional", null, true),
    ];

    #endregion
}
