namespace Drover.Server.Tests;

/// <summary>
/// A space admin says a model of a compatible endpoint reads pictures, because the endpoint lists models and nothing
/// about them. The mark is the admin's alone, is per model, can be cleared, and decides whether a picture is taken
/// as a picture. A provider that reports its own capabilities takes no mark.
/// </summary>
public sealed class ModelVisionTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private ScriptedModelProvider? model;

    private ScriptedProvider Scripted { get; } = new();

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(new ScriptedModel()));
        services.AddSingleton<IModelClientProvider>(Scripted);
    }

    #endregion

    #region Marking

    [Fact]
    public async Task An_admin_marks_a_model_as_reading_pictures_and_clears_the_mark()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateCompatible(client, alice.SpaceId);
        await SeedModels(alice, connection.Id, "plain", "other");

        var marked = await Mark(client, alice.SpaceId, connection.Id, "plain", true);
        var listed = await ListModels(client, alice.SpaceId, connection.Id);

        marked.StatusCode.Should().Be(HttpStatusCode.OK, await marked.Content.ReadAsStringAsync(Token));
        var response = (await marked.Content.ReadFromJsonAsync<ModelCatalogResponse>(Token))!;
        (response.Model, response.Capabilities.Vision, response.Capabilities.VisionSetByOperator).Should().Be(("plain", true, true));
        listed.Single(entry => entry.Model == "plain").Capabilities.Vision.Should().BeTrue();
        listed.Single(entry => entry.Model == "other").Capabilities.Vision.Should().BeFalse("the mark is for one model");

        var cleared = await Mark(client, alice.SpaceId, connection.Id, "plain", false);

        cleared.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = (await ListModels(client, alice.SpaceId, connection.Id)).Single(entry => entry.Model == "plain");
        (after.Capabilities.Vision, after.Capabilities.VisionSetByOperator).Should().Be((false, false));
    }

    [Fact]
    public async Task A_model_id_with_slashes_can_be_marked()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateCompatible(client, alice.SpaceId);
        await SeedModels(alice, connection.Id, "meta/llama/vision-3");

        (await Mark(client, alice.SpaceId, connection.Id, "meta/llama/vision-3", true)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ListModels(client, alice.SpaceId, connection.Id)).Single().Capabilities.Vision.Should().BeTrue();
    }

    [Fact]
    public async Task Only_an_admin_marks_a_model_and_another_space_cannot_reach_the_connection()
    {
        var alice = await SeedPerson("alice");
        var bob = await SeedPerson("bob");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var asAlice = await SignIn(alice);
        var asBob = await SignIn(bob);
        var asMember = NewClient();
        await asMember.SignIn(member.Email, member.Password);
        var connection = await CreateCompatible(asAlice, alice.SpaceId);
        await SeedModels(alice, connection.Id, "plain");

        (await Mark(asMember, alice.SpaceId, connection.Id, "plain", true)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Mark(asBob, alice.SpaceId, connection.Id, "plain", true)).StatusCode.Should().Be(HttpStatusCode.NotFound, "bob is no member of alice's space, which does not say it exists");
        (await Mark(asBob, bob.SpaceId, connection.Id, "plain", true)).StatusCode.Should().Be(HttpStatusCode.NotFound, "the connection is not in bob's space");

        (await ListModels(asAlice, alice.SpaceId, connection.Id)).Single().Capabilities.Vision.Should().BeFalse();
    }

    [Fact]
    public async Task A_model_the_catalog_does_not_list_or_a_missing_name_is_refused()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateCompatible(client, alice.SpaceId);
        await SeedModels(alice, connection.Id, "plain");

        (await Mark(client, alice.SpaceId, connection.Id, "unlisted", true)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Mark(client, alice.SpaceId, connection.Id, "", true)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Mark(client, alice.SpaceId, connection.Id, null, true)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Mark(client, alice.SpaceId, Guid.NewGuid(), "plain", true)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_provider_that_reports_its_own_capabilities_takes_no_mark()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var venice = await Read<ModelConnectionSummary>(await client.Post(
            Path(alice.SpaceId),
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Reports its own", null, null, "plain")));
        await SeedModels(alice, venice.Id, "plain");

        var refused = await Mark(client, alice.SpaceId, venice.Id, "plain", true);

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync(Token)).Should().Contain("reports its own");
        (await ListModels(client, alice.SpaceId, venice.Id)).Single().Capabilities.Vision.Should().BeFalse();
    }

    #endregion

    #region Overlapping Writes

    [Fact]
    public async Task A_refresh_that_overlaps_a_mark_keeps_the_mark_and_what_the_provider_reported()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateScripted(client, alice.SpaceId);
        await SeedModels(alice, connection.Id, "plain");
        Scripted.OnList = _ => Task.FromResult<IReadOnlyList<ModelInfo>>(
            [new ModelInfo("plain", "plain", new ModelCapabilities { ContextTokens = 8192 }, [])]);
        await using var admin = await HoldCatalogRow(alice, "plain", entry => entry.OperatorCapabilities = new ModelCapabilityOverrides { Vision = true });

        var refreshing = client.Post($"{Path(alice.SpaceId)}/{connection.Id}/models/refresh");
        await WaitForAWriterToWait();
        await admin.Commit();

        (await refreshing).StatusCode.Should().Be(HttpStatusCode.OK);
        var stored = (await ListModels(client, alice.SpaceId, connection.Id)).Single().Capabilities;
        (stored.Vision, stored.VisionSetByOperator).Should().Be((true, true), "the admin's mark was committed while the refresh was writing");
        stored.ContextSize.Should().Be(8192, "the refresh's own change is kept as well");
    }

    [Fact]
    public async Task A_mark_that_overlaps_a_refresh_keeps_what_the_refresh_reported()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateScripted(client, alice.SpaceId);
        await SeedModels(alice, connection.Id, "plain");
        await ChangeKind(alice, connection.Id, ModelConnectionKinds.Compatible);
        await using var refresh = await HoldCatalogRow(alice, "plain", entry => entry.Capabilities = entry.Capabilities with { ContextTokens = 8192 });

        var marking = Mark(client, alice.SpaceId, connection.Id, "plain", true);
        await WaitForAWriterToWait();
        await refresh.Commit();

        (await marking).StatusCode.Should().Be(HttpStatusCode.OK);
        var stored = (await ListModels(client, alice.SpaceId, connection.Id)).Single().Capabilities;
        (stored.Vision, stored.VisionSetByOperator).Should().Be((true, true));
        stored.ContextSize.Should().Be(8192, "the refresh committed while the mark was being written, and the mark must not put the old value back");
    }

    #endregion

    #region What The Mark Decides

    [Fact]
    public async Task A_picture_is_refused_until_the_model_is_marked_and_taken_after()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice);
        var connection = await CreateCompatible(client, alice.SpaceId);
        await SeedModels(alice, connection.Id, "plain");
        var bot = await Read<BotSummary>(await client.Post($"{Space(alice.SpaceId)}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, "plain")));
        var thread = await Read<ThreadSummary>(await client.Put($"{Space(alice.SpaceId)}/bots/{bot.Id}/main-thread"));
        var rulesPath = $"{Space(alice.SpaceId)}/threads/{thread.Id}/attachment-rules";
        var uploadPath = $"{Space(alice.SpaceId)}/threads/{thread.Id}/files?name=dot.png";

        (await Rules(client, rulesPath)).ModelReadsImages.Should().BeFalse();
        var refused = await client.PostContent(uploadPath, new ByteArrayContent(Png(3, 2)));
        refused.StatusCode.Should().NotBe(HttpStatusCode.Created, "the model is not marked, so a picture has nowhere to go");

        (await Mark(client, alice.SpaceId, connection.Id, "plain", true)).EnsureSuccessStatusCode();

        (await Rules(client, rulesPath)).ModelReadsImages.Should().BeTrue("the attachment hint follows the mark");
        var taken = await client.PostContent(uploadPath, new ByteArrayContent(Png(3, 2)));
        taken.StatusCode.Should().Be(HttpStatusCode.Created, await taken.Content.ReadAsStringAsync(Token));
        (await taken.Content.ReadFromJsonAsync<FileResponse>(Token))!.ContentType.Should().Be("image/png");

        (await Mark(client, alice.SpaceId, connection.Id, "plain", false)).EnsureSuccessStatusCode();

        (await Rules(client, rulesPath)).ModelReadsImages.Should().BeFalse();
    }

    #endregion

    #region Private Methods

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    private static string Path(Guid spaceId) => $"{Space(spaceId)}/model-connections";

    private static byte[] Png(int width, int height) =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, .. "IHDR"u8,
        0, 0, 0, (byte)width, 0, 0, 0, (byte)height, 8, 6, 0, 0, 0,
    ];

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

    private static Task<HttpResponseMessage> Mark(ApiClient client, Guid spaceId, Guid connectionId, string? modelId, bool readsImages) =>
        client.Put($"{Path(spaceId)}/{connectionId}/models/vision", new SetModelVisionRequest(modelId, readsImages));

    private static async Task<List<ModelCatalogResponse>> ListModels(ApiClient client, Guid spaceId, Guid connectionId) =>
        await Read<List<ModelCatalogResponse>>(await client.Get($"{Path(spaceId)}/{connectionId}/models"));

    private static async Task<AttachmentRules> Rules(ApiClient client, string path) =>
        await Read<AttachmentRules>(await client.Get(path));

    private static async Task<ModelConnectionSummary> CreateCompatible(ApiClient client, Guid spaceId) =>
        await Read<ModelConnectionSummary>(await client.Post(
            Path(spaceId),
            new CreateModelConnectionRequest(ModelConnectionKinds.Compatible, "Compatible", "https://models.example.test/v1", null, "plain")));

    private static async Task<ModelConnectionSummary> CreateScripted(ApiClient client, Guid spaceId) =>
        await Read<ModelConnectionSummary>(await client.Post(
            Path(spaceId),
            new CreateModelConnectionRequest(ScriptedProvider.ConnectionKind, "Scripted", null, null, null)));

    private async Task ChangeKind((string Email, string Password, Guid UserId, Guid SpaceId) person, Guid connectionId, string kind)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(person.SpaceId, person.UserId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var db = scope.ServiceProvider.GetRequiredService<DroverDbContext>();
        (await db.ModelConnections.SingleAsync(row => row.Id == connectionId, Token)).Kind = kind;
        await db.SaveChangesAsync(Token);
        await work.Commit(Token);
    }

    /// <summary>
    /// Changes a catalog row in a transaction that stays open, so the row stays locked and the write that comes next
    /// has to wait for it. Committing releases it.
    /// </summary>
    private async Task<HeldWrite> HoldCatalogRow(
        (string Email, string Password, Guid UserId, Guid SpaceId) person,
        string modelId,
        Action<ModelCatalogEntry> change)
    {
        var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(person.SpaceId, person.UserId);
        var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var db = scope.ServiceProvider.GetRequiredService<DroverDbContext>();
        var entry = await db.ModelCatalog.SingleAsync(row => row.Model == modelId, Token);
        change(entry);
        await db.SaveChangesAsync(Token);
        return new HeldWrite(scope, work);
    }

    /// <summary>Waits until another session is waiting on a lock, which is the write that has to wait for the held row.</summary>
    private async Task WaitForAWriterToWait()
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'",
            connection);

        for (var attempt = 0; attempt < 300; attempt++)
        {
            if ((long)(await command.ExecuteScalarAsync(Token))! > 0)
            {
                return;
            }

            await Task.Delay(50, Token);
        }

        throw new TimeoutException("No write waited on the held row.");
    }

    private sealed class HeldWrite(AsyncServiceScope scope, TenantWork work) : IAsyncDisposable
    {
        #region Private Fields

        private bool committed;

        #endregion

        #region Public Methods

        public async Task Commit()
        {
            committed = true;
            await work.Commit(Token);
        }

        #endregion

        #region Interface Implementations

        public async ValueTask DisposeAsync()
        {
            if (!committed)
            {
                await work.DisposeAsync();
            }

            await scope.DisposeAsync();
        }

        #endregion
    }

    private async Task SeedModels((string Email, string Password, Guid UserId, Guid SpaceId) person, Guid connectionId, params string[] models)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(person.SpaceId, person.UserId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var db = scope.ServiceProvider.GetRequiredService<DroverDbContext>();

        foreach (var id in models)
        {
            db.ModelCatalog.Add(new ModelCatalogEntry
            {
                ModelConnectionId = connectionId,
                Model = id,
                DisplayName = id,
                Capabilities = new ModelCapabilities(),
                LastSeenAt = Time.GetUtcNow(),
            });
        }

        await db.SaveChangesAsync(Token);
        await work.Commit(Token);
    }

    #endregion
}
