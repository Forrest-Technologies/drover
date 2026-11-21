namespace Drover.Server.Tests;

/// <summary>
/// The sandbox secrets API over real PostgreSQL: a value goes in once and never comes back, in any response, log line,
/// or audit row; who may manage secrets and who may read a bot's names; what is refused at save; and that scope, rotation
/// and removal do what they say. Every value here is generated for the test.
/// </summary>
public sealed class SandboxSecretApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    private ScriptedModelProvider? model;

    #endregion

    #region Overrides

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        [$"Drover:Policy:Rules:{nameof(DeploymentPolicy.MaxSandboxSecretsPerSpace)}"] = "4",
    };

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(new ScriptedModel()));

    #endregion

    #region Write Only

    [Fact]
    public async Task A_value_is_stored_in_the_vault_and_never_returned_logged_or_audited()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var botId = await NewBot(client, alice.SpaceId);
        var value = NewValue();
        var bodies = new List<string>();

        var created = await client.Post(Secrets(alice.SpaceId), Brokered("DEPLOY_TOKEN", value, "bots", [botId]));
        bodies.Add(await created.Content.ReadAsStringAsync(Token));
        var secret = JsonSerializer.Deserialize<SandboxSecretResponse>(bodies[0], json)!;
        bodies.Add(await (await client.Get(Secrets(alice.SpaceId))).Content.ReadAsStringAsync(Token));
        bodies.Add(await (await client.Get($"{Secrets(alice.SpaceId)}/{secret.Id}/history")).Content.ReadAsStringAsync(Token));
        bodies.Add(await (await client.Get($"{Space(alice.SpaceId)}/bots/{botId}/sandbox/secrets")).Content.ReadAsStringAsync(Token));
        bodies.Add(await (await client.Get($"{Space(alice.SpaceId)}/secrets")).Content.ReadAsStringAsync(Token));

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        secret.Should().Match<SandboxSecretResponse>(found => found.Name == "DEPLOY_TOKEN" && found.Mode == "brokered" && found.Scope == "bots");
        secret.Hosts.Should().Equal("api.example.test");
        secret.Bots.Should().ContainSingle().Which.Id.Should().Be(botId);
        (await Reveal(alice, secret.Id)).Should().Be(value);
        (await StoredKinds(alice)).Should().Contain(SecretKind.SandboxEnvironment);

        foreach (var form in Forms(value))
        {
            bodies.Should().AllSatisfy(body => body.Should().NotContain(form));
            Logs.AllText().Should().NotContain(form);
            (await AuditText(alice)).Should().NotContain(form);
        }
    }

    [Fact]
    public async Task Rotation_keeps_the_secret_and_replaces_its_value()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var first = NewValue();
        var second = NewValue();
        var secret = await Send<SandboxSecretResponse>(client.Post(Secrets(alice.SpaceId), Brokered("API_KEY", first, "space")), HttpStatusCode.Created);

        var rotated = await Send<SandboxSecretResponse>(
            client.Put($"{Secrets(alice.SpaceId)}/{secret.Id}/value", new RotateSandboxSecretRequest(second)),
            HttpStatusCode.OK);

        rotated.Id.Should().Be(secret.Id);
        rotated.RotatedAt.Should().NotBeNull();
        (await Reveal(alice, secret.Id)).Should().Be(second);
        Logs.AllText().Should().NotContain(first).And.NotContain(second);
        (await AuditText(alice)).Should().NotContain(first).And.NotContain(second);
    }

    [Fact]
    public async Task Removing_a_secret_removes_its_value_and_the_history_records_every_change()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var botId = await NewBot(client, alice.SpaceId);
        var secret = await Send<SandboxSecretResponse>(client.Post(Secrets(alice.SpaceId), Brokered("API_KEY", NewValue(), "space")), HttpStatusCode.Created);
        var path = $"{Secrets(alice.SpaceId)}/{secret.Id}";

        await Send<SandboxSecretResponse>(client.Put($"{path}/value", new RotateSandboxSecretRequest(NewValue())), HttpStatusCode.OK);
        await Send<SandboxSecretResponse>(client.Put($"{path}/scope", new ChangeSandboxSecretScopeRequest("bots", [botId], null)), HttpStatusCode.OK);
        await Send<SandboxSecretResponse>(client.Put($"{path}/binding", new ChangeSandboxSecretBindingRequest("brokered", ["*.example.test"], true, false)), HttpStatusCode.OK);
        await Send<SandboxSecretResponse>(client.Put($"{path}/description", new ChangeSandboxSecretDescriptionRequest("For deploys")), HttpStatusCode.OK);
        var history = await Send<List<SandboxSecretHistoryEntry>>(client.Get($"{path}/history"), HttpStatusCode.OK);
        var removed = await client.Delete(path);
        var again = await client.Delete(path);

        history.Select(entry => entry.Action).Should().Equal(
            "sandbox_secret.description_changed",
            "sandbox_secret.binding_changed",
            "sandbox_secret.scope_changed",
            "sandbox_secret.rotated",
            "sandbox_secret.created");
        history.Should().AllSatisfy(entry => entry.Should().Match<SandboxSecretHistoryEntry>(found => found.ActorKind == "user" && found.ActorId == alice.UserId && found.ActorName != null));
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        again.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Send<List<SandboxSecretResponse>>(client.Get(Secrets(alice.SpaceId)), HttpStatusCode.OK)).Should().BeEmpty();
        (await StoredKinds(alice)).Should().NotContain(SecretKind.SandboxEnvironment, "the value goes with the secret");
        (await AuditText(alice)).Should().Contain("sandbox_secret.deleted");
    }

    #endregion

    #region Who May

    [Fact]
    public async Task Only_owners_and_admins_manage_secrets_and_a_member_reads_only_a_bots_names()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var botId = await NewBot(client, alice.SpaceId);
        var secret = await Send<SandboxSecretResponse>(client.Post(Secrets(alice.SpaceId), Brokered("API_KEY", NewValue(), "space")), HttpStatusCode.Created);
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var admin = await SeedMember(alice.SpaceId, "admin", MembershipRole.Admin);
        var asMember = await SignIn(member.Email, member.Password);
        var asAdmin = await SignIn(admin.Email, admin.Password);
        var bob = await SeedPerson("bob");
        var asBob = await SignIn(bob.Email, bob.Password);

        (await asMember.Get(Secrets(alice.SpaceId))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await asMember.Post(Secrets(alice.SpaceId), Brokered("OTHER", NewValue(), "space"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await asMember.Put($"{Secrets(alice.SpaceId)}/{secret.Id}/value", new RotateSandboxSecretRequest(NewValue()))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await asMember.Delete($"{Secrets(alice.SpaceId)}/{secret.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await asMember.Get($"{Secrets(alice.SpaceId)}/{secret.Id}/history")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Send<List<BotSandboxSecretName>>(asMember.Get($"{Space(alice.SpaceId)}/bots/{botId}/sandbox/secrets"), HttpStatusCode.OK))
            .Should().ContainSingle().Which.Name.Should().Be("API_KEY");
        (await Send<List<SandboxSecretResponse>>(asAdmin.Get(Secrets(alice.SpaceId)), HttpStatusCode.OK)).Should().ContainSingle();
        (await asBob.Get(Secrets(alice.SpaceId))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await asBob.Delete($"{Secrets(bob.SpaceId)}/{secret.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "another space's secret is not found");
    }

    #endregion

    #region Refused

    public static TheoryData<string, CreateSandboxSecretRequest, string> RefusedSecrets() => new()
    {
        { "lowercase name", Brokered("deploy_token", "a-value-for-the-test", "space"), "capital letters" },
        { "reserved name", Brokered("PATH", "a-value-for-the-test", "space"), "reserved" },
        { "reserved prefix", Brokered("LD_PRELOAD", "a-value-for-the-test", "space"), "reserved" },
        { "proxy name", Brokered("HTTPS_PROXY", "a-value-for-the-test", "space"), "reserved" },
        { "no value", Brokered("API_KEY", string.Empty, "space"), "Paste the value" },
        { "value too long", Brokered("API_KEY", new string('a', SandboxSecretRules.MaxValueCharacters + 1), "space"), "at most" },
        { "brokered without a host", Brokered("API_KEY", "a-value-for-the-test", "space") with { Hosts = [] }, "at least one host" },
        { "host with a scheme", Brokered("API_KEY", "a-value-for-the-test", "space") with { Hosts = ["https://api.example.test"] }, "not a host name" },
        { "host as an address", Brokered("API_KEY", "a-value-for-the-test", "space") with { Hosts = ["10.0.0.1"] }, "is an address" },
        { "raw without confirming", Raw("SIGNING_KEY", "a-value-for-the-test") with { ConfirmRawMode = false }, "must be confirmed" },
        { "raw with hosts", Raw("SIGNING_KEY", "a-value-for-the-test") with { Hosts = ["api.example.test"] }, "takes no hosts" },
        { "host on a private network", Brokered("API_KEY", "a-value-for-the-test", "space") with { Hosts = ["vault.internal"] }, "private network" },
        { "wildcard on a private network", Brokered("API_KEY", "a-value-for-the-test", "space") with { Hosts = ["*.svc.cluster.local"] }, "private network" },
        { "proxy settings name", Brokered("GOPROXY", "a-value-for-the-test", "space"), "reserved" },
        { "unknown mode", Brokered("API_KEY", "a-value-for-the-test", "space") with { Mode = "plain" }, "through the broker" },
        { "unknown scope", Brokered("API_KEY", "a-value-for-the-test", "everyone"), "every bot, chosen bots, or a project" },
        { "chosen bots without any", Brokered("API_KEY", "a-value-for-the-test", "bots"), "at least one bot" },
        { "a bot from nowhere", Brokered("API_KEY", "a-value-for-the-test", "bots", [Guid.CreateVersion7()]), "from this space" },
        { "a project from nowhere", Brokered("API_KEY", "a-value-for-the-test", "project") with { ProjectId = Guid.CreateVersion7() }, "project from this space" },
    };

    [Theory]
    [MemberData(nameof(RefusedSecrets))]
    public async Task A_secret_that_cannot_be_kept_safely_is_refused_with_a_sentence(string what, CreateSandboxSecretRequest request, string expected)
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);

        var refused = await client.Post(Secrets(alice.SpaceId), request);

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest, what);
        (await Title(refused)).Should().Contain(expected, what);
        (await StoredKinds(alice)).Should().NotContain(SecretKind.SandboxEnvironment, what);
    }

    [Fact]
    public async Task A_raw_value_cannot_be_given_to_a_project_and_a_switch_to_raw_must_be_confirmed()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var project = await Send<ProjectSummary>(client.Post($"{Space(alice.SpaceId)}/projects", new CreateProjectRequest("Launch")), HttpStatusCode.Created);
        var forProject = await Send<SandboxSecretResponse>(
            client.Post(Secrets(alice.SpaceId), Brokered("API_KEY", NewValue(), "project") with { ProjectId = project.Id }),
            HttpStatusCode.Created);
        var forSpace = await Send<SandboxSecretResponse>(client.Post(Secrets(alice.SpaceId), Brokered("OTHER_KEY", NewValue(), "space")), HttpStatusCode.Created);

        var rawProject = await client.Post(Secrets(alice.SpaceId), Raw("SIGNING_KEY", NewValue()) with { Scope = "project", ProjectId = project.Id });
        var projectToRaw = await client.Put($"{Secrets(alice.SpaceId)}/{forProject.Id}/binding", new ChangeSandboxSecretBindingRequest("raw", [], false, false, ConfirmRawMode: true));
        var unconfirmed = await client.Put($"{Secrets(alice.SpaceId)}/{forSpace.Id}/binding", new ChangeSandboxSecretBindingRequest("raw", [], false, false));
        var confirmed = await Send<SandboxSecretResponse>(
            client.Put($"{Secrets(alice.SpaceId)}/{forSpace.Id}/binding", new ChangeSandboxSecretBindingRequest("raw", [], true, true, ConfirmRawMode: true)),
            HttpStatusCode.OK);

        forProject.ProjectName.Should().Be("Launch");
        (await Title(rawProject)).Should().Contain("must go through the broker");
        (await Title(projectToRaw)).Should().Contain("must go through the broker");
        (await Title(unconfirmed)).Should().Contain("must be confirmed");
        confirmed.Should().Match<SandboxSecretResponse>(found => found.Mode == "raw" && found.Hosts.Count == 0 && !found.AllowInQuery && !found.AllowInBody);
    }

    [Fact]
    public async Task Giving_a_value_kept_in_the_sandbox_to_more_bots_must_be_confirmed_again()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var atlas = await NewBot(client, alice.SpaceId, "Atlas");
        var scout = await NewBot(client, alice.SpaceId, "Scout");
        var secret = await Send<SandboxSecretResponse>(
            client.Post(Secrets(alice.SpaceId), Raw("SIGNING_KEY", NewValue()) with { Scope = "bots", BotIds = [atlas, scout] }),
            HttpStatusCode.Created);
        var path = $"{Secrets(alice.SpaceId)}/{secret.Id}/scope";

        var narrowed = await client.Put(path, new ChangeSandboxSecretScopeRequest("bots", [atlas], null));
        var widenedToScout = await client.Put(path, new ChangeSandboxSecretScopeRequest("bots", [atlas, scout], null));
        var widenedToAll = await client.Put(path, new ChangeSandboxSecretScopeRequest("space", null, null));
        var confirmed = await client.Put(path, new ChangeSandboxSecretScopeRequest("space", null, null, ConfirmRawMode: true));

        narrowed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Title(widenedToScout)).Should().Contain("must be confirmed");
        (await Title(widenedToAll)).Should().Contain("must be confirmed");
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_name_has_one_value_per_bot_per_project_and_for_every_bot()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var atlas = await NewBot(client, alice.SpaceId, "Atlas");
        var scout = await NewBot(client, alice.SpaceId, "Scout");
        await Send<SandboxSecretResponse>(client.Post(Secrets(alice.SpaceId), Brokered("API_KEY", NewValue(), "space")), HttpStatusCode.Created);
        await Send<SandboxSecretResponse>(client.Post(Secrets(alice.SpaceId), Brokered("API_KEY", NewValue(), "bots", [atlas])), HttpStatusCode.Created);

        var secondForEveryone = await client.Post(Secrets(alice.SpaceId), Brokered("API_KEY", NewValue(), "space"));
        var secondForAtlas = await client.Post(Secrets(alice.SpaceId), Brokered("API_KEY", NewValue(), "bots", [atlas, scout]));
        var scoutOnly = await client.Post(Secrets(alice.SpaceId), Brokered("API_KEY", NewValue(), "bots", [scout]));

        (await Title(secondForEveryone)).Should().Contain("already called API_KEY for every bot");
        (await Title(secondForAtlas)).Should().Contain("already has a secret called API_KEY");
        scoutOnly.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_space_holds_at_most_the_number_of_secrets_the_server_allows()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);

        foreach (var name in new[] { "FIRST_KEY", "SECOND_KEY", "THIRD_KEY", "FOURTH_KEY" })
        {
            await Send<SandboxSecretResponse>(client.Post(Secrets(alice.SpaceId), Brokered(name, NewValue(), "space")), HttpStatusCode.Created);
        }

        var fifth = await client.Post(Secrets(alice.SpaceId), Brokered("FIFTH_KEY", NewValue(), "space"));

        fifth.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(fifth)).Should().Be("A space on this server holds at most 4 sandbox secrets. Remove one first.");
    }

    [Fact]
    public async Task A_body_past_the_limit_is_refused_before_it_is_read()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);

        var body = JsonSerializer.Serialize(Brokered("API_KEY", new string('a', 140 * 1024), "space"), JsonSerializerOptions.Web);

        var huge = await client.PostContent(Secrets(alice.SpaceId), new StringContent(body, Encoding.UTF8, "application/json"));

        huge.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    #endregion

    #region Bot Names

    [Fact]
    public async Task A_bot_is_given_the_narrowest_value_of_each_name_and_project_names_with_their_project()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var atlas = await NewBot(client, alice.SpaceId, "Atlas");
        var scout = await NewBot(client, alice.SpaceId, "Scout");
        var project = await Send<ProjectSummary>(client.Post($"{Space(alice.SpaceId)}/projects", new CreateProjectRequest("Launch")), HttpStatusCode.Created);
        await Send<SandboxSecretResponse>(client.Post(Secrets(alice.SpaceId), Brokered("API_KEY", NewValue(), "space") with { Description = "Shared" }), HttpStatusCode.Created);
        await Send<SandboxSecretResponse>(client.Post(Secrets(alice.SpaceId), Brokered("API_KEY", NewValue(), "bots", [atlas]) with { Description = "Atlas only" }), HttpStatusCode.Created);
        await Send<SandboxSecretResponse>(client.Post(Secrets(alice.SpaceId), Raw("SIGNING_KEY", NewValue()) with { Scope = "bots", BotIds = [scout] }), HttpStatusCode.Created);
        await Send<SandboxSecretResponse>(client.Post(Secrets(alice.SpaceId), Brokered("LAUNCH_KEY", NewValue(), "project") with { ProjectId = project.Id }), HttpStatusCode.Created);

        var forAtlas = await Send<List<BotSandboxSecretName>>(client.Get($"{Space(alice.SpaceId)}/bots/{atlas}/sandbox/secrets"), HttpStatusCode.OK);
        var forScout = await Send<List<BotSandboxSecretName>>(client.Get($"{Space(alice.SpaceId)}/bots/{scout}/sandbox/secrets"), HttpStatusCode.OK);
        var nobody = await client.Get($"{Space(alice.SpaceId)}/bots/{Guid.CreateVersion7()}/sandbox/secrets");

        forAtlas.Select(name => (name.Name, name.Description, name.From)).Should().BeEquivalentTo(
            [("API_KEY", "Atlas only", "bot"), ("LAUNCH_KEY", string.Empty, "project")]);
        forAtlas.Single(name => name.Name == "LAUNCH_KEY").ProjectName.Should().Be("Launch");
        forScout.Select(name => (name.Name, name.Mode, name.From)).Should().BeEquivalentTo(
            [("API_KEY", "brokered", "space"), ("SIGNING_KEY", "raw", "bot"), ("LAUNCH_KEY", "brokered", "project")]);
        nobody.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Removing_a_project_removes_its_secrets_and_their_values()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var project = await Send<ProjectSummary>(client.Post($"{Space(alice.SpaceId)}/projects", new CreateProjectRequest("Launch")), HttpStatusCode.Created);
        await Send<SandboxSecretResponse>(client.Post(Secrets(alice.SpaceId), Brokered("LAUNCH_KEY", NewValue(), "project") with { ProjectId = project.Id }), HttpStatusCode.Created);

        (await client.Delete($"{Space(alice.SpaceId)}/projects/{project.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Send<List<SandboxSecretResponse>>(client.Get(Secrets(alice.SpaceId)), HttpStatusCode.OK)).Should().BeEmpty();
        (await StoredKinds(alice)).Should().NotContain(SecretKind.SandboxEnvironment);
    }

    #endregion

    #region Helpers

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    private static string Secrets(Guid spaceId) => $"{Space(spaceId)}/sandbox-secrets";

    private static string NewValue() => $"test-{Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant()}";

    /// <summary>The value and the encodings a leak would most likely take.</summary>
    private static IEnumerable<string> Forms(string value) =>
    [
        value,
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value)),
        Convert.ToHexString(Encoding.UTF8.GetBytes(value)),
        Convert.ToHexString(Encoding.UTF8.GetBytes(value)).ToLowerInvariant(),
    ];

    private static CreateSandboxSecretRequest Brokered(string name, string value, string scope, IReadOnlyList<Guid>? botIds = null) =>
        new(name, null, value, "brokered", ["api.example.test"], false, false, scope, botIds, null);

    private static CreateSandboxSecretRequest Raw(string name, string value) =>
        new(name, null, value, "raw", [], false, false, "space", null, null, ConfirmRawMode: true);

    private static async Task<string?> Title(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString();

    private static async Task<T> Send<T>(Task<HttpResponseMessage> request, HttpStatusCode expected)
    {
        var response = await request;
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private async Task<ApiClient> SignIn(string email, string password)
    {
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<Guid> NewBot(ApiClient client, Guid spaceId, string name = "Helper")
    {
        var connection = await Send<ModelConnectionSummary>(
            client.Post($"{Space(spaceId)}/model-connections", new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, $"For {name}", null, null, "scripted")),
            HttpStatusCode.Created);
        var bot = await Send<BotSummary>(client.Post($"{Space(spaceId)}/bots", new CreateBotRequest(name, "Answer briefly.", connection.Id, null)), HttpStatusCode.Created);
        return bot.Id;
    }

    private async Task<T> InSpace<T>((string Email, string Password, Guid UserId, Guid SpaceId) person, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(person.SpaceId, person.UserId);
        await using var transaction = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var result = await work(scope.ServiceProvider);
        await transaction.Commit(Token);
        return result;
    }

    private Task<string> Reveal((string Email, string Password, Guid UserId, Guid SpaceId) person, Guid sandboxSecretId) =>
        InSpace(person, async provider =>
        {
            var row = await provider.GetRequiredService<DroverDbContext>().SandboxSecrets.SingleAsync(found => found.Id == sandboxSecretId, Token);
            return await provider.GetRequiredService<ISecretVault>().Reveal(row.SecretId, Token);
        });

    private Task<List<SecretKind>> StoredKinds((string Email, string Password, Guid UserId, Guid SpaceId) person) =>
        InSpace(person, provider => provider.GetRequiredService<DroverDbContext>().Secrets.Select(secret => secret.Kind).ToListAsync(Token));

    private Task<string> AuditText((string Email, string Password, Guid UserId, Guid SpaceId) person) =>
        InSpace(person, async provider =>
        {
            var entries = await provider.GetRequiredService<DroverDbContext>().AuditLog.AsNoTracking().ToListAsync(Token);
            return string.Join("\n", entries.Select(entry => $"{entry.Action} {entry.Data.GetRawText()}"));
        });

    #endregion
}
