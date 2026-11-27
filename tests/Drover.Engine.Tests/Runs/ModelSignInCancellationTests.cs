namespace Drover.Engine.Tests.Runs;

/// <summary>
/// Tokens the provider has already handed out are kept whatever happens to the call that asked for them: a refresh
/// spends the old refresh token and an exchange spends the one-time code, so dropping the answer would end the
/// sign-in. Signing out waits for a renewal in flight. A run that loses its sign-in partway stops with the reason.
/// </summary>
public sealed class ModelSignInCancellationTests(PostgresServer postgres)
{
    #region Keeping What Was Handed Out

    [Fact]
    public async Task A_refresh_cancelled_after_the_provider_rotated_the_tokens_still_keeps_the_new_set_and_the_next_call_uses_it()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var signIn = new FakeModelSignIn(ScriptedModelProvider.ConnectionKind) { Lifetime = TimeSpan.FromMinutes(2) };
        var host = world.IdleHost(services => services.AddSingleton<IModelSignIn>(signIn));
        await ModelCredentialsTests.SignIn(world, host, signIn, world.UserId);
        var credentials = host.Services.GetRequiredService<ModelCredentials>();
        var read = await Read(world, host);
        var before = signIn.Refreshes;
        using var cancel = new CancellationTokenSource();
        signIn.AfterTokens = cancel.Cancel;
        signIn.Lifetime = TimeSpan.FromHours(1);

        var renewing = () => credentials.Ready(read, cancel.Token);
        await renewing.Should().NotThrowAsync<SignInEndedException>();
        signIn.AfterTokens = null;
        var rotated = signIn.Issued[^2];

        var next = await credentials.Ready(await Read(world, host), RunWorld.Token);

