namespace Drover.Server.Tests;

/// <summary>
/// The deployment rule <c>SubscriptionSignIn</c> on a server started under the hosted preset: a personal plan kind is
/// refused on every route with the rule's sentence before any request leaves, and a sign-in the provider offers to
/// any app still works, with its tokens private to the person who signed in.
/// </summary>
public sealed class PersonalPlanSignInEndpointTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private readonly FakeModelSignIn plan = new(FakeModelSignIn.PlanKind);

    private readonly FakeModelSignIn open = new(ScriptedModelProvider.ConnectionKind, isPersonalPlan: false);

    #endregion

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
    public async Task Enforces_deployment_policy_subscription_mode_and_isolates_user_tokens_in_vault()
    {
        var owner = await SeedPerson("owner");
        var member = await SeedMember(owner.SpaceId, "member", MembershipRole.Admin);
        var client = NewClient();
        (await client.SignIn(owner.Email, owner.Password)).EnsureSuccessStatusCode();
        var path = $"/api/spaces/{owner.SpaceId}/model-connections";

        var refused = await client.Post($"{path}/sign-in", new StartModelSignInRequest(plan.Kind, FakeModelSignIn.RedirectUri.AbsoluteUri));
        var started = await client.Post($"{path}/sign-in", new StartModelSignInRequest(open.Kind, FakeModelSignIn.RedirectUri.AbsoluteUri));
        var attempt = (await started.Content.ReadFromJsonAsync<ModelSignInStarted>(Token))!;
        var finished = await client.Post(
            $"{path}/sign-in/{attempt.AttemptId}/finish",
            new FinishModelSignInRequest(open.Approve(FakeModelSignIn.StateOf(new Uri(attempt.Address))).Text));

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await refused.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString().Should().Be(
            "Signing in with a personal plan is not available on this server. You can add an API key instead.");
        plan.Begins.Should().Be(0, "nothing is sent for a refused kind");
        plan.Received.Should().BeEmpty();
        started.StatusCode.Should().Be(HttpStatusCode.OK, "a sign-in the provider offers to any app stays on");
        finished.StatusCode.Should().Be(HttpStatusCode.OK, await finished.Content.ReadAsStringAsync(Token));
        (await finished.Content.ReadAsStringAsync(Token)).Should().NotContainAny(open.Issued);
        Logs.AllText().Should().NotContainAny(open.Issued);

        var ownersRows = await AsPerson(owner.SpaceId, owner.UserId, async db => (
            Credentials: await db.ModelConnectionCredentials.AsNoTracking().ToListAsync(Token),
            Secrets: await db.Secrets.AsNoTracking().Where(secret => secret.Kind == SecretKind.OauthTokens).ToListAsync(Token)));
        var membersRows = await AsPerson(owner.SpaceId, member.UserId, async db => (
            Credentials: await db.ModelConnectionCredentials.CountAsync(Token),
            Secrets: await db.Secrets.CountAsync(secret => secret.Kind == SecretKind.OauthTokens, Token)));
        var credential = ownersRows.Credentials.Should().ContainSingle().Subject;
        credential.AudienceUserId.Should().Be(owner.UserId);
        ownersRows.Secrets.Should().ContainSingle().Which.AudienceUserId.Should().Be(owner.UserId);
        membersRows.Should().Be((0, 0), "another member of the space, an admin included, sees neither the row nor the token set");
        var revealed = await Reveal(owner.SpaceId, owner.UserId, credential.SecretId);
        var revealing = () => Reveal(owner.SpaceId, member.UserId, credential.SecretId);
        revealed.Should().ContainAny(open.Issued, "the owner's own token set is in the vault");
        await revealing.Should().ThrowAsync<KeyNotFoundException>("the vault does not hand another member the owner's token set");
    }

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IModelClientProvider>(_ => new ScriptedModelProvider(new ScriptedModel()));
        services.AddSingleton<IModelClientProvider>(_ => new ScriptedModelProvider(new ScriptedModel(), FakeModelSignIn.PlanKind));
        services.AddSingleton<IModelSignIn>(plan);
        services.AddSingleton<IModelSignIn>(open);
    }

    #endregion

    #region Private Methods

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
