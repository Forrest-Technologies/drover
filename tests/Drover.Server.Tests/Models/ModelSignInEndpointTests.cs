namespace Drover.Server.Tests;

/// <summary>
/// A person's own sign-in to a model provider, end to end over real PostgreSQL, against a fake provider played in
/// memory: who may start, finish, read and remove one, which return addresses a kind takes, and that no token leaves
/// the vault for a response or the log.
/// </summary>
public sealed class ModelSignInEndpointTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private readonly FakeModelSignIn plan = new(ScriptedModelProvider.ConnectionKind);

    private readonly FakeModelSignIn switchedOff = new(FakeModelSignIn.OpenKind, isPersonalPlan: false);

    #endregion

    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings =>
        new Dictionary<string, string?>(base.Settings) { ["Drover:Models:SignInOff:0"] = FakeModelSignIn.OpenKind };

    #endregion

    #region Signing In

    [Fact]
    public async Task A_member_signs_in_and_the_space_gets_its_one_connection_with_their_own_sign_in_and_no_token_is_ever_returned()
    {
        var owner = await SeedPerson("owner");
        var member = await SeedMember(owner.SpaceId, "member", MembershipRole.Member);
        var client = await SignedIn(member.Email, member.Password);

        var summary = await SignInThrough(client, owner.SpaceId);
        var list = await client.Get(Path(owner.SpaceId));
        var again = await SignInThrough(client, owner.SpaceId);

        summary.Kind.Should().Be(ScriptedModelProvider.ConnectionKind);
        summary.Name.Should().Be(plan.DisplayName);
        summary.Status.Should().Be(ConnectionStatuses.Ready);
        summary.HasKey.Should().BeFalse();
        summary.SignIn.Should().Be(new ModelSignInSummary(ModelSignInStatuses.Active, FakeModelSignIn.AccountLabel));
        again.Id.Should().Be(summary.Id, "a space has one connection of a sign-in kind");
        var listed = (await list.Content.ReadFromJsonAsync<List<ModelConnectionSummary>>(Token))!;
        listed.Should().ContainSingle().Which.SignIn!.Status.Should().Be(ModelSignInStatuses.Active);
        (await list.Content.ReadAsStringAsync(Token)).Should().NotContainAny(plan.Issued);
        Logs.AllText().Should().NotContainAny(plan.Issued);
        var stored = await ReadSecrets(owner.SpaceId, member.UserId);
        stored.Should().ContainSingle(secret => secret.Kind == SecretKind.OauthTokens)
            .Which.AudienceUserId.Should().Be(member.UserId, "the token set is private to the person who signed in");
    }

    [Fact]
    public async Task Signing_in_again_hands_the_provider_what_the_persons_own_sign_in_left_and_nobody_elses()
    {
        var owner = await SeedPerson("owner");
        var member = await SeedMember(owner.SpaceId, "member", MembershipRole.Member);
        var ownerClient = await SignedIn(owner.Email, owner.Password);
        var memberClient = await SignedIn(member.Email, member.Password);

        await SignInThrough(ownerClient, owner.SpaceId);
        await SignInThrough(memberClient, owner.SpaceId);
        await SignInThrough(ownerClient, owner.SpaceId);

        plan.Earliers[0].Should().BeEmpty("a first sign-in has nothing earlier");
        plan.Earliers[1].Should().BeEmpty("another member's sign-in is never read for this person");
        plan.Earliers[2].Should().ContainKey("client").WhoseValue.Should().StartWith("fake-client-");
    }

    [Fact]
    public async Task A_refusal_with_the_providers_own_reason_is_answered_with_drovers_sentence_for_it()
    {
        var owner = await SeedPerson("owner");
        var client = await SignedIn(owner.Email, owner.Password);
        plan.RefusalSentence = "ChatGPT signed you in but did not allow plan use. Try again and allow it.";
        var started = await Start(client, owner.SpaceId, FakeModelSignIn.RedirectUri.AbsoluteUri);

        var finish = await client.Post(
            $"{Path(owner.SpaceId)}/sign-in/{started.AttemptId}/finish",
            new FinishModelSignInRequest(plan.Approve(FakeModelSignIn.StateOf(new Uri(started.Address))).Text));

        finish.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await finish.Content.ReadAsStringAsync(Token)).Should().Contain(plan.RefusalSentence);
        (await ReadCredentialCount(owner.SpaceId, owner.UserId)).Should().Be(0);
    }

    [Fact]
    public async Task Another_member_sees_no_sign_in_of_theirs_and_cannot_finish_read_or_remove_anothers()
    {
        var owner = await SeedPerson("owner");
        var member = await SeedMember(owner.SpaceId, "member", MembershipRole.Admin);
        var ownerClient = await SignedIn(owner.Email, owner.Password);
        var memberClient = await SignedIn(member.Email, member.Password);
        var summary = await SignInThrough(ownerClient, owner.SpaceId);
        var started = await Start(ownerClient, owner.SpaceId, FakeModelSignIn.RedirectUri.AbsoluteUri);
        var approval = plan.Approve(FakeModelSignIn.StateOf(new Uri(started.Address))).Text;

        var finish = await memberClient.Post($"{Path(owner.SpaceId)}/sign-in/{started.AttemptId}/finish", new FinishModelSignInRequest(approval));
        var read = await memberClient.Get($"{Path(owner.SpaceId)}/sign-in/{started.AttemptId}");
        var remove = await memberClient.Delete($"{Path(owner.SpaceId)}/{summary.Id}/sign-in");
        var list = (await (await memberClient.Get(Path(owner.SpaceId))).Content.ReadFromJsonAsync<List<ModelConnectionSummary>>(Token))!;

        finish.StatusCode.Should().Be(HttpStatusCode.NotFound);
        read.StatusCode.Should().Be(HttpStatusCode.NotFound);
        remove.StatusCode.Should().Be(HttpStatusCode.NotFound);
        list.Should().ContainSingle().Which.SignIn.Should().Be(new ModelSignInSummary(ModelSignInStatuses.None, null), "it never says who else has signed in");
        (await ReadCredentialCount(owner.SpaceId, member.UserId)).Should().Be(0);
        (await ReadCredentialCount(owner.SpaceId, owner.UserId)).Should().Be(1);
        var ownersRead = await ownerClient.Get($"{Path(owner.SpaceId)}/sign-in/{started.AttemptId}");
        (await ownersRead.Content.ReadFromJsonAsync<ModelSignInProgress>(Token))!.Status.Should().Be(ModelSignInStatuses.Waiting);
    }

    [Fact]
    public async Task Another_space_gets_404_for_every_route()
    {
        var owner = await SeedPerson("owner");
        var stranger = await SeedPerson("stranger");
        var ownerClient = await SignedIn(owner.Email, owner.Password);
        var strangerClient = await SignedIn(stranger.Email, stranger.Password);
        var summary = await SignInThrough(ownerClient, owner.SpaceId);
        var started = await Start(ownerClient, owner.SpaceId, FakeModelSignIn.RedirectUri.AbsoluteUri);

        var responses = new[]
        {
            await strangerClient.Post($"{Path(owner.SpaceId)}/sign-in", new StartModelSignInRequest(plan.Kind, FakeModelSignIn.RedirectUri.AbsoluteUri)),
            await strangerClient.Post($"{Path(owner.SpaceId)}/sign-in/{started.AttemptId}/finish", new FinishModelSignInRequest("code=x")),
            await strangerClient.Get($"{Path(owner.SpaceId)}/sign-in/{started.AttemptId}"),
            await strangerClient.Delete($"{Path(owner.SpaceId)}/{summary.Id}/sign-in"),
            await strangerClient.Post($"{Path(stranger.SpaceId)}/sign-in/{started.AttemptId}/finish", new FinishModelSignInRequest("code=x")),
            await strangerClient.Get($"{Path(stranger.SpaceId)}/sign-in/{started.AttemptId}"),
            await strangerClient.Delete($"{Path(stranger.SpaceId)}/{summary.Id}/sign-in"),
        };

        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.NotFound);
        plan.Begins.Should().Be(2, "the stranger started nothing");
    }

    [Theory]
    [InlineData("http://127.0.0.1:49152/auth/callback", HttpStatusCode.OK)]
    [InlineData("http://127.0.0.1:1455/elsewhere", HttpStatusCode.BadRequest)]
    [InlineData("https://attacker.example.test/auth/callback", HttpStatusCode.BadRequest)]
    [InlineData("not an address", HttpStatusCode.BadRequest)]
    [InlineData(null, HttpStatusCode.BadRequest)]
    public async Task A_kind_takes_its_own_return_address_on_any_port_and_nothing_else(string? redirectUri, HttpStatusCode expected)
    {
        var owner = await SeedPerson("owner");
        var client = await SignedIn(owner.Email, owner.Password);

        var response = await client.Post($"{Path(owner.SpaceId)}/sign-in", new StartModelSignInRequest(plan.Kind, redirectUri));

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task An_unknown_kind_and_a_kind_the_operator_switched_off_are_refused_before_anything_is_sent()
    {
        var owner = await SeedPerson("owner");
        var client = await SignedIn(owner.Email, owner.Password);

        var unknown = await client.Post($"{Path(owner.SpaceId)}/sign-in", new StartModelSignInRequest("no_such_signin", FakeModelSignIn.RedirectUri.AbsoluteUri));
        var off = await client.Post($"{Path(owner.SpaceId)}/sign-in", new StartModelSignInRequest(FakeModelSignIn.OpenKind, FakeModelSignIn.RedirectUri.AbsoluteUri));

        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        off.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await off.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString().Should().Be(ModelCredentials.NotAvailable);
        switchedOff.Begins.Should().Be(0);
    }

    [Fact]
    public async Task A_refused_consent_or_a_return_for_another_attempt_ends_the_attempt_with_nothing_stored()
    {
        var owner = await SeedPerson("owner");
        var client = await SignedIn(owner.Email, owner.Password);
        var refused = await Start(client, owner.SpaceId, FakeModelSignIn.RedirectUri.AbsoluteUri);
        var refusedFinish = await client.Post(
            $"{Path(owner.SpaceId)}/sign-in/{refused.AttemptId}/finish",
            new FinishModelSignInRequest(FakeModelSignIn.Refuse(FakeModelSignIn.StateOf(new Uri(refused.Address))).Text));
        var wrong = await Start(client, owner.SpaceId, FakeModelSignIn.RedirectUri.AbsoluteUri);

        var wrongFinish = await client.Post($"{Path(owner.SpaceId)}/sign-in/{wrong.AttemptId}/finish", new FinishModelSignInRequest(plan.Approve("another-state").Text));
        var status = await client.Get($"{Path(owner.SpaceId)}/sign-in/{wrong.AttemptId}");
        var retried = await client.Post(
            $"{Path(owner.SpaceId)}/sign-in/{wrong.AttemptId}/finish",
            new FinishModelSignInRequest(plan.Approve(FakeModelSignIn.StateOf(new Uri(wrong.Address))).Text));

        refusedFinish.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        wrongFinish.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await status.Content.ReadFromJsonAsync<ModelSignInProgress>(Token))!.Status.Should().Be(ModelSignInStatuses.Refused);
        retried.StatusCode.Should().Be(HttpStatusCode.BadRequest, "an attempt that ended cannot be finished again");
        plan.Received.Should().BeEmpty("nothing was exchanged");
        (await ReadCredentialCount(owner.SpaceId, owner.UserId)).Should().Be(0);
    }

    [Fact]
    public async Task Signing_out_tells_the_provider_keeps_only_what_it_registered_and_a_second_sign_out_removes_everything()
    {
        var owner = await SeedPerson("owner");
        var client = await SignedIn(owner.Email, owner.Password);
        var summary = await SignInThrough(client, owner.SpaceId);

        var signedOut = await client.Delete($"{Path(owner.SpaceId)}/{summary.Id}/sign-in");
        var afterSignOut = (await (await client.Get(Path(owner.SpaceId))).Content.ReadFromJsonAsync<List<ModelConnectionSummary>>(Token))!;
        await SignInThrough(client, owner.SpaceId);
        await client.Delete($"{Path(owner.SpaceId)}/{summary.Id}/sign-in");
        var removed = await client.Delete($"{Path(owner.SpaceId)}/{summary.Id}/sign-in");
        var again = await client.Delete($"{Path(owner.SpaceId)}/{summary.Id}/sign-in");
        var list = (await (await client.Get(Path(owner.SpaceId))).Content.ReadFromJsonAsync<List<ModelConnectionSummary>>(Token))!;

        signedOut.StatusCode.Should().Be(HttpStatusCode.NoContent);
        plan.Revoked.Should().HaveCount(2, "each sign-out with tokens tells the provider, and one with none sends nothing");
        afterSignOut.Should().ContainSingle().Which.SignIn.Should().Be(new ModelSignInSummary(ModelSignInStatuses.SignedOut, null), "a sign-out keeps no account label");
        plan.Earliers[1].Should().ContainKey("client", "what the provider registered for the person outlives a sign-out, so signing in again does not register anew");
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        again.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadCredentialCount(owner.SpaceId, owner.UserId)).Should().Be(0);
        (await ReadSecrets(owner.SpaceId, owner.UserId)).Should().NotContain(secret => secret.Kind == SecretKind.OauthTokens);
        list.Should().ContainSingle().Which.SignIn!.Status.Should().Be(ModelSignInStatuses.None);
        Logs.AllText().Should().NotContainAny(plan.Issued);
    }

    [Fact]
    public async Task A_sign_in_the_provider_made_but_that_is_not_used_keeps_only_its_registration_for_the_next()
    {
        var owner = await SeedPerson("owner");
        var client = await SignedIn(owner.Email, owner.Password);
        plan.RefusalSentence = "ChatGPT signed you in but did not allow plan use. Try again and allow it.";
        plan.RefusalKeeps = true;

        var first = await Refused(client, owner.SpaceId);
        var withNoConnection = (await (await client.Get(Path(owner.SpaceId))).Content.ReadFromJsonAsync<List<ModelConnectionSummary>>(Token))!;
        plan.RefusalSentence = null;
        var summary = await SignInThrough(client, owner.SpaceId);
        var active = plan.Issued[^2];
        plan.RefusalSentence = "ChatGPT signed you in but did not allow plan use. Try again and allow it.";
        await Refused(client, owner.SpaceId);
        var list = (await (await client.Get(Path(owner.SpaceId))).Content.ReadFromJsonAsync<List<ModelConnectionSummary>>(Token))!;
        var credential = (await AsPerson(owner.SpaceId, owner.UserId, db => db.ModelConnectionCredentials.SingleAsync(Token)))!;
        var kept = await Reveal(owner.SpaceId, owner.UserId, credential.SecretId);
        plan.RefusalSentence = null;
        await SignInThrough(client, owner.SpaceId);

        first.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        withNoConnection.Should().BeEmpty("a sign-in that is not used never makes the space's connection");
        plan.Revoked.Should().Contain(active, "the sign-in the person had before is ended with the provider");
        list.Should().ContainSingle().Which.SignIn.Should().Be(new ModelSignInSummary(ModelSignInStatuses.SignedOut, null));
        kept.Should().NotContainAny(plan.Issued, "the kept registration holds no token");
        ModelCredentials.ReadTokens(kept)!.AccessToken.Should().BeEmpty();
        credential.AccountLabel.Should().BeNull();
        plan.Earliers[^1].Should().ContainKey("client");
        summary.SignIn!.Status.Should().Be(ModelSignInStatuses.Active);
    }

    [Fact]
    public async Task A_return_too_long_to_be_a_sign_ins_is_refused_and_the_attempt_still_waits()
    {
        var owner = await SeedPerson("owner");
        var client = await SignedIn(owner.Email, owner.Password);
        var started = await Start(client, owner.SpaceId, FakeModelSignIn.RedirectUri.AbsoluteUri);

        var finish = await client.Post(
            $"{Path(owner.SpaceId)}/sign-in/{started.AttemptId}/finish",
            new FinishModelSignInRequest(new string('a', FinishModelSignInRequest.MostLength + 1)));
        var read = await client.Get($"{Path(owner.SpaceId)}/sign-in/{started.AttemptId}");

        finish.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await read.Content.ReadFromJsonAsync<ModelSignInProgress>(Token))!.Status.Should().Be(ModelSignInStatuses.Waiting);
        plan.Received.Should().BeEmpty();
    }

    [Fact]
    public async Task A_sign_in_kind_cannot_be_given_a_key()
    {
        var owner = await SeedPerson("owner");
        var client = await SignedIn(owner.Email, owner.Password);

        var created = await client.Post(Path(owner.SpaceId), new CreateModelConnectionRequest(plan.Kind, "Plan", null, "a-key-value", null));

        created.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IModelClientProvider>(_ => new ScriptedModelProvider(new ScriptedModel()));
        services.AddSingleton<IModelClientProvider>(_ => new ScriptedModelProvider(new ScriptedModel(), FakeModelSignIn.OpenKind));
        services.AddSingleton<IModelSignIn>(plan);
        services.AddSingleton<IModelSignIn>(switchedOff);
    }

    #endregion

    #region Private Methods

    private static string Path(Guid spaceId) => $"/api/spaces/{spaceId}/model-connections";

    private async Task<ApiClient> SignedIn(string email, string password)
    {
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<ModelSignInStarted> Start(ApiClient client, Guid spaceId, string redirectUri)
    {
        var response = await client.Post($"{Path(spaceId)}/sign-in", new StartModelSignInRequest(ScriptedModelProvider.ConnectionKind, redirectUri));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        var started = (await response.Content.ReadFromJsonAsync<ModelSignInStarted>(Token))!;
        started.Wait.Should().Be(ModelSignInWaits.Return);
        return started;
    }

    /// <summary>Starts, approves on the fake provider's page, and hands the return to the finish route, as a device does.</summary>
    private async Task<ModelConnectionSummary> SignInThrough(ApiClient client, Guid spaceId)
    {
        var started = await Start(client, spaceId, FakeModelSignIn.RedirectUri.AbsoluteUri);
        var approval = plan.Approve(FakeModelSignIn.StateOf(new Uri(started.Address))).Text;
        var finish = await client.Post($"{Path(spaceId)}/sign-in/{started.AttemptId}/finish", new FinishModelSignInRequest(approval));
        finish.StatusCode.Should().Be(HttpStatusCode.OK, await finish.Content.ReadAsStringAsync(Token));
        (await finish.Content.ReadAsStringAsync(Token)).Should().NotContainAny(plan.Issued);
        var read = await client.Get($"{Path(spaceId)}/sign-in/{started.AttemptId}");
        var progress = (await read.Content.ReadFromJsonAsync<ModelSignInProgress>(Token))!;
        progress.Status.Should().Be(ModelSignInStatuses.Done);
        return (await finish.Content.ReadFromJsonAsync<ModelConnectionSummary>(Token))!;
    }

    private async Task<int> ReadCredentialCount(Guid spaceId, Guid userId) =>
        await AsPerson(spaceId, userId, db => db.ModelConnectionCredentials.CountAsync(Token));

    private async Task<List<Secret>> ReadSecrets(Guid spaceId, Guid userId) =>
        await AsPerson(spaceId, userId, db => db.Secrets.AsNoTracking().ToListAsync(Token));

    private async Task<HttpResponseMessage> Refused(ApiClient client, Guid spaceId)
    {
        var started = await Start(client, spaceId, FakeModelSignIn.RedirectUri.AbsoluteUri);
        return await client.Post(
            $"{Path(spaceId)}/sign-in/{started.AttemptId}/finish",
            new FinishModelSignInRequest(plan.Approve(FakeModelSignIn.StateOf(new Uri(started.Address))).Text));
    }

    private async Task<string> Reveal(Guid spaceId, Guid userId, Guid secretId)
    {
        await using var scope = Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        provider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var work = await provider.GetRequiredService<TenantScope>().Begin(Token);
        var value = await provider.GetRequiredService<ISecretVault>().Reveal(secretId, Token);
        await work.Commit(Token);
        return value;
    }

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
