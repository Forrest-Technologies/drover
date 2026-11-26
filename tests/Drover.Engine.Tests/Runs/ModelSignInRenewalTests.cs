namespace Drover.Engine.Tests.Runs;

/// <summary>
/// The hourly pass that renews people's model sign-ins that sat unused, on a clock the test moves: one idle for a week
/// is renewed as its own person, one renewed yesterday is left alone, and a kind this server refuses is not sent for.
/// </summary>
public sealed class ModelSignInRenewalTests(PostgresServer postgres)
{
    #region Public Methods

    [Fact]
    public async Task A_sign_in_idle_for_seven_days_is_renewed_and_one_renewed_yesterday_is_left_alone()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var signIn = new FakeModelSignIn(ScriptedModelProvider.ConnectionKind, time: clock);
        var host = world.IdleHost(services =>
        {
            services.AddSingleton<IModelSignIn>(signIn);
            services.AddSingleton<TimeProvider>(clock);
        });
        var memberId = await world.AddMember(MembershipRole.Member);
        await ModelCredentialsTests.SignIn(world, host, signIn, world.UserId);
        clock.Advance(TimeSpan.FromDays(7));
        await ModelCredentialsTests.SignIn(world, host, signIn, memberId);
        clock.Advance(TimeSpan.FromDays(1));
        var membersBefore = await RenewedAt(world, memberId);

        var renewed = await host.Services.GetRequiredService<ModelSignInRenewal>().RenewDue(RunWorld.Token);

        renewed.Should().Be(1);
        signIn.Refreshes.Should().Be(1);
        (await RenewedAt(world, world.UserId))!.Value.Should().BeCloseTo(clock.GetUtcNow(), TimeSpan.FromMilliseconds(1), "the owner's sign-in sat unused for eight days");
        (await RenewedAt(world, memberId)).Should().Be(membersBefore, "the member's was renewed yesterday");
    }

    [Fact]
    public async Task A_pass_sends_nothing_for_a_kind_this_server_refuses()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var signIn = new FakeModelSignIn(ScriptedModelProvider.ConnectionKind, time: clock);
        var open = world.IdleHost(services =>
        {
            services.AddSingleton<IModelSignIn>(signIn);
            services.AddSingleton<TimeProvider>(clock);
        });
        await ModelCredentialsTests.SignIn(world, open, signIn, world.UserId);
        clock.Advance(TimeSpan.FromDays(8));
        var switchedOff = world.IdleHost(services =>
        {
            services.AddSingleton<IModelSignIn>(signIn);
            services.AddSingleton<TimeProvider>(clock);
            services.Configure<ModelSignInOptions>(options => options.SignInOff.Add(ScriptedModelProvider.ConnectionKind));
        });

        var renewed = await switchedOff.Services.GetRequiredService<ModelSignInRenewal>().RenewDue(RunWorld.Token);

        renewed.Should().Be(0);
        signIn.Refreshes.Should().Be(0);
    }

    #endregion

    #region Private Methods

    private static Task<DateTimeOffset?> RenewedAt(RunWorld world, Guid userId) =>
        world.TransactAs(world.SpaceId, userId, async services =>
            (await services.GetRequiredService<DroverDbContext>().ModelConnectionCredentials.SingleAsync(RunWorld.Token)).RenewedAt);

    #endregion
}
