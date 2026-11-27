namespace Drover.Server.Tests;

/// <summary>
/// A space admin says what a model can do and costs, on any connection. What the admin set outlives a refresh, is
/// cleared one member at a time, is read by every part of the server through the effective values, and leaves an
/// audit row.
/// </summary>
public sealed class ModelSettingsApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private ScriptedProvider Scripted { get; } = new();

    private ScriptedModelProvider? model;

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(new ScriptedModel()));
        services.AddSingleton<IModelClientProvider>(Scripted);
    }

    #endregion

    #region Setting

    [Fact]
    public async Task An_admin_sets_vision_context_and_prices_and_the_list_says_which_members_are_set()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateCompatible(client, alice.SpaceId);
        await SeedModels(alice, connection.Id, ("plain", new ModelCapabilities { ContextTokens = 4096 }));

        var saved = await Put(client, alice.SpaceId, connection.Id, new SetModelSettingsRequest(
            "plain",
            new ModelAbilitiesRequest(ContextSize: 20_000_000, Vision: true),
            new ModelPricingRequest(InputPerMillion: 0.5m, OutputPerMillion: 1.25m, Currency: "eur")));

        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Token));
        var listed = (await ListModels(client, alice.SpaceId, connection.Id)).Single();
        listed.Capabilities.ContextSize.Should().Be(20_000_000, "a size above ten million is accepted");
        listed.Capabilities.Vision.Should().BeTrue();
        listed.Price.Should().Be(new ModelPriceResponse(0.5m, 1.25m, null, "EUR"));
        listed.OperatorSet.Should().BeEquivalentTo(["contextSize", "vision", "inputPerMillion", "outputPerMillion"]);
    }

    [Fact]
    public async Task A_refresh_that_lists_the_model_with_empty_facts_leaves_the_admins_values_in_effect()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateScripted(client, alice.SpaceId);
        Scripted.OnList = _ => Task.FromResult<IReadOnlyList<ModelInfo>>([new ModelInfo("plain", "plain", new ModelCapabilities(), [])]);
        (await client.Post($"{Path(alice.SpaceId)}/{connection.Id}/models/refresh")).EnsureSuccessStatusCode();
        (await Put(client, alice.SpaceId, connection.Id, new SetModelSettingsRequest(
            "plain",
            new ModelAbilitiesRequest(Vision: true, ContextSize: 32_000),
            new ModelPricingRequest(InputPerMillion: 2m)))).EnsureSuccessStatusCode();

        (await client.Post($"{Path(alice.SpaceId)}/{connection.Id}/models/refresh")).EnsureSuccessStatusCode();

        var listed = (await ListModels(client, alice.SpaceId, connection.Id)).Single();
        (listed.Capabilities.Vision, listed.Capabilities.ContextSize, listed.Price.InputPerMillion).Should().Be((true, 32_000, 2m));
    }

    [Fact]
    public async Task Clearing_one_member_falls_back_to_the_providers_value_for_that_member_only()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateCompatible(client, alice.SpaceId);
        await SeedModels(alice, connection.Id, ("plain", new ModelCapabilities { ContextTokens = 8192 }));
        (await Put(client, alice.SpaceId, connection.Id, new SetModelSettingsRequest(
            "plain",
            new ModelAbilitiesRequest(ContextSize: 32_000, Vision: true),
            null))).EnsureSuccessStatusCode();

        (await Put(client, alice.SpaceId, connection.Id, new SetModelSettingsRequest("plain", new ModelAbilitiesRequest(Vision: true), null)))
            .EnsureSuccessStatusCode();

        var listed = (await ListModels(client, alice.SpaceId, connection.Id)).Single();
        (listed.Capabilities.ContextSize, listed.Capabilities.Vision).Should().Be((8192, true));
        listed.OperatorSet.Should().BeEquivalentTo(["vision"]);
    }

    [Fact]
    public async Task Deleting_the_settings_clears_everything_and_leaves_the_row()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateCompatible(client, alice.SpaceId);
        await SeedModels(alice, connection.Id, ("vendor/plain", new ModelCapabilities()));
        (await Put(client, alice.SpaceId, connection.Id, new SetModelSettingsRequest(
            "vendor/plain",
            new ModelAbilitiesRequest(Vision: true),
            new ModelPricingRequest(InputPerMillion: 1m)))).EnsureSuccessStatusCode();

        var cleared = await client.Delete($"{Path(alice.SpaceId)}/{connection.Id}/model-settings?model={Uri.EscapeDataString("vendor/plain")}");

        cleared.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var listed = (await ListModels(client, alice.SpaceId, connection.Id)).Single();
        listed.Capabilities.Vision.Should().BeFalse();
        listed.Price.InputPerMillion.Should().BeNull();
        listed.OperatorSet.Should().BeEmpty();
    }

    [Fact]
    public async Task A_model_the_catalog_does_not_have_is_added_and_a_second_put_updates_it()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateCompatible(client, alice.SpaceId);

        (await Put(client, alice.SpaceId, connection.Id, new SetModelSettingsRequest("meta/unlisted-1", new ModelAbilitiesRequest(ContextSize: 8000), null)))
            .EnsureSuccessStatusCode();
        (await Put(client, alice.SpaceId, connection.Id, new SetModelSettingsRequest("meta/unlisted-1", new ModelAbilitiesRequest(ContextSize: 16_000), null)))
            .EnsureSuccessStatusCode();

        var listed = (await ListModels(client, alice.SpaceId, connection.Id)).Single();
        (listed.Model, listed.State, listed.Capabilities.ContextSize).Should().Be(("meta/unlisted-1", "available", 16_000));
    }

    [Fact]
    public async Task A_value_set_on_a_provider_that_reports_its_own_capabilities_takes_effect_like_any_other()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var own = await Read<ModelConnectionSummary>(await client.Post(
            Path(alice.SpaceId),
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Reports its own", null, null, "plain")));
        await SeedModels(alice, own.Id, ("plain", new ModelCapabilities()));

        (await Put(client, alice.SpaceId, own.Id, new SetModelSettingsRequest("plain", new ModelAbilitiesRequest(Vision: true), null)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await ListModels(client, alice.SpaceId, own.Id)).Single().Capabilities.Vision.Should().BeTrue();
    }

    #endregion

    #region Who And What

    [Fact]
    public async Task A_member_gets_403_and_another_space_gets_404()
    {
        var alice = await SeedPerson("alice");
        var bob = await SeedPerson("bob");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var asAlice = await SignIn(alice);
        var asBob = await SignIn(bob);
        var asMember = NewClient();
        await asMember.SignIn(member.Email, member.Password);
        var connection = await CreateCompatible(asAlice, alice.SpaceId);
        await SeedModels(alice, connection.Id, ("plain", new ModelCapabilities()));
        var request = new SetModelSettingsRequest("plain", new ModelAbilitiesRequest(Vision: true), null);

        (await Put(asMember, alice.SpaceId, connection.Id, request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await asMember.Delete($"{Path(alice.SpaceId)}/{connection.Id}/model-settings?model=plain")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Put(asBob, alice.SpaceId, connection.Id, request)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Put(asBob, bob.SpaceId, connection.Id, request)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await asBob.Delete($"{Path(bob.SpaceId)}/{connection.Id}/model-settings?model=plain")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ListModels(asAlice, alice.SpaceId, connection.Id)).Single().Capabilities.Vision.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, null, "Enter a size of 1 token or more.")]
    [InlineData(null, -5.0, "Enter a price of zero or more per million tokens.")]
    [InlineData(null, 1.2345678, "Enter a price of zero or more per million tokens.")]
    public async Task Each_bad_value_gets_its_sentence(int? size, double? price, string sentence)
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateCompatible(client, alice.SpaceId);
        await SeedModels(alice, connection.Id, ("plain", new ModelCapabilities()));

        var refused = await Put(client, alice.SpaceId, connection.Id, new SetModelSettingsRequest(
            "plain",
            new ModelAbilitiesRequest(ContextSize: size),
            new ModelPricingRequest(InputPerMillion: (decimal?)price)));

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(refused)).Should().Be(sentence);
    }

    [Fact]
    public async Task A_currency_and_efforts_that_do_not_fit_are_refused_with_a_sentence()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateCompatible(client, alice.SpaceId);
        await SeedModels(alice, connection.Id, ("plain", new ModelCapabilities()));

        var badCurrency = await Put(client, alice.SpaceId, connection.Id, new SetModelSettingsRequest(
            "plain",
            null,
            new ModelPricingRequest(InputPerMillion: 1m, Currency: "DOLLARS")));
        var noReasoning = await Put(client, alice.SpaceId, connection.Id, new SetModelSettingsRequest(
            "plain",
            new ModelAbilitiesRequest(ReasoningEfforts: ["low"]),
            null));
        var badEffort = await Put(client, alice.SpaceId, connection.Id, new SetModelSettingsRequest(
            "plain",
            new ModelAbilitiesRequest(Reasoning: true, ReasoningEfforts: ["extreme"]),
            null));
        var noName = await Put(client, alice.SpaceId, connection.Id, new SetModelSettingsRequest(" ", null, null));

        (await Title(badCurrency)).Should().Be("Enter a three letter currency code, such as USD.");
        (await Title(noReasoning)).Should().Be("Choose effort levels only for a model that thinks before answering.");
        (await Title(badEffort)).Should().Be("Choose effort levels from minimal, low, medium, high, xhigh and max.");
        noName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ListModels(client, alice.SpaceId, connection.Id)).Single().OperatorSet.Should().BeEmpty("a refused request changes nothing");
    }

    [Fact]
    public async Task A_connection_that_holds_as_many_models_as_it_may_takes_no_new_row()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateCompatible(client, alice.SpaceId);
        var most = DeploymentPolicy.SelfHostedMaxModelsPerConnection;
        await SeedModels(alice, connection.Id, [.. Enumerable.Range(0, most).Select(index => ($"m{index}", new ModelCapabilities()))]);

        var refused = await Put(client, alice.SpaceId, connection.Id, new SetModelSettingsRequest("one-too-many", new ModelAbilitiesRequest(Vision: true), null));
        var existing = await Put(client, alice.SpaceId, connection.Id, new SetModelSettingsRequest("m0", new ModelAbilitiesRequest(Vision: true), null));

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        existing.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region What Reads It

    [Fact]
    public async Task A_bot_can_be_given_a_reasoning_effort_once_the_admin_says_the_model_thinks()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateCompatible(client, alice.SpaceId);
        await SeedModels(alice, connection.Id, ("plain", new ModelCapabilities()));
        var bot = await Read<BotSummary>(await client.Post($"{Space(alice.SpaceId)}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, "plain")));
        var edit = new UpdateBotRequest(
            null, null, null, null, null, null, null,
            new ModelSettingsBody(null, null, "high", "off", false), null, null, null, null, null);

        var before = await client.Put($"{Space(alice.SpaceId)}/bots/{bot.Id}/definition", edit);
        (await Put(client, alice.SpaceId, connection.Id, new SetModelSettingsRequest(
            "plain",
            new ModelAbilitiesRequest(Reasoning: true, ReasoningEfforts: ["low", "high"]),
            null))).EnsureSuccessStatusCode();
        var after = await client.Put($"{Space(alice.SpaceId)}/bots/{bot.Id}/definition", edit);

        before.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        after.StatusCode.Should().Be(HttpStatusCode.OK, await after.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task A_bot_can_be_given_an_effort_on_a_model_whose_server_reported_that_it_thinks_with_no_admin_step()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateCompatible(client, alice.SpaceId);
        // What detection stores for a model a routed list says supports reasoning.
        await SeedModels(alice, connection.Id, ("plain", new ModelCapabilities { Reasoning = true, ReasoningEfforts = ReasoningLevels.Documented }));
        var bot = await Read<BotSummary>(await client.Post($"{Space(alice.SpaceId)}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, "plain")));
        var edit = new UpdateBotRequest(
            null, null, null, null, null, null, null,
            new ModelSettingsBody(null, null, "high", "off", false), null, null, null, null, null);

        var saved = await client.Put($"{Space(alice.SpaceId)}/bots/{bot.Id}/definition", edit);

        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task Two_requests_that_add_the_same_new_model_both_succeed()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var other = await SignIn(alice);
        var connection = await CreateCompatible(client, alice.SpaceId);

        var first = Put(client, alice.SpaceId, connection.Id, new SetModelSettingsRequest("race/model", new ModelAbilitiesRequest(ContextSize: 1000), null));
        var second = Put(other, alice.SpaceId, connection.Id, new SetModelSettingsRequest("race/model", new ModelAbilitiesRequest(ContextSize: 2000), null));
        var answers = await Task.WhenAll(first, second);

        answers.Should().OnlyContain(answer => answer.StatusCode == HttpStatusCode.OK);
        (await ListModels(client, alice.SpaceId, connection.Id)).Should().ContainSingle(entry => entry.Model == "race/model");
    }

    [Fact]
    public async Task A_model_marked_as_reading_pictures_through_the_older_route_is_the_same_mark_as_the_sheets()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateCompatible(client, alice.SpaceId);
        await SeedModels(alice, connection.Id, ("plain", new ModelCapabilities()));

        (await client.Put($"{Path(alice.SpaceId)}/{connection.Id}/models/vision", new SetModelVisionRequest("plain", true))).EnsureSuccessStatusCode();

        var listed = (await ListModels(client, alice.SpaceId, connection.Id)).Single();
        (listed.Capabilities.Vision, listed.Capabilities.VisionSetByOperator).Should().Be((true, true));
        listed.OperatorSet.Should().BeEquivalentTo(["vision"]);
    }

    #endregion

    #region Audit

    [Fact]
    public async Task Each_put_and_delete_writes_an_audit_row_naming_the_connection_the_model_and_what_changed()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateCompatible(client, alice.SpaceId);
        await SeedModels(alice, connection.Id, ("plain", new ModelCapabilities()));

        (await Put(client, alice.SpaceId, connection.Id, new SetModelSettingsRequest(
            "plain",
            new ModelAbilitiesRequest(ContextSize: 8000),
            new ModelPricingRequest(InputPerMillion: 1.5m)))).EnsureSuccessStatusCode();
        (await client.Delete($"{Path(alice.SpaceId)}/{connection.Id}/model-settings?model=plain")).EnsureSuccessStatusCode();

        List<AuditLogEntry> entries = [];
        await InSpace(alice.SpaceId, alice.UserId, async services =>
            entries = await services.GetRequiredService<AuditRepository>().ListEntriesAbout(connection.Id, Token));
        entries.Should().HaveCount(2);
        entries.Should().OnlyContain(entry => entry.Action == "model.settings_changed" && entry.TargetKind == "model_connection");
        entries.Should().OnlyContain(entry => entry.ActorId == alice.UserId);
        entries[0].Data.GetProperty("model").GetString().Should().Be("plain");
        var set = entries[0].Data.GetProperty("changes");
        set.EnumerateObject().Select(change => change.Name).Should().BeEquivalentTo(["context_tokens", "input_per_million", "price_currency"]);
        set.GetProperty("context_tokens").GetProperty("after").GetInt32().Should().Be(8000);
        entries[1].Data.GetProperty("changes").GetProperty("context_tokens").GetProperty("before").GetInt32().Should().Be(8000);
    }

    #endregion

    #region Private Methods

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    private static string Path(Guid spaceId) => $"{Space(spaceId)}/model-connections";

    private async Task<ApiClient> SignIn((string Email, string Password, Guid UserId, Guid SpaceId) person)
    {
        var client = NewClient();
        (await client.SignIn(person.Email, person.Password)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        response.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created], await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private static async Task<string?> Title(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString();

    private static Task<HttpResponseMessage> Put(ApiClient client, Guid spaceId, Guid connectionId, SetModelSettingsRequest request) =>
        client.Put($"{Path(spaceId)}/{connectionId}/model-settings", request);

    private static async Task<List<ModelCatalogResponse>> ListModels(ApiClient client, Guid spaceId, Guid connectionId) =>
        await Read<List<ModelCatalogResponse>>(await client.Get($"{Path(spaceId)}/{connectionId}/models"));

    private static async Task<ModelConnectionSummary> CreateCompatible(ApiClient client, Guid spaceId) =>
        await Read<ModelConnectionSummary>(await client.Post(
            Path(spaceId),
            new CreateModelConnectionRequest(ModelConnectionKinds.Compatible, "Compatible", "https://models.example.test/v1", null, "plain")));

    private static async Task<ModelConnectionSummary> CreateScripted(ApiClient client, Guid spaceId) =>
        await Read<ModelConnectionSummary>(await client.Post(
            Path(spaceId),
            new CreateModelConnectionRequest(ScriptedProvider.ConnectionKind, "Scripted", null, null, null)));

    private async Task InSpace(Guid spaceId, Guid userId, Func<IServiceProvider, Task> work)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var transaction = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        await work(scope.ServiceProvider);
        await transaction.Commit(Token);
    }

    private async Task SeedModels(
        (string Email, string Password, Guid UserId, Guid SpaceId) person,
        Guid connectionId,
        params (string Model, ModelCapabilities Capabilities)[] models)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(person.SpaceId, person.UserId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var db = scope.ServiceProvider.GetRequiredService<DroverDbContext>();

        foreach (var (name, capabilities) in models)
        {
            db.ModelCatalog.Add(new ModelCatalogEntry
            {
                ModelConnectionId = connectionId,
                Model = name,
                DisplayName = name,
                Capabilities = capabilities,
                LastSeenAt = Time.GetUtcNow(),
            });
        }

        await db.SaveChangesAsync(Token);
        await work.Commit(Token);
    }

    #endregion
}
