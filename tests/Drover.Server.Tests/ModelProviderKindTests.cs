namespace Drover.Server.Tests;

/// <summary>
/// The kinds of model provider a server offers, over the real server: what the route lists and who may ask.
/// </summary>
public sealed class ModelProviderKindTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Public Methods

    [Fact]
    public async Task The_list_holds_every_registered_kind_with_its_key_and_address_rules()
    {
        var client = await SignedInClient();

        var response = await client.Get(Route);
        var kinds = (await response.Content.ReadFromJsonAsync<List<ModelProviderKindResponse>>(Token))!;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        kinds.Select(kind => kind.Kind).Should().Equal(ModelConnectionKinds.Compatible, Venice, OpenAI, ModelConnectionKinds.CopilotSignIn);
        kinds.Single(kind => kind.Kind == OpenAI).Should().BeEquivalentTo(new ModelProviderKindResponse(
            OpenAI,
            "OpenAI",
            ModelKeyRules.Required,
            ModelAddressRules.Fixed,
            null,
            "Paste an API key from your OpenAI account.",
            false,
            []));
        kinds.Single(kind => kind.Kind == Venice).Should().BeEquivalentTo(new ModelProviderKindResponse(
            Venice,
            "Venice",
            ModelKeyRules.Required,
            ModelAddressRules.Fixed,
            null,
            "Made in your Venice account, under API keys.",
            true,
            []), options => options.ComparingByMembers<ModelProviderKindResponse>());
        kinds.Single(kind => kind.Kind == ModelConnectionKinds.CopilotSignIn).Should().BeEquivalentTo(new ModelProviderKindResponse(
            ModelConnectionKinds.CopilotSignIn,
            "GitHub Copilot",
            ModelKeyRules.None,
            ModelAddressRules.Fixed,
            null,
            null,
            true,
            [],
            true));
        var compatible = kinds.Single(kind => kind.Kind == ModelConnectionKinds.Compatible);
        compatible.KeyRule.Should().Be(ModelKeyRules.Optional);
        compatible.AddressRule.Should().Be(ModelAddressRules.Required);
        compatible.AddressHint.Should().NotBeNullOrWhiteSpace();
        compatible.ReportsCapabilities.Should().BeFalse();
    }

    [Fact]
    public async Task The_list_carries_no_address_of_a_kind_that_has_its_own_address()
    {
        var client = await SignedInClient();

        var response = await client.Get(Route);
        var kinds = (await response.Content.ReadFromJsonAsync<List<ModelProviderKindResponse>>(Token))!;

        kinds.Where(kind => kind.Kind != ModelConnectionKinds.Compatible).Should().OnlyContain(kind => kind.NamedProviders.Count == 0);
        var withoutNamedProviders = JsonSerializer.Serialize(kinds.Select(kind => kind with { NamedProviders = [] }), JsonSerializerOptions.Web);
        withoutNamedProviders.Should().NotContain("api/v1").And.NotContain("venice.ai").And.NotContain("api.openai.com");
        JsonSerializer.Serialize(kinds.Where(kind => kind.Kind != ModelConnectionKinds.Compatible), JsonSerializerOptions.Web)
            .Should().NotContain("://", "only the compatible kind's hint names a scheme, and no kind names an address");
    }

    [Fact]
    public async Task A_kind_that_is_not_registered_cannot_be_created_and_the_refusal_names_it()
    {
        var alice = await SeedPerson("alice");
        var client = NewClient();
        (await client.SignIn(alice.Email, alice.Password)).EnsureSuccessStatusCode();

        var response = await client.Post(
            $"/api/spaces/{alice.SpaceId}/model-connections",
            new CreateModelConnectionRequest("no_such_kind", "Nothing", null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Token)).Should().Contain("no_such_kind");
    }

    [Fact]
    public async Task A_signed_out_caller_gets_401()
    {
        var client = NewClient();

        var response = await client.Get(Route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Constants

    private const string Route = "/api/model-provider-kinds";

    private const string Venice = "venice";

    private const string OpenAI = "openai";

    #endregion

    #region Private Methods

    private async Task<ApiClient> SignedInClient()
    {
        var alice = await SeedPerson("alice");
        var client = NewClient();
        (await client.SignIn(alice.Email, alice.Password)).EnsureSuccessStatusCode();
        return client;
    }

    #endregion
}

/// <summary>The server's start, when the providers it was given cannot all be served.</summary>
public sealed class ModelProviderStartTests(PostgresServer postgres)
{
    #region Public Methods

    [Fact]
    public async Task Two_providers_with_one_kind_stop_the_server_at_start_and_name_the_kind()
    {
        await using var database = await postgres.CreateDatabase();
        await using var factory = new DroverFactory(
            database,
            Path.Combine(Path.GetTempPath(), $"drover-keys-{Guid.NewGuid():N}"),
            new LogCapture(),
            new AdjustableTimeProvider(),
            new HashCounter(),
            new Dictionary<string, string?> { ["Drover:SignInRateLimit:PermitLimit"] = "1000" },
            configure: services => services.AddSingleton<IModelClientProvider>(
                _ => new ScriptedModelProvider(new ScriptedModel(), "venice")));

        var start = () => factory.Services;

        var failure = start.Should().Throw<Exception>().Which;
        Messages(failure).Should().Contain(message => message.Contains("'venice'", StringComparison.Ordinal));
    }

    #endregion

    #region Private Methods

    private static IEnumerable<string> Messages(Exception failure)
    {
        for (var next = failure; next is not null; next = next.InnerException)
        {
            yield return next.Message;
        }
    }

    #endregion
}

/// <summary>
/// A server whose operator lists the kinds it offers: only the listed ones are shown and made, and a space cannot
/// turn on a kind the deployment turned off.
/// </summary>
public sealed class OperatorListedModelProviderKindTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings =>
        new Dictionary<string, string?>(base.Settings)
        {
            ["Drover:Policy:Rules:ModelProviders"] = nameof(ModelProviderSet.OperatorList),
            [$"{DroverFactory.AllowedKindsSetting}:0"] = "venice",
        };

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_kind_the_policy_does_not_allow_is_not_listed_and_cannot_be_created()
    {
        var alice = await SeedPerson("alice");
        var client = NewClient();
        (await client.SignIn(alice.Email, alice.Password)).EnsureSuccessStatusCode();

        var kinds = (await (await client.Get("/api/model-provider-kinds")).Content
            .ReadFromJsonAsync<List<ModelProviderKindResponse>>(Token))!;
        var refused = await client.Post(
            $"/api/spaces/{alice.SpaceId}/model-connections",
            new CreateModelConnectionRequest(ModelConnectionKinds.Compatible, "Local", "https://models.example.test/v1", null, null));
        var allowed = await client.Post(
            $"/api/spaces/{alice.SpaceId}/model-connections",
            new CreateModelConnectionRequest("venice", "Venice", null, null, null));

        kinds.Select(kind => kind.Kind).Should().Equal("venice");
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync(Token)).Should().Contain(ModelConnectionKinds.Compatible);
        allowed.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_refused_kind_leaves_no_connection_behind()
    {
        var alice = await SeedPerson("alice");
        var client = NewClient();
        (await client.SignIn(alice.Email, alice.Password)).EnsureSuccessStatusCode();

        var response = await client.Post(
            $"/api/spaces/{alice.SpaceId}/model-connections",
            new CreateModelConnectionRequest(ModelConnectionKinds.Compatible, "Local", "https://models.example.test/v1", "a-key", null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var connections = (await (await client.Get($"/api/spaces/{alice.SpaceId}/model-connections")).Content
            .ReadFromJsonAsync<List<ModelConnectionSummary>>(Token))!;
        connections.Should().BeEmpty();
    }

    #endregion
}

/// <summary>With the rule left at All, every registered kind is offered and no list is needed.</summary>
public sealed class AllModelProviderKindsTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings =>
        new Dictionary<string, string?>(base.Settings) { [$"{DroverFactory.AllowedKindsSetting}:0"] = "venice" };

    #endregion

    #region Public Methods

    [Fact]
    public async Task With_the_rule_at_All_the_list_of_allowed_kinds_is_ignored()
    {
        var alice = await SeedPerson("alice");
        var client = NewClient();
        (await client.SignIn(alice.Email, alice.Password)).EnsureSuccessStatusCode();

        var kinds = (await (await client.Get("/api/model-provider-kinds")).Content
            .ReadFromJsonAsync<List<ModelProviderKindResponse>>(Token))!;

        kinds.Select(kind => kind.Kind).Should().Equal(ModelConnectionKinds.Compatible, "venice", "openai", ModelConnectionKinds.CopilotSignIn);
    }

    #endregion
}
