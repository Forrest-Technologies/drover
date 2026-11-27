namespace Drover.Engine.Tests.Runs;

/// <summary>
/// Signing in from a device that cannot listen, on a clock the test moves. By a code the server polls for: the server
/// asks the provider at its interval and never sooner, waits five seconds longer when told to slow down, keeps asking
/// through a failure that may pass, finishes by itself when the person approves, and stops when the code runs out or a
/// later attempt takes its place; the device cannot finish it. By a pasted code: one that does not work can be pasted
/// again a few times, and the right one finishes the sign-in.
/// </summary>
public sealed class ModelSignInByCodeTests(PostgresServer postgres)
{
    #region Polling

    [Fact]
    public async Task A_code_is_polled_at_the_providers_interval_never_sooner_and_slower_when_asked()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var (clock, signIn, signIns) = Rig(world, ModelSignInWait.Poll);
        var begun = await signIns.Begin(world.SpaceId, world.UserId, signIn.Kind, null, RunWorld.Token);

        begun.Problem.Should().BeNull();
        begun.Wait.Should().Be(ModelSignInWait.Poll);
        begun.Code.Should().Be(FakeModelSignIn.DeviceCode);
        begun.Address.Should().Be(FakeModelSignIn.DevicePage);

        await Advance(clock, TimeSpan.FromSeconds(4));
        signIn.Polls.Should().BeEmpty("the provider's interval has not passed");
        await Advance(clock, TimeSpan.FromSeconds(1));
        await Until(() => signIn.Polls.Count == 1);

        signIn.SlowDownOnce = true;
        await Advance(clock, TimeSpan.FromSeconds(5));
        await Until(() => signIn.Polls.Count == 2);
        await Advance(clock, TimeSpan.FromSeconds(9));
        signIn.Polls.Should().HaveCount(2, "a slow-down adds five seconds to the wait");
        await Advance(clock, TimeSpan.FromSeconds(1));
        await Until(() => signIn.Polls.Count == 3);

        signIns.Read(world.SpaceId, world.UserId, begun.AttemptId).State.Should().Be(ModelSignInState.Waiting);
        signIn.Polls.Zip(signIn.Polls.Skip(1), (earlier, later) => later - earlier).Should().AllSatisfy(gap => gap.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Approving_the_code_finishes_the_sign_in_by_itself_with_a_ready_connection()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var (clock, signIn, signIns) = Rig(world, ModelSignInWait.Poll);
        var begun = await signIns.Begin(world.SpaceId, world.UserId, signIn.Kind, null, RunWorld.Token);
        signIn.UnreachableOnce = true;
        await Advance(clock, TimeSpan.FromSeconds(5));
        await Until(() => signIn.Polls.Count == 1);
        signIns.Read(world.SpaceId, world.UserId, begun.AttemptId).State.Should().Be(ModelSignInState.Waiting, "a failure that may pass leaves the code waiting");

        signIn.DeviceApproved = true;
        await Advance(clock, TimeSpan.FromSeconds(5));
        await Until(() => signIns.Read(world.SpaceId, world.UserId, begun.AttemptId).State is ModelSignInState.Done);

        var outcome = signIns.Read(world.SpaceId, world.UserId, begun.AttemptId);
        outcome.ConnectionId.Should().Be(world.ConnectionId);
        var credential = await world.Read(db => db.ModelConnectionCredentials.SingleAsync(RunWorld.Token));
        credential.AudienceUserId.Should().Be(world.UserId);
        credential.Status.Should().Be(ModelSignInStatus.Active);
        await Advance(clock, TimeSpan.FromSeconds(30));
        signIn.Polls.Should().HaveCount(2, "a finished sign-in is not polled again");
    }

    [Fact]
    public async Task A_code_that_runs_out_ends_as_expired_and_is_not_polled_again()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var (clock, signIn, signIns) = Rig(world, ModelSignInWait.Poll);
        signIn.CodeLifetime = TimeSpan.FromSeconds(12);
        var begun = await signIns.Begin(world.SpaceId, world.UserId, signIn.Kind, null, RunWorld.Token);

        await Advance(clock, TimeSpan.FromSeconds(5));
        await Until(() => signIn.Polls.Count == 1);
        await Advance(clock, TimeSpan.FromSeconds(5));
        await Until(() => signIn.Polls.Count == 2);
        await Advance(clock, TimeSpan.FromSeconds(5));

        await Until(() => signIns.Read(world.SpaceId, world.UserId, begun.AttemptId).State is ModelSignInState.Expired);
        await Advance(clock, TimeSpan.FromSeconds(10));
        signIn.Polls.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_refusal_on_the_providers_page_ends_the_attempt()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var (clock, signIn, signIns) = Rig(world, ModelSignInWait.Poll);
        var begun = await signIns.Begin(world.SpaceId, world.UserId, signIn.Kind, null, RunWorld.Token);
        signIn.DeviceRefused = true;

        await Advance(clock, TimeSpan.FromSeconds(5));

