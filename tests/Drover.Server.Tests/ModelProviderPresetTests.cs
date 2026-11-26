namespace Drover.Server.Tests;

/// <summary>
/// The named providers a server offers: only to a signed-in person, in the table's order, with no secret, and with
/// the local ones left out where the deployment forbids private addresses. A connection made from one is an
/// ordinary compatible connection.
/// </summary>
public abstract class ModelProviderPresetTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    protected const string Route = "/api/model-provider-kinds";

    protected static readonly string[] RowFields = ["address", "displayName", "id", "keyPage", "keyRule", "local"];

    /// <summary>The rows this server lists: every row but the one whose own kind (OpenAI's) the server offers.</summary>
    protected static IEnumerable<ModelProviderPreset> Rows => KnownProviders.All.Where(row => row.Id != OpenAIProvider.ConnectionKind);

    #endregion

    #region Helpers

    protected async Task<ApiClient> SignedIn()
    {
        var alice = await SeedPerson("alice");
        var client = NewClient();
        (await client.SignIn(alice.Email, alice.Password)).EnsureSuccessStatusCode();
        return client;
    }

    protected static async Task<List<ModelProviderPresetResponse>> Listed(ApiClient client)
    {
        var response = await client.Get(Route);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        var kinds = (await response.Content.ReadFromJsonAsync<List<ModelProviderKindResponse>>(Token))!;
        return [.. kinds.Single(kind => kind.Kind == ModelConnectionKinds.Compatible).NamedProviders];
    }

    #endregion
}

public sealed class SelfHostedModelProviderPresetTests(PostgresServer postgres) : ModelProviderPresetTests(postgres)
{
    #region Public Methods

