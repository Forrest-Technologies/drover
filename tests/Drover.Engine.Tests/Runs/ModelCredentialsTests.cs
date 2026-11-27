namespace Drover.Engine.Tests.Runs;

/// <summary>
/// Whose credential a model call sends when its connection is one each person signs in to: the run's own person's,
/// read under their tenant scope, renewed once however many runs find it running out, and never anyone else's or the
/// connection's key in its place (ADR 0006). The world's scripted connection is made with a key, then a host where a
/// sign-in serves its kind treats it as a sign-in connection: so a run that used the key would show.
/// </summary>
public sealed class ModelCredentialsTests(PostgresServer postgres)
{
    #region Running

    [Fact]
    public async Task A_run_on_a_sign_in_connection_sends_its_own_persons_token_and_never_the_connections_key()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("Hello.")]));
        var signIn = new FakeModelSignIn(ScriptedModelProvider.ConnectionKind);
        var host = world.StartHost(replaceServices: services => services.AddSingleton<IModelSignIn>(signIn));
        await SignIn(world, host, signIn, world.UserId);
        var threadId = await world.NewThread();

        var runId = await world.Post(threadId, "Hi.");

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        world.Model.KeysSeen.Should().NotBeEmpty().And.OnlyContain(key => key != null && signIn.Issued.Contains(key));
        world.Model.KeysSeen.Should().NotContain(world.ModelKey);
    }

    [Fact]
    public async Task A_member_who_has_not_signed_in_is_told_so_no_model_is_called_and_nothing_of_anothers_is_revealed()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("Hello.")]));
        var signIn = new FakeModelSignIn(ScriptedModelProvider.ConnectionKind);
        var reveals = new RevealLog();
        var host = world.StartHost(replaceServices: services =>
        {
            services.AddSingleton<IModelSignIn>(signIn);
            services.AddScoped<DataProtectionSecretVault>();
            services.AddScoped<ISecretVault>(provider => new RecordingVault(provider.GetRequiredService<DataProtectionSecretVault>(), reveals));
        });
        await SignIn(world, host, signIn, world.UserId);
        var ownersSecret = await world.Read(db => db.ModelConnectionCredentials.Select(credential => credential.SecretId).SingleAsync(RunWorld.Token));
        var memberId = await world.AddMember(MembershipRole.Member);
        var threadId = (await world.TransactAs(world.SpaceId, memberId, services => services.GetRequiredService<ThreadService>()
            .Create(world.BotId, "Mine", memberId, RunWorld.Token)))!.Id;
        var callsBefore = world.Model.KeysSeen.Count;
        reveals.Clear();

        var post = await world.PostRaw(threadId, "Hi.", asUser: memberId);
        var run = await world.WaitForStatus(post.Run!.Id, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Failed);
        run.Error!.Code.Should().Be(RunReasons.SignInRequired);
        run.Error.Message.Should().Be("Helper answers with Scripted, which each person signs in to with their own account. Sign in with yours to talk to it.");
        world.Model.KeysSeen.Should().HaveCount(callsBefore, "no model call is made for a person with no sign-in");
        reveals.Revealed.Should().NotContain(ownersSecret, "another person's tokens are never read for this run");
        signIn.Refreshes.Should().Be(0);
    }

    #endregion

    #region Reading

    [Fact]
    public async Task A_person_reads_their_own_sign_in_and_another_person_in_the_space_finds_none()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var signIn = new FakeModelSignIn(ScriptedModelProvider.ConnectionKind);
        var host = world.IdleHost(services => services.AddSingleton<IModelSignIn>(signIn));
        var memberId = await world.AddMember(MembershipRole.Admin);
        await SignIn(world, host, signIn, world.UserId);

        var owners = await Read(world, host, world.UserId);
        var members = await Read(world, host, memberId);

        owners.Problem.Should().BeNull();
        signIn.Issued.Should().Contain(owners.Value!);
        members.Problem.Should().Be(ModelCredentialProblem.SignInMissing);
        members.Value.Should().BeNull("there is no fallback to another person's sign-in, or to the connection's key");
    }

    #endregion

    #region Renewing

    [Fact]
    public async Task Two_calls_that_find_the_token_running_out_make_one_refresh_between_them_and_both_go_on()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var signIn = new FakeModelSignIn(ScriptedModelProvider.ConnectionKind) { Lifetime = TimeSpan.FromMinutes(2) };
        var host = world.IdleHost(services => services.AddSingleton<IModelSignIn>(signIn));
        await SignIn(world, host, signIn, world.UserId);
        var before = signIn.Refreshes;
        var credentials = host.Services.GetRequiredService<ModelCredentials>();
        var first = await Read(world, host, world.UserId, ready: false);
        var second = await Read(world, host, world.UserId, ready: false);
        first.SignIn.Should().NotBeNull();
        first.ExpiresAt.Should().BeBefore(DateTimeOffset.UtcNow + ModelCredentials.RefreshLead, "the token runs out within the lead");
        signIn.Lifetime = TimeSpan.FromHours(1);
        signIn.RefreshGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var one = credentials.Ready(first, RunWorld.Token);
        var two = credentials.Ready(second, RunWorld.Token);
        await WaitFor(() => signIn.Refreshes == before + 1);
        signIn.RefreshGate.SetResult();
        var results = await Task.WhenAll(one, two);

        signIn.Refreshes.Should().Be(before + 1);
        results.Should().OnlyContain(result => result.Problem == null);
        results[0].Value.Should().Be(results[1].Value).And.NotBeNull();
        var stored = await world.Read(db => db.ModelConnectionCredentials.SingleAsync(RunWorld.Token));
        stored.Status.Should().Be(ModelSignInStatus.Active);
        stored.RenewedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task A_refresh_the_provider_says_is_over_marks_the_sign_in_and_a_new_sign_in_restores_it_on_the_same_connection()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var signIn = new FakeModelSignIn(ScriptedModelProvider.ConnectionKind) { Lifetime = TimeSpan.FromMinutes(2) };
        var host = world.IdleHost(services => services.AddSingleton<IModelSignIn>(signIn));
        await SignIn(world, host, signIn, world.UserId);
        var before = signIn.Refreshes;
        signIn.RefreshAnswer = FakeRefreshAnswer.Ended;

        var ended = await Read(world, host, world.UserId);
        var after = await Read(world, host, world.UserId);

        ended.Problem.Should().Be(ModelCredentialProblem.SignInEnded);
        after.Problem.Should().Be(ModelCredentialProblem.SignInEnded, "the row is kept and says so, with nothing sent again");
        signIn.Refreshes.Should().Be(before + 1);
        (await world.Read(db => db.ModelConnectionCredentials.SingleAsync(RunWorld.Token))).Status.Should().Be(ModelSignInStatus.NeedsSignIn);

        signIn.RefreshAnswer = FakeRefreshAnswer.Renewed;
        signIn.Lifetime = TimeSpan.FromHours(1);
        await SignIn(world, host, signIn, world.UserId);

        (await Read(world, host, world.UserId)).Problem.Should().BeNull();
        (await world.Read(db => db.ModelConnections.CountAsync(RunWorld.Token))).Should().Be(1, "signing in again uses the space's one connection of the kind");
        var restored = await world.Read(db => db.ModelConnectionCredentials.SingleAsync(RunWorld.Token));
        restored.Status.Should().Be(ModelSignInStatus.Active);
        (await world.Read(db => db.Secrets.CountAsync(secret => secret.Kind == SecretKind.OauthTokens, RunWorld.Token))).Should().Be(1, "the ended token set was deleted");
    }

    [Theory]
    [InlineData(FakeRefreshAnswer.ServerError)]
    [InlineData(FakeRefreshAnswer.Unreachable)]
    public async Task A_refresh_that_fails_for_a_reason_that_may_pass_leaves_the_sign_in_as_it_was(FakeRefreshAnswer answer)
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var signIn = new FakeModelSignIn(ScriptedModelProvider.ConnectionKind) { Lifetime = TimeSpan.FromMinutes(2) };
        var host = world.IdleHost(services => services.AddSingleton<IModelSignIn>(signIn));
        await SignIn(world, host, signIn, world.UserId);
        signIn.RefreshAnswer = answer;

        var result = await Read(world, host, world.UserId);

        result.Problem.Should().Be(ModelCredentialProblem.Unreachable);
        result.Message.Should().NotContainAny(signIn.Issued);
        (await world.Read(db => db.ModelConnectionCredentials.SingleAsync(RunWorld.Token))).Status.Should().Be(ModelSignInStatus.Active);
    }

    [Fact]
    public async Task A_refused_kind_is_refused_on_use_before_anything_is_read_or_sent()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var signIn = new FakeModelSignIn(ScriptedModelProvider.ConnectionKind) { Lifetime = TimeSpan.FromMinutes(2) };
        var open = world.IdleHost(services => services.AddSingleton<IModelSignIn>(signIn));
        await SignIn(world, open, signIn, world.UserId);
        var before = signIn.Refreshes;
        var hosted = world.IdleHost(services =>
        {
            services.AddSingleton<IModelSignIn>(signIn);
            services.AddSingleton<IOptions<DeploymentPolicyOptions>>(Options.Create(new DeploymentPolicyOptions { Rules = DeploymentPolicy.Hosted with { AllowedModelProviderKinds = [ScriptedModelProvider.ConnectionKind] } }));
        });
        var switchedOff = world.IdleHost(services =>
        {
            services.AddSingleton<IModelSignIn>(signIn);
            services.Configure<ModelSignInOptions>(options => options.SignInOff.Add(ScriptedModelProvider.ConnectionKind));
        });

        var onHosted = await Read(world, hosted, world.UserId);
        var offHere = await Read(world, switchedOff, world.UserId);

        onHosted.Problem.Should().Be(ModelCredentialProblem.NotAvailable);
        onHosted.Message.Should().Be(ModelCredentials.NotAvailable);
        offHere.Problem.Should().Be(ModelCredentialProblem.NotAvailable);
        signIn.Refreshes.Should().Be(before, "nothing is sent for a refused kind");
    }

    [Fact]
    public async Task A_sign_in_kind_the_provider_catalog_does_not_offer_is_refused_with_its_sentence_before_anything_is_sent()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var signIn = new FakeModelSignIn(ScriptedModelProvider.ConnectionKind) { Lifetime = TimeSpan.FromMinutes(2) };
        var unregistered = new FakeModelSignIn("test_no_provider");
        var open = world.IdleHost(services =>
        {
            services.AddSingleton<IModelSignIn>(signIn);
            services.AddSingleton<IModelSignIn>(unregistered);
        });
        await SignIn(world, open, signIn, world.UserId);
        var before = signIn.Refreshes;
        var listOnly = world.IdleHost(services =>
        {
            services.AddSingleton<IModelSignIn>(signIn);
            services.AddSingleton<IOptions<DeploymentPolicyOptions>>(Options.Create(new DeploymentPolicyOptions
            {
                Rules = DeploymentPolicy.SelfHosted with { ModelProviders = ModelProviderSet.OperatorList, AllowedModelProviderKinds = ["openai_compatible"] },
            }));
        });

        var noProvider = await open.Services.GetRequiredService<ModelSignIns>()
            .Begin(world.SpaceId, world.UserId, unregistered.Kind, FakeModelSignIn.RedirectUri, RunWorld.Token);
        var notOffered = await listOnly.Services.GetRequiredService<ModelSignIns>()
            .Begin(world.SpaceId, world.UserId, signIn.Kind, FakeModelSignIn.RedirectUri, RunWorld.Token);
        var use = await Read(world, listOnly, world.UserId);

        noProvider.Problem.Should().Be("This server has no provider of the kind test_no_provider.");
        unregistered.Begins.Should().Be(0);
        notOffered.Problem.Should().Be(ModelProviderCatalog.SwitchedOff(signIn.Kind));
        use.Problem.Should().Be(ModelCredentialProblem.NotAvailable);
        use.Message.Should().Be(ModelProviderCatalog.SwitchedOff(signIn.Kind));
        signIn.Refreshes.Should().Be(before, "nothing is sent for a kind the catalog does not offer");
    }

    #endregion

    #region Private Methods

    /// <summary>Signs the person in through the engine, as the routes do, approving on the fake provider's page.</summary>
    internal static async Task SignIn(RunWorld world, EngineHost host, FakeModelSignIn signIn, Guid userId)
    {
        var signIns = host.Services.GetRequiredService<ModelSignIns>();
        var begun = await signIns.Begin(world.SpaceId, userId, signIn.Kind, FakeModelSignIn.RedirectUri, RunWorld.Token);
        begun.Problem.Should().BeNull();
        var finished = await signIns.Finish(world.SpaceId, userId, begun.AttemptId, signIn.Approve(FakeModelSignIn.StateOf(begun.Address!)).Text, RunWorld.Token);
        finished.State.Should().Be(ModelSignInState.Done, finished.Problem);
    }

    /// <summary>The credential a call would send, read as the person in a transaction, then made ready with none open.</summary>
    private static async Task<ModelCredential> Read(RunWorld world, EngineHost host, Guid userId, bool ready = true)
    {
        var credentials = host.Services.GetRequiredService<ModelCredentials>();
        var read = await host.Services.GetRequiredService<RunScopes>().For(world.SpaceId, userId).Transact(
            async services =>
            {
                var connection = await services.GetRequiredService<ModelConnectionRepository>().FindConnection(world.ConnectionId, RunWorld.Token);
                return await credentials.Read(services, connection!, RunWorld.Token);
            },
            RunWorld.Token);
        return ready ? await credentials.Ready(read, RunWorld.Token) : read;
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (!condition())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the condition held in time");
            await Task.Delay(20, RunWorld.Token);
        }
    }

    #endregion

    #region Nested Types

    /// <summary>Every secret id revealed through the vault, in any flow.</summary>
    private sealed class RevealLog
    {
        #region Private Fields

        private readonly ConcurrentQueue<Guid> revealed = new();

        #endregion

        #region Properties

        public IReadOnlyList<Guid> Revealed => [.. revealed];

        #endregion

        #region Public Methods

        public void Add(Guid secretId) => revealed.Enqueue(secretId);

        public void Clear() => revealed.Clear();

        #endregion
    }

    /// <summary>The real vault, noting each secret it reveals.</summary>
    private sealed class RecordingVault(ISecretVault inner, RevealLog log) : ISecretVault
    {
        #region Interface Implementations

        public Task<Guid> Store(SecretKind kind, string value, Guid? privateToUserId, CancellationToken cancellationToken) =>
            inner.Store(kind, value, privateToUserId, cancellationToken);

        public Task Replace(Guid secretId, string value, CancellationToken cancellationToken) => inner.Replace(secretId, value, cancellationToken);

        public Task<string> Reveal(Guid secretId, CancellationToken cancellationToken)
        {
            log.Add(secretId);
            return inner.Reveal(secretId, cancellationToken);
        }

        public Task Delete(Guid secretId, CancellationToken cancellationToken) => inner.Delete(secretId, cancellationToken);

        #endregion
    }

    #endregion
}
