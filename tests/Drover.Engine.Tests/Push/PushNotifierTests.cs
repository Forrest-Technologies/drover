namespace Drover.Engine.Tests.Push;

/// <summary>
/// When a person's devices are woken, and with what: one push per finished, failed, or waiting run, none while the
/// person has the space open, none for a bot they silenced, and never another person's run. A real database, real
/// runs, and a push service stand-in.
/// </summary>
public sealed class PushNotifierTests(PostgresServer postgres)
{
    #region Private Fields

    private const string OwnersDevice = "https://push.example/owner";

    private const string MembersDevice = "https://push.example/member";

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    #endregion

    #region One Push Each

    [Fact]
    public async Task A_finished_run_wakes_the_persons_device_once_with_the_kind_and_the_thread()
    {
        var push = new FakePushChannel();
        await using var world = await World(push, new ScriptedModel([new TextOutput("Done.")]));
        await TurnOn(world, world.UserId, OwnersDevice);
        var threadId = await world.NewThread();

        await world.WaitForStatus(await world.Post(threadId, "Hello"), RunStatus.Completed);

        (await PushesTo(push, OwnersDevice, 1)).Should().Equal(new PushWakeUp(PushKinds.RunFinished, threadId));
    }

    [Fact]
    public async Task A_failed_run_wakes_the_persons_device_once()
    {
        var push = new FakePushChannel();
        await using var world = await World(push, new ScriptedModel([new TextOutput("Never sent.")]));
        world.Model.FailNext = ModelProviderException.FromStatus(402);
        await TurnOn(world, world.UserId, OwnersDevice);
        var threadId = await world.NewThread();

        await world.WaitForStatus(await world.Post(threadId, "Hello"), RunStatus.Failed);

        (await PushesTo(push, OwnersDevice, 1)).Should().Equal(new PushWakeUp(PushKinds.RunFailed, threadId));
    }

    [Fact]
    public async Task A_run_waiting_for_approval_wakes_the_persons_device_once()
    {
        var tools = new TestTools().Add("test.write", ToolSensitivity.Write);
        var push = new FakePushChannel();
        await using var world = await World(push, new ScriptedModel([new ToolCallOutput("c1", "test_write")], [new TextOutput("Written.")]), tools);
        await world.Grant("test.write", GrantEffect.Ask);
        await TurnOn(world, world.UserId, OwnersDevice);
        var threadId = await world.NewThread();

        await world.WaitForStatus(await world.Post(threadId, "Write it"), RunStatus.Waiting);

        (await PushesTo(push, OwnersDevice, 1)).Should().Equal(new PushWakeUp(PushKinds.ApprovalWaiting, threadId));
    }

    [Fact]
    public async Task A_person_with_the_space_open_is_not_woken()
    {
        var push = new FakePushChannel();
        await using var world = await World(push, new ScriptedModel([new TextOutput("Seen live.")], [new TextOutput("Seen later.")]));
        await TurnOn(world, world.UserId, OwnersDevice);
        var watchedThread = await world.NewThread();
        var laterThread = await world.NewThread();

        var running = world.Hosts[^1];

        using (running.Services.GetRequiredService<UserSignalHub>().Subscribe(world.SpaceId, world.UserId))
        {
            await world.WaitForStatus(await world.Post(watchedThread, "Hello"), RunStatus.Completed);
        }

        await world.WaitForStatus(await world.Post(laterThread, "Hello again"), RunStatus.Completed);

        (await PushesTo(push, OwnersDevice, 1)).Should().Equal(
            [new PushWakeUp(PushKinds.RunFinished, laterThread)],
            "the first run ended while the person had the space open, and the second after they closed it");
    }

    [Fact]
    public void A_cancelled_run_wakes_nobody()
    {
        PushNotifier.KindFor(RunStatus.Cancelled).Should().BeNull();
        PushNotifier.KindFor(RunStatus.Running).Should().BeNull();
    }

    #endregion

    #region Choices