    [Fact]
    public async Task A_signed_out_caller_gets_401()
    {
        var response = await NewClient().Get(Route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_list_holds_every_row_and_no_secret()
    {
        var client = await SignedIn();
        var response = await client.Get(Route);
        var body = await response.Content.ReadAsStringAsync(Token);

        var listed = await Listed(client);

        listed.Select(row => row.Id).Should().Equal(Rows.Select(row => row.Id));
        listed.Should().BeEquivalentTo(
            Rows.Select(row => new ModelProviderPresetResponse(
                row.Id,
                row.DisplayName,
                row.Address,
                row.KeyRule.ToString().ToLowerInvariant(),
                row.KeyPage,
                row.Local)),
            options => options.WithStrictOrdering());
        listed.Select(row => row.KeyRule).Should().OnlyContain(rule =>
            rule == ModelProviderKeyRules.Required || rule == ModelProviderKeyRules.Optional || rule == ModelProviderKeyRules.None);
        using var document = JsonDocument.Parse(body);
        document.RootElement.EnumerateArray().Single(kind => kind.GetProperty("kind").GetString() == ModelConnectionKinds.Compatible)
            .GetProperty("namedProviders").EnumerateArray()
            .Select(row => row.EnumerateObject().Select(property => property.Name).Order().ToList())
            .Should().OnlyContain(names => names.SequenceEqual(RowFields), "a row carries no secret");
    }

    [Fact]
    public async Task A_connection_made_from_a_row_is_a_compatible_connection()
    {
        var alice = await SeedPerson("alice");
        var client = NewClient();
        (await client.SignIn(alice.Email, alice.Password)).EnsureSuccessStatusCode();
        var row = (await Listed(client)).Single(preset => preset.Id == "groq");
        var path = $"/api/spaces/{alice.SpaceId}/model-connections";

        var created = await client.Post(path, new CreateModelConnectionRequest(
            ModelConnectionKinds.Compatible, row.DisplayName, row.Address, TestKey(), null));

        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Token));
        var connection = (await created.Content.ReadFromJsonAsync<ModelConnectionSummary>(Token))!;
        (connection.Kind, connection.BaseUrl, connection.Name, connection.HasKey)
            .Should().Be((ModelConnectionKinds.Compatible, row.Address, row.DisplayName, true));

        await SeedModel(alice, connection.Id, "some-model");
        var vision = await client.Put($"{path}/{connection.Id}/models/vision", new SetModelVisionRequest("some-model", true));
        var voice = await client.Put($"{path}/{connection.Id}/models/voice", new SetModelVoiceRequest("some-model", true, false, false));
        vision.StatusCode.Should().Be(HttpStatusCode.OK, await vision.Content.ReadAsStringAsync(Token));
        voice.StatusCode.Should().Be(HttpStatusCode.OK, await voice.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task A_server_that_allows_private_addresses_lists_the_local_rows()
    {
        var listed = await Listed(await SignedIn());

        listed.Where(row => row.Local).Select(row => row.Id).Should().Equal(KnownProviders.All.Where(row => row.Local).Select(row => row.Id));
    }

    #endregion

    #region Private Methods

    private static string TestKey() => $"test-key-{Convert.ToHexString(RandomNumberGenerator.GetBytes(12))}";

    private async Task SeedModel((string Email, string Password, Guid UserId, Guid SpaceId) person, Guid connectionId, string model)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(person.SpaceId, person.UserId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var db = scope.ServiceProvider.GetRequiredService<DroverDbContext>();
        db.ModelCatalog.Add(new ModelCatalogEntry
        {
            ModelConnectionId = connectionId,
            Model = model,
            DisplayName = model,
            Capabilities = new ModelCapabilities(),
            LastSeenAt = Time.GetUtcNow(),
        });
        await db.SaveChangesAsync(Token);
        await work.Commit(Token);
    }

    #endregion
}

public sealed class HostedModelProviderPresetTests(PostgresServer postgres) : ModelProviderPresetTests(postgres)
{
    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings
    {
        get
        {
            var settings = new Dictionary<string, string?>(base.Settings) { ["DROVER_POLICY"] = DeploymentPolicy.HostedName };

            foreach (var (key, value) in KeyRingCertificate.Settings)
            {
                settings[key] = value;
            }

            return settings;
        }
    }

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_server_that_forbids_private_addresses_lists_no_local_row()
    {
        var listed = await Listed(await SignedIn());

        listed.Should().NotBeEmpty();
        listed.Should().OnlyContain(row => !row.Local);
        // This hosted server's operator list does not offer OpenAI's own kind, so its row is listed.
        listed.Select(row => row.Id).Should().Equal(KnownProviders.All.Where(row => !row.Local).Select(row => row.Id));
    }

    #endregion
}

/// <summary>What the catalog offers of the compatible kind's named providers under each rule of the deployment policy.</summary>
public sealed class ModelProviderCatalogNamedProviderTests
{
    #region Public Methods

    [Fact]
    public void A_kind_the_operator_does_not_offer_offers_no_named_provider()
    {
        var rules = DeploymentPolicy.SelfHosted with { ModelProviders = ModelProviderSet.OperatorList, AllowedModelProviderKinds = ["venice"] };

        Catalog(rules).Offered.Should().NotContain(descriptor => descriptor.Kind == ModelConnectionKinds.Compatible);
    }

    [Fact]
    public void The_operators_list_can_offer_the_compatible_kind_and_with_it_its_named_providers()
    {
        var rules = DeploymentPolicy.SelfHosted with { ModelProviders = ModelProviderSet.OperatorList, AllowedModelProviderKinds = [ModelConnectionKinds.Compatible] };

        Catalog(rules).Offered.Single().NamedProviders.Should().Equal(KnownProviders.All);
    }

    [Fact]
    public void Forbidding_private_addresses_leaves_out_only_the_local_ones_and_keeps_the_order()
    {
        var offered = Catalog(DeploymentPolicy.SelfHosted with { AllowPrivateAddresses = false }).Offered.Single().NamedProviders;

        offered.Should().Equal(KnownProviders.All.Where(row => !row.Local));
    }

    [Fact]
    public void A_named_provider_whose_own_kind_is_offered_is_not_listed()
    {
        var offered = WithOpenAI(DeploymentPolicy.SelfHosted).Offered.Single(descriptor => descriptor.Kind == ModelConnectionKinds.Compatible);

        offered.NamedProviders.Should().NotContain(row => row.Id == OpenAIProvider.ConnectionKind);
        offered.NamedProviders.Should().Equal(KnownProviders.All.Where(row => row.Id != OpenAIProvider.ConnectionKind));
    }

    [Fact]
    public void A_named_provider_comes_back_when_the_policy_switches_its_own_kind_off()
    {
        var rules = DeploymentPolicy.SelfHosted with { ModelProviders = ModelProviderSet.OperatorList, AllowedModelProviderKinds = [ModelConnectionKinds.Compatible] };

        var offered = WithOpenAI(rules).Offered.Single();

        offered.NamedProviders.Should().Contain(row => row.Id == OpenAIProvider.ConnectionKind);
        offered.NamedProviders.Should().Equal(KnownProviders.All);
    }

    #endregion

    #region Private Methods

    private static ModelProviderCatalog Catalog(DeploymentPolicy rules) =>
        new([new OpenAICompatibleProvider(new NoNetwork())], Options.Create(new DeploymentPolicyOptions { Rules = rules }));

    private static ModelProviderCatalog WithOpenAI(DeploymentPolicy rules) =>
        new([new OpenAICompatibleProvider(new NoNetwork()), new OpenAIProvider(new NoNetwork())], Options.Create(new DeploymentPolicyOptions { Rules = rules }));

    #endregion

    #region Nested Types

    private sealed class NoNetwork : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("No call is made.");
    }

    #endregion
}