        await Until(() => signIns.Read(world.SpaceId, world.UserId, begun.AttemptId).State is ModelSignInState.Refused);
        (await world.Read(db => db.ModelConnectionCredentials.CountAsync(RunWorld.Token))).Should().Be(0);
    }

    [Fact]
    public async Task The_device_cannot_finish_a_code_the_server_polls_for_and_another_person_cannot_read_it()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var (_, signIn, signIns) = Rig(world, ModelSignInWait.Poll);
        var memberId = await world.AddMember(MembershipRole.Member);
        var begun = await signIns.Begin(world.SpaceId, world.UserId, signIn.Kind, null, RunWorld.Token);

        var finished = await signIns.Finish(world.SpaceId, world.UserId, begun.AttemptId, "code=anything", RunWorld.Token);

        finished.Status.Should().Be(400);
        finished.State.Should().Be(ModelSignInState.Waiting);
        signIn.Polls.Should().BeEmpty("nothing the device sends reaches the provider");
        signIns.Read(world.SpaceId, memberId, begun.AttemptId).Should().Be(ModelSignInOutcome.NotFound);
        (await signIns.Finish(world.SpaceId, memberId, begun.AttemptId, null, RunWorld.Token)).Should().Be(ModelSignInOutcome.NotFound);
    }

    [Fact]
    public async Task A_later_attempt_takes_the_first_ones_place_and_only_it_is_polled()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var (clock, signIn, signIns) = Rig(world, ModelSignInWait.Poll);
        var first = await signIns.Begin(world.SpaceId, world.UserId, signIn.Kind, null, RunWorld.Token);
        var second = await signIns.Begin(world.SpaceId, world.UserId, signIn.Kind, null, RunWorld.Token);

        await Advance(clock, TimeSpan.FromSeconds(5));
        await Until(() => signIn.Polls.Count >= 1);
        await Task.Delay(TimeSpan.FromMilliseconds(300), RunWorld.Token);

        signIn.Polls.Should().HaveCount(1, "the replaced attempt stopped polling");
        signIns.Read(world.SpaceId, world.UserId, first.AttemptId).Should().Be(ModelSignInOutcome.NotFound);
        signIns.Read(world.SpaceId, world.UserId, second.AttemptId).State.Should().Be(ModelSignInState.Waiting);
    }

    [Fact]
    public async Task A_kind_with_no_flow_by_code_refuses_a_device_that_cannot_listen()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var (_, signIn, signIns) = Rig(world, codeWait: null);

        var begun = await signIns.Begin(world.SpaceId, world.UserId, signIn.Kind, null, RunWorld.Token);

        begun.Status.Should().Be(400);
        signIn.Begins.Should().Be(0, "nothing is asked of the provider");
    }

    #endregion

    #region Pasting

    [Fact]
    public async Task A_pasted_code_that_does_not_work_can_be_pasted_again_and_the_right_one_finishes()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var (_, signIn, signIns) = Rig(world, ModelSignInWait.Paste);
        var begun = await signIns.Begin(world.SpaceId, world.UserId, signIn.Kind, null, RunWorld.Token);
        begun.Wait.Should().Be(ModelSignInWait.Paste);
        var state = FakeModelSignIn.StateOf(begun.Address!);

        var wrong = await signIns.Finish(world.SpaceId, world.UserId, begun.AttemptId, "code=typo&state=another", RunWorld.Token);

        wrong.Status.Should().Be(400);
        wrong.State.Should().Be(ModelSignInState.Waiting, "the person can paste again from the same page");
        var right = await signIns.Finish(world.SpaceId, world.UserId, begun.AttemptId, signIn.Approve(state).Text, RunWorld.Token);
        right.State.Should().Be(ModelSignInState.Done);
        right.ConnectionId.Should().Be(world.ConnectionId);
    }

    [Fact]
    public async Task An_attempt_takes_only_a_few_pasted_codes_that_do_not_work()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var (_, signIn, signIns) = Rig(world, ModelSignInWait.Paste);
        var begun = await signIns.Begin(world.SpaceId, world.UserId, signIn.Kind, null, RunWorld.Token);
        var last = ModelSignInOutcome.NotFound;

        for (var paste = 0; paste < ModelSignIns.MostPastes; paste++)
        {
            last = await signIns.Finish(world.SpaceId, world.UserId, begun.AttemptId, "code=typo&state=another", RunWorld.Token);
        }

        last.State.Should().Be(ModelSignInState.Refused);
        var late = await signIns.Finish(world.SpaceId, world.UserId, begun.AttemptId, signIn.Approve(FakeModelSignIn.StateOf(begun.Address!)).Text, RunWorld.Token);
        late.State.Should().Be(ModelSignInState.Refused, "an ended attempt takes no more codes");
    }

    #endregion

    #region Private Methods

    private static (FakeTimeProvider Clock, FakeModelSignIn SignIn, ModelSignIns SignIns) Rig(RunWorld world, ModelSignInWait? codeWait)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var signIn = new FakeModelSignIn(ScriptedModelProvider.ConnectionKind, time: clock) { CodeWait = codeWait };
        var host = world.IdleHost(services =>
        {
            services.AddSingleton<IModelSignIn>(signIn);
            services.AddSingleton<TimeProvider>(clock);
        });
        return (clock, signIn, host.Services.GetRequiredService<ModelSignIns>());
    }

    /// <summary>Moves the clock and lets a poll it woke reach the provider.</summary>
    private static async Task Advance(FakeTimeProvider clock, TimeSpan by)
    {
        clock.Advance(by);
        await Task.Delay(TimeSpan.FromMilliseconds(150), RunWorld.Token);
    }

    /// <summary>Waits, in real time, for the background poll to get somewhere.</summary>
    private static async Task Until(Func<bool> done)
    {
        for (var tries = 0; tries < 200 && !done(); tries++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), RunWorld.Token);
        }

        done().Should().BeTrue("the background poll got there in time");

        // The poll goes on to wait for its next ask on the test's clock; let it, before the clock moves again.
        await Task.Delay(TimeSpan.FromMilliseconds(400), RunWorld.Token);
    }

    #endregion
}