        next.Problem.Should().BeNull();
        next.Value.Should().Be(rotated, "the rotated set was stored although the call was cancelled");
        signIn.Refreshes.Should().Be(before + 1, "the stored set lasts, so nothing is sent again, and no spent token ever is");
        (await world.Read(db => db.ModelConnectionCredentials.SingleAsync(RunWorld.Token))).Status.Should().Be(ModelSignInStatus.Active);
    }

    [Fact]
    public async Task A_finish_cancelled_after_the_exchange_still_keeps_the_tokens_and_the_sign_in_is_done()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var signIn = new FakeModelSignIn(ScriptedModelProvider.ConnectionKind);
        var host = world.IdleHost(services => services.AddSingleton<IModelSignIn>(signIn));
        var signIns = host.Services.GetRequiredService<ModelSignIns>();
        var begun = await signIns.Begin(world.SpaceId, world.UserId, signIn.Kind, FakeModelSignIn.RedirectUri, RunWorld.Token);
        using var cancel = new CancellationTokenSource();
        signIn.AfterTokens = cancel.Cancel;

        var finishing = () => signIns.Finish(world.SpaceId, world.UserId, begun.AttemptId, signIn.Approve(FakeModelSignIn.StateOf(begun.Address!)).Text, cancel.Token);
        // Only the listing of the models, after the tokens were kept, may be cut short by the cancelled request.
        await finishing.Should().ThrowAsync<OperationCanceledException>();

        signIn.AfterTokens = null;
        signIns.Read(world.SpaceId, world.UserId, begun.AttemptId).State.Should().Be(ModelSignInState.Done);
        var credential = await world.Read(db => db.ModelConnectionCredentials.SingleAsync(RunWorld.Token));
        credential.Status.Should().Be(ModelSignInStatus.Active);
        (await Read(world, host)).Value.Should().Be(signIn.Issued[0], "the exchanged tokens were stored");
    }

    #endregion

    #region Signing Out

    [Fact]
    public async Task Signing_out_waits_for_a_renewal_in_flight_so_no_new_tokens_outlive_it()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var signIn = new FakeModelSignIn(ScriptedModelProvider.ConnectionKind);
        var host = world.IdleHost(services => services.AddSingleton<IModelSignIn>(signIn));
        await ModelCredentialsTests.SignIn(world, host, signIn, world.UserId);
        var credentialId = await world.Read(db => db.ModelConnectionCredentials.Select(credential => credential.Id).SingleAsync(RunWorld.Token));
        var renewal = await host.Services.GetRequiredService<ModelCredentialLocks>().Hold(credentialId, RunWorld.Token);

        var signingOut = host.Services.GetRequiredService<ModelSignIns>().SignOut(world.SpaceId, world.UserId, world.ConnectionId, RunWorld.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(500), RunWorld.Token);

        signingOut.IsCompleted.Should().BeFalse("sign-out waits for the renewal's lock");
        signIn.Revoked.Should().BeEmpty();
        await renewal.DisposeAsync();
        (await signingOut).Should().BeTrue();
        signIn.Revoked.Should().ContainSingle();
        (await world.Read(db => db.ModelConnectionCredentials.CountAsync(RunWorld.Token))).Should().Be(0);
    }

    #endregion

    #region Losing The Sign-In During A Run

    [Theory]
    [InlineData(FakeRefreshAnswer.Ended, RunReasons.SignInEnded)]
    [InlineData(FakeRefreshAnswer.ServerError, "unreachable")]
    public async Task A_run_that_loses_its_sign_in_partway_stops_with_the_reason_after_one_model_call_and_no_token_in_its_events(
        FakeRefreshAnswer answer,
        string code)
    {
        var tools = new TestTools().Add("test.lookup", ToolSensitivity.Read, """{"answer":42}""");
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new ToolCallOutput("c1", "test_lookup")], [new TextOutput("It is 42.")]), tools);
        var signIn = new FakeModelSignIn(ScriptedModelProvider.ConnectionKind) { Lifetime = ModelCredentials.RefreshLead + TimeSpan.FromSeconds(6) };
        var host = world.StartHost(replaceServices: services => services.AddSingleton<IModelSignIn>(signIn));
        await ModelCredentialsTests.SignIn(world, host, signIn, world.UserId);
        var callsBefore = world.Model.KeysSeen.Count;

        // During the first call the provider stops renewing, and the call takes long enough that the token is due.
        world.Model.BeforeCall = call =>
        {
            if (call != 0)
            {
                return Task.CompletedTask;
            }

            signIn.RefreshAnswer = answer;
            return Task.Delay(TimeSpan.FromSeconds(7), RunWorld.Token);
        };
        var threadId = await world.NewThread();
        var runId = await world.Post(threadId, "What is it?");
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Failed);
        run.Error!.Code.Should().Be(code);
        world.Model.KeysSeen.Count.Should().Be(callsBefore + 1, "the call after the sign-in was lost is never made");
        JsonSerializer.Serialize(await world.ReadEvents(runId)).Should().NotContainAny(signIn.Issued);
        JsonSerializer.Serialize(await world.ReadMessages(threadId)).Should().NotContainAny(signIn.Issued);
        (await world.Read(db => db.ModelConnectionCredentials.SingleAsync(RunWorld.Token))).Status.Should().Be(
            answer is FakeRefreshAnswer.Ended ? ModelSignInStatus.NeedsSignIn : ModelSignInStatus.Active);
    }

    #endregion

    #region Private Methods

    private static Task<ModelCredential> Read(RunWorld world, EngineHost host)
    {
        var credentials = host.Services.GetRequiredService<ModelCredentials>();
        return host.Services.GetRequiredService<RunScopes>().For(world.SpaceId, world.UserId).Transact(
            async services =>
            {
                var connection = await services.GetRequiredService<ModelConnectionRepository>().FindConnection(world.ConnectionId, RunWorld.Token);
                return await credentials.Read(services, connection!, RunWorld.Token);
            },
            RunWorld.Token);
    }

    #endregion
}
