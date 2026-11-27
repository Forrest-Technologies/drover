namespace Drover.Engine.Tests.Runs;

/// <summary>
/// Runs on a connection each person signs in to, end to end: a token that runs out while a run goes on is renewed
/// before the next model call; a routine acts with its owner's sign-in and a delegated run with its parent's person's;
/// a connection test and a refresh renew a token that runs out soon; and no token reaches the run's events or the log.
/// </summary>
public sealed class ModelSignInRunTests(PostgresServer postgres)
{
    #region Renewing During A Run

    [Fact]
    public async Task A_token_that_runs_out_while_a_run_goes_on_is_renewed_and_the_later_call_carries_the_new_one()
    {
        var tools = new TestTools().Add("test.lookup", ToolSensitivity.Read, """{"answer":42}""");
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new ToolCallOutput("c1", "test_lookup")], [new TextOutput("It is 42.")]), tools);
        var signIn = new FakeModelSignIn(ScriptedModelProvider.ConnectionKind) { Lifetime = ModelCredentials.RefreshLead + TimeSpan.FromSeconds(3) };
        var host = world.StartHost(replaceServices: services => services.AddSingleton<IModelSignIn>(signIn));
        await ModelCredentialsTests.SignIn(world, host, signIn, world.UserId);
        var callsBefore = world.Model.KeysSeen.Count;

        // The first model call takes long enough that the token the run started with is within the lead after it.
        world.Model.BeforeCall = call => call == 0 ? Task.Delay(TimeSpan.FromSeconds(4), RunWorld.Token) : Task.CompletedTask;
        var threadId = await world.NewThread();
        var runId = await world.Post(threadId, "What is it?");
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        List<string?> keys = [.. world.Model.KeysSeen.Skip(callsBefore)];
        keys.Should().HaveCount(2, "each model call is given its own credential");
        keys.Should().OnlyContain(key => key != null && signIn.Issued.Contains(key));
        keys[1].Should().NotBe(keys[0], "the later call carries the renewed token");
        signIn.Refreshes.Should().BeGreaterThanOrEqualTo(1);

        await host.Services.GetRequiredService<ModelSignIns>().SignOut(world.SpaceId, world.UserId, world.ConnectionId, RunWorld.Token);
        var events = JsonSerializer.Serialize(await world.ReadEvents(runId));
        var messages = JsonSerializer.Serialize(await world.ReadMessages(threadId));
        events.Should().NotContainAny(signIn.Issued, "no token reaches the run's events");
        messages.Should().NotContainAny(signIn.Issued);
        host.Log.Tail().Should().NotContainAny(signIn.Issued, "no token reaches the log, across sign-in, refresh, a run and sign-out");
        signIn.Revoked.Should().ContainSingle();
    }

    #endregion

    #region Whose Sign-In

    [Fact]
    public async Task A_routine_acts_with_its_owners_sign_in_and_not_with_anyone_elses()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("Here is the brief.")]));
        var signIn = new FakeModelSignIn(ScriptedModelProvider.ConnectionKind);
        var host = world.StartHost(replaceServices: services => services.AddSingleton<IModelSignIn>(signIn));
        var ownerId = await world.AddMember(MembershipRole.Admin);
        await ModelCredentialsTests.SignIn(world, host, signIn, world.UserId);
        List<string> spaceOwners = [.. signIn.Issued];
        await ModelCredentialsTests.SignIn(world, host, signIn, ownerId);
        var routineOwners = signIn.Issued.Except(spaceOwners).ToList();
        var schedule = await AsPerson(world, host, ownerId, services => services.GetRequiredService<ScheduleService>()
            .Create(world.BotId, new ScheduleEdit("0 9 * * *", "UTC", "Brief me.", ScheduleThreadMode.NewThread, true), ownerId, RunWorld.Token));
        schedule.Problem.Should().BeNull();
        var callsBefore = world.Model.KeysSeen.Count;

        var trial = await RunOnce(world, host, ownerId, schedule.Schedule!.Id);
        trial.Problem.Should().BeNull();
        var run = await world.WaitForStatus(trial.RunId!.Value, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        run.PrincipalUserId.Should().Be(ownerId);
        world.Model.KeysSeen.Skip(callsBefore).Should().NotBeEmpty().And.OnlyContain(key => key != null && routineOwners.Contains(key));
    }

    [Fact]
    public async Task A_delegated_run_acts_with_its_parents_persons_sign_in_and_one_with_none_is_told_to_sign_in()
    {
        var script = new ScriptedModel();
        await using var world = await RunWorld.Create(postgres, script);
        var childId = await world.NewBotBeneath(world.BotId, "Finder", "Looks things up.");
        await world.Grant(DelegateTool.Name, GrantEffect.Allow);
        var signIn = new FakeModelSignIn(ScriptedModelProvider.ConnectionKind);
        var host = world.StartHost(replaceServices: services => services.AddSingleton<IModelSignIn>(signIn));
        var memberId = await world.AddMember(MembershipRole.Member);
        var withoutId = await world.AddMember(MembershipRole.Member);
        await ModelCredentialsTests.SignIn(world, host, signIn, world.UserId);
        List<string> spaceOwners = [.. signIn.Issued];
        await ModelCredentialsTests.SignIn(world, host, signIn, memberId);
        var members = signIn.Issued.Except(spaceOwners).ToList();
        world.Script.Then(
            [new ToolCallOutput("d1", DelegateTool.Name, JsonSerializer.Serialize(new { bot_id = childId, task = "Find it." }))],
            [new TextOutput("Found it.")],
            [new TextOutput("Finder found it.")]);
        var callsBefore = world.Model.KeysSeen.Count;
        var threadId = await ThreadOf(world, memberId);

        var post = await world.PostRaw(threadId, "Ask Finder.", asUser: memberId);
        var root = await world.WaitForStatus(post.Run!.Id, RunStatus.Completed, RunStatus.Failed);

        root.Status.Should().Be(RunStatus.Completed, root.Error?.Message);
        var child = (await world.TransactAs(world.SpaceId, memberId, services => services.GetRequiredService<DroverDbContext>().Runs.AsNoTracking()
            .Where(run => run.RootRunId == root.Id && run.Id != root.Id).ToListAsync(RunWorld.Token))).Should().ContainSingle().Subject;
        child.PrincipalUserId.Should().Be(memberId);
        List<string?> keys = [.. world.Model.KeysSeen.Skip(callsBefore)];
        keys.Should().HaveCount(3, "the parent's two calls and the child's one");
        keys.Should().OnlyContain(key => key != null && members.Contains(key), "the child acts for the parent's person");

        var callsThen = world.Model.KeysSeen.Count;
        var withoutThread = await ThreadOf(world, withoutId);
        var refused = await world.PostRaw(withoutThread, "Ask Finder.", asUser: withoutId);
        var failed = await world.WaitForStatus(refused.Run!.Id, RunStatus.Completed, RunStatus.Failed);

        failed.Status.Should().Be(RunStatus.Failed);
        failed.Error!.Code.Should().Be(RunReasons.SignInRequired);
        world.Model.KeysSeen.Count.Should().Be(callsThen, "no model call, and so no delegation, is made for a person with no sign-in");
    }

    #endregion

    #region Connection Calls

    [Fact]
    public async Task A_test_and_a_refresh_with_a_token_that_runs_out_soon_each_renew_it_once_and_succeed_and_a_sign_in_lists_the_models()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var signIn = new FakeModelSignIn(ScriptedModelProvider.ConnectionKind) { Lifetime = TimeSpan.FromMinutes(2) };
        var host = world.IdleHost(services => services.AddSingleton<IModelSignIn>(signIn));

        await ModelCredentialsTests.SignIn(world, host, signIn, world.UserId);

        signIn.Refreshes.Should().Be(1, "listing the models after the sign-in renewed the short-lived token first");
        host.Log.Tail().Should().NotContain("could not be listed after a sign-in");

        ProviderCheck<ConnectionTestResult>? test = null;
        ModelRefresh? refresh = null;
        var tenant = host.Services.GetRequiredService<RunScopes>().For(world.SpaceId, world.UserId);
        await tenant.InScope(async services => test = await services.GetRequiredService<ModelConnectionService>().Test(world.ConnectionId, RunWorld.Token));
        signIn.Refreshes.Should().Be(2);
        await tenant.InScope(async services => refresh = await services.GetRequiredService<ModelConnectionService>().RefreshModels(world.ConnectionId, RunWorld.Token));

        test!.Result!.Succeeded.Should().BeTrue(test.Message);
        refresh!.Failure.Should().BeNull(refresh.Message);
        signIn.Refreshes.Should().Be(3, "each call renewed the token once");
    }

    #endregion

    #region Private Methods

    private static Task<Guid> ThreadOf(RunWorld world, Guid userId) =>
        world.TransactAs(world.SpaceId, userId, async services =>
            (await services.GetRequiredService<ThreadService>().Create(world.BotId, "Mine", userId, RunWorld.Token))!.Id);

    private static async Task<ScheduleTrial> RunOnce(RunWorld world, EngineHost host, Guid userId, Guid scheduleId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, userId);
        return await scope.ServiceProvider.GetRequiredService<ScheduleFiring>().RunOnce(world.BotId, scheduleId, userId, RunWorld.Token);
    }

    private static async Task<T> AsPerson<T>(RunWorld world, EngineHost host, Guid userId, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, userId);
        await using var transaction = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(RunWorld.Token);
        var result = await work(scope.ServiceProvider);
        await transaction.Commit(RunWorld.Token);
        return result;
    }

    #endregion
}