    [Fact]
    public async Task A_bot_the_person_silenced_never_wakes_them_while_another_member_still_hears_it()
    {
        var push = new FakePushChannel();
        await using var world = await World(push, new ScriptedModel([new TextOutput("For the owner.")], [new TextOutput("For the member.")]));
        var member = await world.AddMember(MembershipRole.Member);
        await TurnOn(world, world.UserId, OwnersDevice);
        await TurnOn(world, member, MembersDevice);
        (await world.WithService<PushService, bool>(service => service.SetNotifies(world.SpaceId, world.BotId, world.UserId, false, RunWorld.Token))).Should().BeTrue();
        var threadId = await world.NewSharedThread();

        await world.WaitForStatus(await world.Post(threadId, "Hello"), RunStatus.Completed);
        await world.WaitForStatus((await world.PostRaw(threadId, "Hello too", asUser: member)).Run!.Id, RunStatus.Completed);

        (await PushesTo(push, MembersDevice, 1)).Should().ContainSingle();
        push.SentTo(OwnersDevice).Should().BeEmpty("the owner silenced the bot, and their own run ended before the member's");
        (await world.WithService<PushService, bool?>(service => service.Notifies(world.SpaceId, world.BotId, world.UserId, RunWorld.Token))).Should().BeFalse();
        (await world.WithServiceAs<PushService, bool?>(member, service => service.Notifies(world.SpaceId, world.BotId, member, RunWorld.Token))).Should().BeTrue();
    }

    [Fact]
    public async Task One_persons_device_never_hears_another_persons_run()
    {
        var push = new FakePushChannel();
        await using var world = await World(push, new ScriptedModel([new TextOutput("For the member.")], [new TextOutput("For the owner.")]));
        var member = await world.AddMember(MembershipRole.Member);
        await TurnOn(world, world.UserId, OwnersDevice);
        await TurnOn(world, member, MembersDevice);
        var threadId = await world.NewSharedThread();

        await world.WaitForStatus((await world.PostRaw(threadId, "Mine", asUser: member)).Run!.Id, RunStatus.Completed);
        await world.WaitForStatus(await world.Post(threadId, "Mine too"), RunStatus.Completed);

        (await PushesTo(push, MembersDevice, 1)).Should().ContainSingle();
        (await PushesTo(push, OwnersDevice, 1)).Should().ContainSingle();
        push.Sent.Should().HaveCount(2, "each run woke only the device of the person it acted for");
    }

    #endregion

    #region Push Service Answers

    [Fact]
    public async Task A_device_the_push_service_reports_gone_is_forgotten()
    {
        var push = new FakePushChannel { Answer = _ => PushOutcome.SubscriptionGone };
        await using var world = await World(push, new ScriptedModel([new TextOutput("Done.")]), start: false);
        await TurnOn(world, world.UserId, OwnersDevice);
        var notifier = await world.WithService<PushNotifier, PushNotifier>(Task.FromResult);

        var next = await notifier.Deliver(Job(world, Guid.CreateVersion7()), RunWorld.Token);

        next.Should().BeNull();
        (await IsOn(world, OwnersDevice)).Should().BeFalse();
    }

    [Fact]
    public async Task A_push_the_service_could_not_take_is_tried_again_for_that_device_only_and_then_dropped()
    {
        var push = new FakePushChannel { Answer = count => count == 0 ? PushOutcome.RetryLater : PushOutcome.Delivered };
        await using var world = await World(push, new ScriptedModel([new TextOutput("Done.")]), start: false);
        await TurnOn(world, world.UserId, OwnersDevice);
        await TurnOn(world, world.UserId, MembersDevice);
        var notifier = await world.WithService<PushNotifier, PushNotifier>(Task.FromResult);
        var retries = new RunOptions().PushRetryDelays.Count;

        var next = await notifier.Deliver(Job(world, Guid.CreateVersion7()), RunWorld.Token);

        next.Should().NotBeNull();
        next!.Attempt.Should().Be(1);
        next.DeviceIds.Should().ContainSingle().Which.Should().Be(push.Sent[0].Device.Id);
        (await notifier.Deliver(next, RunWorld.Token)).Should().BeNull("the second try was delivered");
        push.Sent.Should().HaveCount(3);
        (await IsOn(world, push.Sent[0].Device.Endpoint)).Should().BeTrue("a device that may come back is kept");

        push.Answer = _ => PushOutcome.RetryLater;
        (await notifier.Deliver(next with { Attempt = retries }, RunWorld.Token)).Should().BeNull("after the last delay the push is dropped");
    }

    [Fact]
    public async Task A_retry_is_sent_after_its_delay_without_a_new_run()
    {
        var push = new FakePushChannel { Answer = count => count == 0 ? PushOutcome.RetryLater : PushOutcome.Delivered };
        await using var world = await World(push, new ScriptedModel([new TextOutput("Done.")]));
        await TurnOn(world, world.UserId, OwnersDevice);
        var threadId = await world.NewThread();

        await world.WaitForStatus(await world.Post(threadId, "Hello"), RunStatus.Completed);

        (await PushesTo(push, OwnersDevice, 2)).Should().AllBeEquivalentTo(new PushWakeUp(PushKinds.RunFinished, threadId));
    }

