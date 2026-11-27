namespace Drover.Server.Tests;

/// <summary>
/// The anonymous route a provider's page sends the browser to when the person's head cannot listen: this server's own
/// https address. The attempt id is its only secret, the provider's state is still checked, and an id ends one waiting
/// attempt, once. Played against a fake provider that may return to the server, over real PostgreSQL.
/// </summary>
public sealed class ModelSignInReturnEndpointTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private const string PublicAddress = "https://drover.example.test";

    private readonly FakeModelSignIn open = new(FakeModelSignIn.OpenKind, isPersonalPlan: false) { CanReturnToServer = true };

    #endregion

    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings =>
        new Dictionary<string, string?>(base.Settings) { ["Drover:PublicUrl"] = PublicAddress };

    #endregion

    #region Returning

    [Fact]
    public async Task A_head_with_no_listener_is_sent_back_to_the_servers_own_address_and_the_return_finishes_the_sign_in()
    {
        var owner = await SeedPerson("owner");
        var client = await SignedIn(owner);
        var started = await Start(client, owner.SpaceId);
        var state = FakeModelSignIn.StateOf(new Uri(started.Address));

        var back = await NewClient().Get($"{ReturnPath(started.AttemptId)}?{open.Approve(state).Text}");
        var read = await client.Get($"{Path(owner.SpaceId)}/sign-in/{started.AttemptId}");

        Query(new Uri(started.Address))["redirect_uri"].Should().Be($"{PublicAddress}/api/model-sign-in/return/{started.AttemptId}");
        back.StatusCode.Should().Be(HttpStatusCode.OK);
        (await back.Content.ReadAsStringAsync(Token)).Should().Contain("Signed in. You can close this tab.").And.NotContainAny(open.Issued);
        var progress = (await read.Content.ReadFromJsonAsync<ModelSignInProgress>(Token))!;
        progress.Status.Should().Be(ModelSignInStatuses.Done);
        progress.Connection!.SignIn.Should().Be(new ModelSignInSummary(ModelSignInStatuses.Active, FakeModelSignIn.AccountLabel));
        (await ReadSecrets(owner.SpaceId, owner.UserId)).Should().ContainSingle(secret => secret.Kind == SecretKind.OauthTokens)
            .Which.AudienceUserId.Should().Be(owner.UserId);
        Logs.AllText().Should().NotContainAny(open.Issued);
    }

    [Fact]
    public async Task A_browser_signed_in_as_someone_else_is_refused_with_no_exchange_and_the_starter_can_still_finish()
    {
        var owner = await SeedPerson("owner");
        var other = await SeedPerson("other");
        var client = await SignedIn(owner);
        var started = await Start(client, owner.SpaceId);
        var approval = open.Approve(FakeModelSignIn.StateOf(new Uri(started.Address))).Text;

        var asOther = await (await SignedIn(other)).Get($"{ReturnPath(started.AttemptId)}?{approval}");
        var asOwner = await client.Get($"{ReturnPath(started.AttemptId)}?{approval}");

        asOther.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await asOther.Content.ReadAsStringAsync(Token)).Should().Contain("did not finish");
        (await ReadCredentialCount(other.SpaceId, other.UserId)).Should().Be(0);
        asOwner.StatusCode.Should().Be(HttpStatusCode.OK, "the refusal changed nothing for the person the attempt belongs to");
        (await ReadCredentialCount(owner.SpaceId, owner.UserId)).Should().Be(1);
    }

    [Fact]
    public async Task An_id_works_once_and_a_wrong_or_made_up_id_is_a_404()
    {
        var owner = await SeedPerson("owner");
        var client = await SignedIn(owner);
        var started = await Start(client, owner.SpaceId);
        var approval = open.Approve(FakeModelSignIn.StateOf(new Uri(started.Address))).Text;

        var first = await NewClient().Get($"{ReturnPath(started.AttemptId)}?{approval}");
        var reused = await NewClient().Get($"{ReturnPath(started.AttemptId)}?{approval}");
        var madeUp = await NewClient().Get($"{ReturnPath(Guid.NewGuid())}?{approval}");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        reused.StatusCode.Should().Be(HttpStatusCode.NotFound);
        madeUp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_return_with_the_wrong_state_or_a_refusal_ends_the_attempt_and_makes_no_connection()
    {
        var owner = await SeedPerson("owner");
        var client = await SignedIn(owner);
        var wrong = await Start(client, owner.SpaceId);

        var withWrongState = await NewClient().Get($"{ReturnPath(wrong.AttemptId)}?{open.Approve("another-state").Text}");
        var read = await client.Get($"{Path(owner.SpaceId)}/sign-in/{wrong.AttemptId}");
        var refused = await Start(client, owner.SpaceId);
        var withRefusal = await NewClient().Get($"{ReturnPath(refused.AttemptId)}?{FakeModelSignIn.Refuse(FakeModelSignIn.StateOf(new Uri(refused.Address))).Text}");

        withWrongState.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        withRefusal.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await withWrongState.Content.ReadAsStringAsync(Token)).Should().Contain("did not finish").And.NotContain("another-state");
        (await read.Content.ReadFromJsonAsync<ModelSignInProgress>(Token))!.Status.Should().Be(ModelSignInStatuses.Refused);
        (await ReadCredentialCount(owner.SpaceId, owner.UserId)).Should().Be(0);
    }

    [Fact]
    public async Task An_attempt_that_came_from_a_device_cannot_be_ended_through_the_open_route()
    {
        var owner = await SeedPerson("owner");
        var client = await SignedIn(owner);
        var response = await client.Post($"{Path(owner.SpaceId)}/sign-in", new StartModelSignInRequest(FakeModelSignIn.OpenKind, FakeModelSignIn.RedirectUri.AbsoluteUri));
        var started = (await response.Content.ReadFromJsonAsync<ModelSignInStarted>(Token))!;

        var back = await NewClient().Get($"{ReturnPath(started.AttemptId)}?{open.Approve(FakeModelSignIn.StateOf(new Uri(started.Address))).Text}");

        back.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Attempt_ids_are_128_random_bits_not_a_version_stamped_guid()
    {
        var owner = await SeedPerson("owner");
        var client = await SignedIn(owner);
        List<Guid> ids = [];

        for (var count = 0; count < 24; count++)
        {
            ids.Add((await Start(client, owner.SpaceId)).AttemptId);
        }

        // A random version-4 guid has six fixed bits; a 16-byte random value does not. All 24 sharing one is a 2^-72 chance.
        ids.Select(id => id.ToString()[14]).Distinct().Should().HaveCountGreaterThan(1);
        ids.Distinct().Should().HaveCount(ids.Count);
    }

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IModelClientProvider>(_ => new ScriptedModelProvider(new ScriptedModel(), FakeModelSignIn.OpenKind));
        services.AddSingleton<IModelSignIn>(open);
    }

    #endregion

    #region Private Methods

    private static string Path(Guid spaceId) => $"/api/spaces/{spaceId}/model-connections";

    private static string ReturnPath(Guid attemptId) => $"/api/model-sign-in/return/{attemptId}";

    private async Task<ApiClient> SignedIn((string Email, string Password, Guid UserId, Guid SpaceId) person)
    {
        var client = NewClient();
        (await client.SignIn(person.Email, person.Password)).EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>Starts a sign-in as a head with no listener does, naming no return address.</summary>
    private static async Task<ModelSignInStarted> Start(ApiClient client, Guid spaceId)
    {
        var response = await client.Post($"{Path(spaceId)}/sign-in", new StartModelSignInRequest(FakeModelSignIn.OpenKind, null));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<ModelSignInStarted>(Token))!;
    }

    private static Dictionary<string, string> Query(Uri address) =>
        address.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));

    private async Task<int> ReadCredentialCount(Guid spaceId, Guid userId) =>
        await AsPerson(spaceId, userId, db => db.ModelConnectionCredentials.CountAsync(Token));

    private async Task<List<Secret>> ReadSecrets(Guid spaceId, Guid userId) =>
        await AsPerson(spaceId, userId, db => db.Secrets.AsNoTracking().ToListAsync(Token));

    private async Task<T> AsPerson<T>(Guid spaceId, Guid userId, Func<DroverDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        provider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var work = await provider.GetRequiredService<TenantScope>().Begin(Token);
        var result = await query(provider.GetRequiredService<DroverDbContext>());
        await work.Commit(Token);
        return result;
    }

    #endregion
}
