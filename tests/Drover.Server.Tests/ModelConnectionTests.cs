namespace Drover.Server.Tests;

/// <summary>
/// The connection endpoints of a space, end to end over real PostgreSQL. The provider is scripted where a test needs
/// to control its answer; the compatible adapter is the real one where a test needs a real outbound request.
/// </summary>
public sealed class ModelConnectionTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private ScriptedProvider Scripted { get; } = new();

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(Scripted);

    #endregion

    #region Create And Key

    [Fact]
    public async Task Creating_a_connection_stores_the_key_in_the_vault_and_no_response_carries_it()
    {
        var alice = await SeedPerson("alice");
        var key = NewKey();
        var client = await SignIn(alice);

        var created = await client.Post(Path(alice.SpaceId), new CreateModelConnectionRequest(ScriptedProvider.ConnectionKind, "Scripted", null, key, null));
        var summary = (await created.Content.ReadFromJsonAsync<ModelConnectionSummary>(Token))!;
        var test = await client.Post($"{Path(alice.SpaceId)}/{summary.Id}/test");
        var list = await client.Get(Path(alice.SpaceId));
        var catalog = await client.Get($"{Path(alice.SpaceId)}/{summary.Id}/models");
        var refresh = await client.Post($"{Path(alice.SpaceId)}/{summary.Id}/models/refresh");
        var secrets = await client.Get($"/api/spaces/{alice.SpaceId}/secrets");

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        summary.HasKey.Should().BeTrue();
        summary.Status.Should().Be("pending");
        foreach (var response in new[] { created, test, list, catalog, refresh, secrets })
        {
            response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
            (await response.Content.ReadAsStringAsync(Token)).Should().NotContain(key);
        }

        Scripted.KeysSeen.Should().Contain(key, "the adapter gets the key at the moment of use");
        (await RevealStoredKey(alice, summary.Id)).Should().Be(key);
        Logs.AllText().Should().NotContain(key);
        var kinds = await ReadSecretKinds(alice);
        kinds.Should().ContainSingle().Which.Should().Be(SecretKind.ModelKey);
    }

    [Fact]
    public async Task Replacing_the_key_replaces_the_stored_secret_and_returns_the_connection_to_pending()
    {
        var alice = await SeedPerson("alice");
        var first = NewKey();
        var second = NewKey();
        var client = await SignIn(alice);
        var summary = await CreateScripted(client, alice, first);
        (await client.Post($"{Path(alice.SpaceId)}/{summary.Id}/test")).EnsureSuccessStatusCode();

        var replaced = await client.Put($"{Path(alice.SpaceId)}/{summary.Id}/key", new UpdateModelKeyRequest(second));

        replaced.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replaced.Content.ReadAsStringAsync(Token)).Should().NotContain(second).And.NotContain(first);
        var after = (await replaced.Content.ReadFromJsonAsync<ModelConnectionSummary>(Token))!;
        after.Status.Should().Be("pending");
        after.LastCheckedAt.Should().BeNull();
        (await RevealStoredKey(alice, summary.Id)).Should().Be(second);
        (await ReadSecretKinds(alice)).Should().HaveCount(1, "the secret is replaced, not added to");
        Logs.AllText().Should().NotContain(first).And.NotContain(second);
    }

    [Fact]
    public async Task A_connection_made_without_a_key_has_none_and_the_adapter_is_given_none()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);

        var summary = await CreateScripted(client, alice, apiKey: null);
        (await client.Post($"{Path(alice.SpaceId)}/{summary.Id}/test")).EnsureSuccessStatusCode();

        summary.HasKey.Should().BeFalse();
        Scripted.KeysSeen.Should().ContainSingle().Which.Should().BeNull();
    }

    [Theory]
    [InlineData("unknown_kind", "Name", null, null)]
    [InlineData("scripted", "", null, null)]
    [InlineData("scripted", "Name", null, "a key with spaces")]
    [InlineData("openai_compatible", "Name", null, null)]
    [InlineData("openai_compatible", "Name", "ftp://models.example.test/v1", null)]
    [InlineData("openai_compatible", "Name", "https://user:pass@models.example.test/v1", null)]
    [InlineData("openai_compatible", "Name", "not an address", null)]
    public async Task A_connection_that_cannot_work_is_refused_without_echoing_the_key(
        string kind,
        string name,
        string? baseUrl,
        string? apiKey)
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);

        var response = await client.Post(Path(alice.SpaceId), new CreateModelConnectionRequest(kind, name, baseUrl, apiKey, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var text = await response.Content.ReadAsStringAsync(Token);
        text.Should().NotContain("key with spaces");
        (await ReadConnections(client, alice.SpaceId)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_state_changing_request_without_the_antiforgery_token_is_refused()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);

        var response = await client.PostWithoutToken(Path(alice.SpaceId), new CreateModelConnectionRequest(ScriptedProvider.ConnectionKind, "x", null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Roles And Spaces

    [Fact]
    public async Task Members_list_connections_and_only_admins_change_them()
    {
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var admin = await SeedMember(alice.SpaceId, "admin", MembershipRole.Admin);
        var asAdmin = NewClient();
        await asAdmin.SignIn(admin.Email, admin.Password);
        var summary = await CreateScripted(asAdmin, alice.SpaceId);
        var asMember = NewClient();
        await asMember.SignIn(member.Email, member.Password);
        var path = $"{Path(alice.SpaceId)}/{summary.Id}";

        (await asMember.Get(Path(alice.SpaceId))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await asMember.Get($"{path}/models")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await asMember.Post(Path(alice.SpaceId), new CreateModelConnectionRequest(ScriptedProvider.ConnectionKind, "x", null, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await asMember.Put($"{path}/key", new UpdateModelKeyRequest(NewKey()))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await asMember.Post($"{path}/test")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await asMember.Post($"{path}/models/refresh")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await asAdmin.Post($"{path}/test")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_connection_of_another_space_is_not_found_from_any_route()
    {
        var alice = await SeedPerson("alice");
        var bob = await SeedPerson("bob");
        var aliceClient = await SignIn(alice);
        var bobClient = await SignIn(bob);
        var bobs = await CreateScripted(bobClient, bob.SpaceId);
        var path = $"{Path(alice.SpaceId)}/{bobs.Id}";

        (await aliceClient.Get($"{path}/models")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await aliceClient.Put($"{path}/key", new UpdateModelKeyRequest(NewKey()))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await aliceClient.Post($"{path}/test")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await aliceClient.Post($"{path}/models/refresh")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await aliceClient.Get($"{Path(bob.SpaceId)}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadConnections(aliceClient, alice.SpaceId)).Should().BeEmpty();
    }

    #endregion

    #region Test And Balances

    [Fact]
    public async Task A_good_test_marks_the_connection_ready_and_records_the_balances()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var summary = await CreateScripted(client, alice.SpaceId);
        Scripted.OnTest = _ => Task.FromResult(ConnectionTestResult.Ok("Connected.", [new ModelBalance("usd", 12.5m), new ModelBalance("diem", 3m)]));

        var response = await client.Post($"{Path(alice.SpaceId)}/{summary.Id}/test");

        var result = (await response.Content.ReadFromJsonAsync<ConnectionTestResponse>(Token))!;
        result.Succeeded.Should().BeTrue();
        result.Connection.Status.Should().Be("ready");
        result.Connection.Balances.Should().BeEquivalentTo([new BalanceAmount("usd", 12.5m), new BalanceAmount("diem", 3m)]);
        var listed = (await (await client.Get(Path(alice.SpaceId))).Content.ReadFromJsonAsync<List<ModelConnectionSummary>>(Token))!;
        listed.Single().Balances.Should().HaveCount(2);
        listed.Single().LastCheckedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Balances_a_reply_reports_are_recorded_on_the_connection()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var summary = await CreateScripted(client, alice.SpaceId);

        await using (var scope = Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(alice.SpaceId, alice.UserId);
            await scope.ServiceProvider.GetRequiredService<ModelConnectionService>()
                .RecordBalances(summary.Id, [new ModelBalance("usd", 4.25m)], Token);
        }

        var listed = (await (await client.Get(Path(alice.SpaceId))).Content.ReadFromJsonAsync<List<ModelConnectionSummary>>(Token))!;
        listed.Single().Balances.Should().ContainSingle().Which.Should().Be(new BalanceAmount("usd", 4.25m));
        listed.Single().BalanceReadAt.Should().NotBeNull();
    }

    [Fact]
    public async Task A_rejected_key_marks_the_connection_failed_with_the_setup_message()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var summary = await CreateScripted(client, alice.SpaceId);
        var invalid = ModelProviderException.FromStatus(401);
        Scripted.OnTest = _ => Task.FromResult(ConnectionTestResult.Failed(invalid.Failure, invalid.Message));

        var result = (await (await client.Post($"{Path(alice.SpaceId)}/{summary.Id}/test")).Content.ReadFromJsonAsync<ConnectionTestResponse>(Token))!;

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be("invalid_key");
        result.Message.Should().Be(invalid.Message);
        result.Connection.Status.Should().Be("failed");
    }

    [Fact]
    public async Task A_rate_limited_test_leaves_the_status_alone()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var summary = await CreateScripted(client, alice.SpaceId);
        var limited = ModelProviderException.FromStatus(429);
        Scripted.OnTest = _ => Task.FromResult(ConnectionTestResult.Failed(limited.Failure, limited.Message));

        var result = (await (await client.Post($"{Path(alice.SpaceId)}/{summary.Id}/test")).Content.ReadFromJsonAsync<ConnectionTestResponse>(Token))!;

        result.Failure.Should().Be("retry_later");
        result.Connection.Status.Should().Be("pending");
    }

    [Fact]
    public async Task No_tenant_transaction_is_open_while_the_provider_answers()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var summary = await CreateScripted(client, alice.SpaceId);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Scripted.OnTest = async _ =>
        {
            entered.SetResult();
            await release.Task;
            return ConnectionTestResult.Ok("Connected.");
        };

        var call = client.Post($"{Path(alice.SpaceId)}/{summary.Id}/test");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        var open = await CountOpenTransactions();
        release.SetResult();
        var response = await call;

        open.Should().Be(0, "a scope is never held across a provider call");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_answer_for_a_key_that_was_replaced_meanwhile_is_not_recorded()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var summary = await CreateScripted(client, alice.SpaceId, NewKey());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Scripted.OnTest = async _ =>
        {
            entered.SetResult();
            await release.Task;
            return ConnectionTestResult.Ok("Connected.");
        };

        var call = client.Post($"{Path(alice.SpaceId)}/{summary.Id}/test");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        var other = await SignIn(alice);
        Scripted.OnTest = _ => Task.FromResult(ConnectionTestResult.Ok("Connected."));
        (await other.Put($"{Path(alice.SpaceId)}/{summary.Id}/key", new UpdateModelKeyRequest(NewKey()))).EnsureSuccessStatusCode();
        release.SetResult();
        await call;

        var current = (await (await client.Get(Path(alice.SpaceId))).Content.ReadFromJsonAsync<List<ModelConnectionSummary>>(Token))!.Single();
        current.Status.Should().Be("pending");
        current.LastCheckedAt.Should().BeNull();
    }

    #endregion

    #region Model Catalog

    [Fact]
    public async Task Refreshing_upserts_the_catalog_and_marks_a_model_that_disappears_as_missing()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var summary = await CreateScripted(client, alice.SpaceId);
        var path = $"{Path(alice.SpaceId)}/{summary.Id}/models";
        Scripted.OnList = _ => Task.FromResult<IReadOnlyList<ModelInfo>>(
        [
            Model("alpha", 1.5m),
            Model("beta", 2m),
        ]);

        var first = (await (await client.Post($"{path}/refresh")).Content.ReadFromJsonAsync<ModelRefreshResponse>(Token))!;
        Scripted.OnList = _ => Task.FromResult<IReadOnlyList<ModelInfo>>([Model("alpha", 1.75m), Model("gamma", 3m)]);
        var second = (await (await client.Post($"{path}/refresh")).Content.ReadFromJsonAsync<ModelRefreshResponse>(Token))!;
        var stored = (await (await client.Get(path)).Content.ReadFromJsonAsync<List<ModelCatalogResponse>>(Token))!;

        first.Succeeded.Should().BeTrue();
        first.Models.Select(model => model.Model).Should().BeEquivalentTo(["alpha", "beta"]);
        second.Models.Should().HaveCount(3);
        stored.Single(model => model.Model == "alpha").Price.InputPerMillion.Should().Be(1.75m);
        stored.Single(model => model.Model == "alpha").Capabilities.Reasoning.Should().BeTrue();
        stored.Single(model => model.Model == "alpha").PrivacyMode.Should().Be("private");
        stored.Single(model => model.Model == "alpha").Traits.Should().Contain("default");
        stored.Single(model => model.Model == "beta").State.Should().Be("missing");
        stored.Single(model => model.Model == "gamma").State.Should().Be("available");
    }

    [Fact]
    public async Task A_refresh_the_provider_cannot_answer_keeps_the_stored_catalog_and_says_why()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var summary = await CreateScripted(client, alice.SpaceId);
        var path = $"{Path(alice.SpaceId)}/{summary.Id}/models";
        Scripted.OnList = _ => Task.FromResult<IReadOnlyList<ModelInfo>>([Model("alpha", 1m)]);
        (await client.Post($"{path}/refresh")).EnsureSuccessStatusCode();
        Scripted.OnList = _ => throw ModelProviderException.FromStatus(401);

        var failed = (await (await client.Post($"{path}/refresh")).Content.ReadFromJsonAsync<ModelRefreshResponse>(Token))!;

        failed.Succeeded.Should().BeFalse();
        failed.Failure.Should().Be("invalid_key");
        failed.Message.Should().Contain("rejected the API key");
        failed.Models.Should().ContainSingle().Which.State.Should().Be("available");
    }

    #endregion

    #region Helpers

    private static string Path(Guid spaceId) => $"/api/spaces/{spaceId}/model-connections";

    private static string NewKey() => $"test-key-{Convert.ToHexString(RandomNumberGenerator.GetBytes(12))}";

    private static ModelInfo Model(string id, decimal price) =>
        new(id, $"Model {id}", new ModelCapabilities { Reasoning = true, ReasoningEfforts = ["low", "high"] }, ["default"])
        {
            Pricing = new ModelPricing { InputPerMillion = price, OutputPerMillion = price * 2 },
            Privacy = PrivacyMode.Private,
        };

    private async Task<ApiClient> SignIn((string Email, string Password, Guid UserId, Guid SpaceId) person)
    {
        var client = NewClient();
        (await client.SignIn(person.Email, person.Password)).EnsureSuccessStatusCode();
        return client;
    }

    private Task<ModelConnectionSummary> CreateScripted(ApiClient client, (string Email, string Password, Guid UserId, Guid SpaceId) person, string? apiKey) =>
        CreateScripted(client, person.SpaceId, apiKey);

    private Task<ModelConnectionSummary> CreateScripted(ApiClient client, Guid spaceId) => CreateScripted(client, spaceId, null);

    private static async Task<ModelConnectionSummary> CreateScripted(ApiClient client, Guid spaceId, string? apiKey)
    {
        var response = await client.Post(Path(spaceId), new CreateModelConnectionRequest(ScriptedProvider.ConnectionKind, "Scripted", null, apiKey, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ModelConnectionSummary>(Token))!;
    }

    private async Task<string> RevealStoredKey((string Email, string Password, Guid UserId, Guid SpaceId) person, Guid connectionId)
    {
        await using var scope = Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        provider.GetRequiredService<TenantContext>().Set(person.SpaceId, person.UserId);
        await using var work = await provider.GetRequiredService<TenantScope>().Begin(Token);
        var connection = await provider.GetRequiredService<DroverDbContext>().ModelConnections.SingleAsync(row => row.Id == connectionId, Token);
        var key = await provider.GetRequiredService<ISecretVault>().Reveal(connection.SecretId!.Value, Token);
        await work.Commit(Token);
        return key;
    }

    private async Task<List<SecretKind>> ReadSecretKinds((string Email, string Password, Guid UserId, Guid SpaceId) person)
    {
        await using var scope = Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        provider.GetRequiredService<TenantContext>().Set(person.SpaceId, person.UserId);
        await using var work = await provider.GetRequiredService<TenantScope>().Begin(Token);
        var kinds = await provider.GetRequiredService<DroverDbContext>().Secrets.Select(secret => secret.Kind).ToListAsync(Token);
        await work.Commit(Token);
        return kinds;
    }

    private static async Task<List<ModelConnectionSummary>> ReadConnections(ApiClient client, Guid spaceId) =>
        (await (await client.Get(Path(spaceId))).Content.ReadFromJsonAsync<List<ModelConnectionSummary>>(Token))!;

    private async Task<long> CountOpenTransactions()
    {
        await using var connection = new NpgsqlConnection(AppConnectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND pid <> pg_backend_pid() AND state LIKE 'idle in transaction%'",
            connection);
        return (long)(await command.ExecuteScalarAsync(Token))!;
    }

    #endregion
}