    [Fact]
    public void Each_retry_waits_longer_than_the_one_before()
    {
        var delays = new RunOptions().PushRetryDelays;

        delays.Should().NotBeEmpty();
        delays.Should().BeInAscendingOrder();
        delays[0].Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_device_that_fails_does_not_keep_the_persons_other_devices_from_being_woken()
    {
        var push = new FakePushChannel { Answer = count => count == 0 ? throw new InvalidOperationException("Sample: a channel failure.") : PushOutcome.Delivered };
        await using var world = await World(push, new ScriptedModel([new TextOutput("Done.")]), start: false);
        await TurnOn(world, world.UserId, OwnersDevice);
        await TurnOn(world, world.UserId, MembersDevice);
        var notifier = await world.WithService<PushNotifier, PushNotifier>(Task.FromResult);

        var next = await notifier.Deliver(Job(world, Guid.CreateVersion7()), RunWorld.Token);

        push.Sent.Should().HaveCount(2, "the second device is still asked after the first one failed");
        next!.DeviceIds.Should().ContainSingle().Which.Should().Be(push.Sent[0].Device.Id);
    }

    #endregion

    #region What A Device Shows

    [Fact]
    public async Task A_woken_device_is_told_the_bot_and_what_happened_and_only_for_a_thread_its_person_can_see()
    {
        var push = new FakePushChannel();
        await using var world = await World(push, new ScriptedModel([new TextOutput("Done.")]), start: false);
        var member = await world.AddMember(MembershipRole.Member);
        var threadId = await world.NewThread();
        var botName = await world.Read(db => db.Bots.Where(bot => bot.Id == world.BotId).Select(bot => bot.Name).SingleAsync(RunWorld.Token));

        var owners = await world.WithService<PushService, PushWakeText?>(service => service.Describe(world.UserId, PushKinds.ApprovalWaiting, threadId, RunWorld.Token));
        var members = await world.WithServiceAs<PushService, PushWakeText?>(member, service => service.Describe(member, PushKinds.ApprovalWaiting, threadId, RunWorld.Token));
        var unknown = await world.WithService<PushService, PushWakeText?>(service => service.Describe(world.UserId, "something.else", threadId, RunWorld.Token));

        owners.Should().Be(new PushWakeText($"{botName} is waiting for you to approve a step.", world.BotId, threadId, false));
        members.Should().BeNull("the thread is the owner's private conversation");
        unknown.Should().BeNull();
    }

    [Theory]
    [InlineData("https://10.0.0.5/push")]
    [InlineData("https://127.0.0.1/push")]
    [InlineData("https://[::1]/push")]
    [InlineData("https://localhost/push")]
    public async Task A_push_address_that_is_plainly_private_is_refused(string endpoint)
    {
        await using var world = await World(new FakePushChannel(), new ScriptedModel([new TextOutput("Done.")]), start: false);

        var accepted = await world.WithService<PushService, bool>(service => service.TurnOn(world.UserId, Device(endpoint), RunWorld.Token));

        accepted.Should().BeFalse();
    }

    [Fact]
    public async Task A_key_that_is_not_a_point_on_the_curve_is_refused()
    {
        await using var world = await World(new FakePushChannel(), new ScriptedModel([new TextOutput("Done.")]), start: false);
        byte[] offTheCurve = [0x04, .. new byte[64]];
        var device = Device("https://push.example/device") with { PublicKey = Base64Url.EncodeToString(offTheCurve) };

        var accepted = await world.WithService<PushService, bool>(service => service.TurnOn(world.UserId, device, RunWorld.Token));

        accepted.Should().BeFalse();
        (await world.WithService<PushService, bool>(service => service.TurnOn(world.UserId, Device("https://push.example/device"), RunWorld.Token)))
            .Should().BeTrue("a real key at the same address is taken");
    }

    [Fact]
    public async Task Devices_turned_on_at_once_still_keep_to_the_cap_and_the_same_device_twice_is_one_row()
    {
        await using var world = await World(new FakePushChannel(), new ScriptedModel([new TextOutput("Done.")]), start: false);

        var many = await Task.WhenAll(Enumerable.Range(0, PushService.MaxDevicesPerPerson * 2)
            .Select(number => world.WithService<PushService, bool>(service => service.TurnOn(world.UserId, Device($"https://push.example/many-{number}"), RunWorld.Token))));
        var countAfterMany = await world.Read(db => db.PushSubscriptions.CountAsync(RunWorld.Token));
        var same = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => world.WithService<PushService, bool>(service => service.TurnOn(world.UserId, Device("https://push.example/same"), RunWorld.Token))));

