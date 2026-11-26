namespace Drover.Engine.Tests.Models;

/// <summary>
/// A device-code sign-in through the engine: the attempt is polled until the person approves, and a client may
/// finish it early once they have.
/// </summary>
public sealed class ModelSignInPollTests(PostgresServer postgres)
{
    #region Polling

    [Fact]
    public async Task A_poll_stays_waiting_until_the_person_approves_then_stores_the_tokens()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var signIn = new FakeModelSignIn(ScriptedModelProvider.ConnectionKind)
        {
            Wait = ModelSignInWait.Poll,
            UserCode = "WXYZ-1234",
            PollGap = TimeSpan.FromSeconds(5),
        };
        var host = world.IdleHost(services => services.AddSingleton<IModelSignIn>(signIn));
        var signIns = host.Services.GetRequiredService<ModelSignIns>();

        var begun = await signIns.Begin(world.SpaceId, world.UserId, signIn.Kind, null, RunWorld.Token);

        begun.Wait.Should().Be(ModelSignInWait.Poll);
        begun.Code.Should().Be("WXYZ-1234");
        begun.Address!.Scheme.Should().Be("https");
        signIns.Read(world.SpaceId, world.UserId, begun.AttemptId).State.Should().Be(ModelSignInState.Waiting);

        signIn.DeviceApproved = true;
        var finished = await signIns.Finish(world.SpaceId, world.UserId, begun.AttemptId, string.Empty, RunWorld.Token);

        finished.State.Should().Be(ModelSignInState.Done, finished.Problem);
        finished.ConnectionId.Should().NotBeNull();
        (await world.Read(db => db.ModelConnectionCredentials.SingleAsync(RunWorld.Token))).Status.Should().Be(ModelSignInStatus.Active);
    }

    [Fact]
    public async Task The_background_poll_finishes_the_sign_in_once_the_wait_elapses()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var clock = new FakeTimeProvider();
        var signIn = new FakeModelSignIn(ScriptedModelProvider.ConnectionKind, time: clock)
        {
            Wait = ModelSignInWait.Poll,
            PollGap = TimeSpan.FromSeconds(5),
        };
        var host = world.IdleHost(services =>
        {
            services.AddSingleton<IModelSignIn>(signIn);
            services.AddSingleton<TimeProvider>(clock);
        });
        var signIns = host.Services.GetRequiredService<ModelSignIns>();
        var begun = await signIns.Begin(world.SpaceId, world.UserId, signIn.Kind, null, RunWorld.Token);
        signIn.DeviceApproved = true;
        await Task.Delay(50, RunWorld.Token);

        clock.Advance(TimeSpan.FromSeconds(6));
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (signIns.Read(world.SpaceId, world.UserId, begun.AttemptId).State is ModelSignInState.Waiting
            && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, RunWorld.Token);
        }

        signIns.Read(world.SpaceId, world.UserId, begun.AttemptId).State.Should().Be(ModelSignInState.Done);
    }

    [Fact]
    public async Task A_poll_may_start_with_no_return_address()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var signIn = new FakeModelSignIn(ScriptedModelProvider.ConnectionKind) { Wait = ModelSignInWait.Poll };
        var host = world.IdleHost(services => services.AddSingleton<IModelSignIn>(signIn));

        var begun = await host.Services.GetRequiredService<ModelSignIns>()
            .Begin(world.SpaceId, world.UserId, signIn.Kind, null, RunWorld.Token);

        begun.Problem.Should().BeNull();
        begun.Status.Should().Be(200);
    }

    #endregion
}