        many.Should().OnlyContain(accepted => accepted);
        countAfterMany.Should().Be(PushService.MaxDevicesPerPerson);
        same.Should().OnlyContain(accepted => accepted, "the same device registered at once is renewed, never an error");
        (await world.Read(db => db.PushSubscriptions.CountAsync(row => row.Endpoint == "https://push.example/same", RunWorld.Token))).Should().Be(1);
    }

    [Theory]
    [InlineData("http://push.example/device", 65, 16)]
    [InlineData("https://push.example/device", 64, 16)]
    [InlineData("https://push.example/device", 65, 8)]
    [InlineData("not an address", 65, 16)]
    public async Task Something_that_is_not_a_push_subscription_is_refused(string endpoint, int keyLength, int secretLength)
    {
        await using var world = await World(new FakePushChannel(), new ScriptedModel([new TextOutput("Done.")]), start: false);

        var accepted = await world.WithService<PushService, bool>(service => service.TurnOn(
            world.UserId,
            new PushDeviceRequest(endpoint, Base64Url.EncodeToString(new byte[keyLength]), Base64Url.EncodeToString(new byte[secretLength])),
            RunWorld.Token));

        accepted.Should().BeFalse();
    }

    [Fact]
    public async Task A_person_keeps_a_bounded_number_of_devices_and_a_new_one_replaces_the_oldest()
    {
        await using var world = await World(new FakePushChannel(), new ScriptedModel([new TextOutput("Done.")]), start: false);

        foreach (var number in Enumerable.Range(0, PushService.MaxDevicesPerPerson + 1))
        {
            await TurnOn(world, world.UserId, $"https://push.example/device-{number}");
        }

        (await IsOn(world, "https://push.example/device-0")).Should().BeFalse("the oldest device made room");
        (await IsOn(world, "https://push.example/device-1")).Should().BeTrue();
        (await IsOn(world, $"https://push.example/device-{PushService.MaxDevicesPerPerson}")).Should().BeTrue();
        (await world.Read(db => db.PushSubscriptions.CountAsync(RunWorld.Token))).Should().Be(PushService.MaxDevicesPerPerson);
    }

    #endregion

    #region Private Methods

    /// <summary>A world whose hosts push through <paramref name="push"/>. A started host retries a push at once.</summary>
    private async Task<RunWorld> World(FakePushChannel push, ScriptedModel model, TestTools? tools = null, bool start = true)
    {
        var world = await RunWorld.Create(postgres, model, tools, everyHost: services => services.AddSingleton<IPushChannel>(push));

        if (start)
        {
            world.StartHost(options => options.PushRetryDelays = [TimeSpan.FromMilliseconds(20)]);
        }

        return world;
    }

    private static PushJob Job(RunWorld world, Guid threadId) =>
        new(world.SpaceId, world.UserId, world.BotId, new PushWakeUp(PushKinds.RunFinished, threadId), null, 0);

    private static async Task TurnOn(RunWorld world, Guid userId, string endpoint)
    {
        var on = await world.WithServiceAs<PushService, bool>(userId, service => service.TurnOn(userId, Device(endpoint), RunWorld.Token));
        on.Should().BeTrue();
    }

    /// <summary>A device as a browser registers it: a real P-256 public key, uncompressed, and a 16-byte secret.</summary>
    private static PushDeviceRequest Device(string endpoint)
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var point = key.ExportParameters(false).Q;
        byte[] publicKey = [0x04, .. point.X!, .. point.Y!];
        return new PushDeviceRequest(endpoint, Base64Url.EncodeToString(publicKey), Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)));
    }

    private static Task<bool> IsOn(RunWorld world, string endpoint) =>
        world.WithService<PushService, bool>(service => service.IsOn(world.UserId, endpoint, RunWorld.Token));

    /// <summary>Waits until the device has had <paramref name="count"/> wake-ups, then returns them.</summary>
    private static async Task<IReadOnlyList<PushWakeUp>> PushesTo(FakePushChannel push, string endpoint, int count)
    {
        var watch = Stopwatch.StartNew();

        while (push.SentTo(endpoint).Count < count && watch.Elapsed < patience)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), RunWorld.Token);
        }

        return push.SentTo(endpoint);
    }

    #endregion
}
