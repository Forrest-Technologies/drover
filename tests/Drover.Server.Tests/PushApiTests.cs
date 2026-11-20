namespace Drover.Server.Tests;

/// <summary>
/// Turning notifications on for a device, the switch per bot, and what a woken device is told, through the API on a
/// server with a public https address. A device and a choice are one person's alone, and a device is only ever told
/// about a thread its person can see.
/// </summary>
public sealed class PushApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private readonly TestPushChannel channel = new();

    #endregion

    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        [PublicAddress.Setting] = "https://drover.example.test",
    };

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IModelClientProvider>(_ => new ScriptedModelProvider(new ScriptedModel()));
        services.RemoveAll<IEnumerable<IPushChannel>>();
        services.AddSingleton<IEnumerable<IPushChannel>>([channel]);
    }

    #endregion

    #region Key

    [Fact]
    public async Task The_server_makes_its_key_pair_once_keeps_it_encrypted_beside_the_key_ring_and_serves_only_the_public_half()
    {
        var alice = await SignIn(await SeedPerson("alice"));

        var key = await GetFor<PushKeyResponse>(alice, "/api/push/key");
        var file = Path.Combine(KeyRingPath, PushKeyStore.FileName);
        var stored = await File.ReadAllTextAsync(file, Token);
        await RestartServer();
        var again = await GetFor<PushKeyResponse>(await SignIn(await SeedPerson("bob")), "/api/push/key");

        key.Problem.Should().BeNull();
        Base64Url.DecodeFromChars(key.PublicKey).Should().HaveCount(65).And.StartWith([0x04]);
        stored.Should().NotContain(key.PublicKey!, "the pair is encrypted at rest");
        again.PublicKey.Should().Be(key.PublicKey, "a restart keeps every device's subscription working");

        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(file).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public async Task Someone_not_signed_in_is_told_nothing()
    {
        var stranger = NewClient();

        (await stranger.Get("/api/push/key")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await stranger.Get($"/api/push/wake?kind=run.finished&thread={Guid.CreateVersion7()}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Devices

    [Fact]
    public async Task A_device_turned_on_is_on_for_its_person_only_until_they_turn_it_off()
    {
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var asAlice = await SignIn(alice);
        var asMember = await SignIn(member.Email, member.Password);
        var device = Device("https://push.example/shared-browser");

        (await asAlice.Put("/api/push/device", device)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var aliceOn = await IsOn(asAlice, device.Endpoint);
        var memberOn = await IsOn(asMember, device.Endpoint);
        (await asMember.Post("/api/push/device/off", new PushDeviceQuery(device.Endpoint))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var aliceStillOn = await IsOn(asAlice, device.Endpoint);
        (await asAlice.Post("/api/push/device/off", new PushDeviceQuery(device.Endpoint))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        aliceOn.Should().BeTrue();
        memberOn.Should().BeFalse("the same browser is on for alice, not for the member");
        aliceStillOn.Should().BeTrue("the member cannot turn off alice's device");
        (await IsOn(asAlice, device.Endpoint)).Should().BeFalse();
    }

    [Fact]
    public async Task A_test_wake_goes_only_to_the_signed_in_persons_device()
    {
        var alice = await SignIn(await SeedPerson("alice"));
        var bob = await SignIn(await SeedPerson("bob"));
        var device = Device("https://push.example/sample-phone");
        (await alice.Put("/api/push/device", device)).EnsureSuccessStatusCode();

        (await bob.Post("/api/push/device/test", new PushDeviceQuery(device.Endpoint))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        channel.Wakes.Should().BeEmpty();
        (await alice.Post("/api/push/device/test", new PushDeviceQuery(device.Endpoint))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        channel.Wakes.Should().ContainSingle().Which.Should().Be(new PushWakeUp(PushKinds.DeviceTest, Guid.Empty));
    }

    [Fact]
    public async Task Something_that_is_not_a_push_subscription_is_refused_with_a_sentence()
    {
        var alice = await SignIn(await SeedPerson("alice"));

        var response = await alice.Put("/api/push/device", new PushDeviceRequest("http://10.0.0.5/push", "short", "short"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Token)).Should().Contain("not a push subscription");
    }

    #endregion

    #region Wake Text

    [Fact]
    public async Task A_woken_device_is_told_the_bot_and_what_happened_but_never_about_another_persons_thread()
    {
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var asAlice = await SignIn(alice);
        var asMember = await SignIn(member.Email, member.Password);
        var bot = await CreateBot(asAlice, alice.SpaceId, "Atlas");
        var thread = await OpenMain(asAlice, alice.SpaceId, bot.Id);

        var text = await GetFor<PushWakeText>(asAlice, $"/api/push/wake?kind=run.failed&thread={thread.Id}");
        var members = await asMember.Get($"/api/push/wake?kind=run.failed&thread={thread.Id}");
        var unknownKind = await asAlice.Get($"/api/push/wake?kind=anything&thread={thread.Id}");

        text.Should().Be(new PushWakeText("Atlas could not finish. Open the conversation to see why.", bot.Id, thread.Id, true, alice.SpaceId));
        members.StatusCode.Should().Be(HttpStatusCode.NotFound, "alice's main thread is hers alone");
        unknownKind.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_browser_left_subscribed_by_one_person_is_told_nothing_to_show_the_next_person()
    {
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var asAlice = await SignIn(alice);
        var asMember = await SignIn(member.Email, member.Password);
        var bot = await CreateBot(asAlice, alice.SpaceId, "Atlas");
        var thread = await OpenMain(asAlice, alice.SpaceId, bot.Id);
        var device = Device("https://push.example/left-subscribed");
        (await asAlice.Put("/api/push/device", device)).EnsureSuccessStatusCode();
        var wake = $"/api/push/wake?kind=run.finished&thread={thread.Id}&user={alice.UserId}";

        var asSubscriber = await asAlice.Get(wake, PushEndpoints.PushEndpointHeader, device.Endpoint);
        var asAnotherPerson = await asMember.Get(wake, PushEndpoints.PushEndpointHeader, device.Endpoint);
        var body = await asAnotherPerson.Content.ReadAsStringAsync(Token);

        asSubscriber.StatusCode.Should().Be(HttpStatusCode.OK);
        (await asSubscriber.Content.ReadFromJsonAsync<PushWakeText>(Token))!.Body.Should().Contain("Atlas");
        asAnotherPerson.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the signed-in person holds no subscription for that address");
        body.Should().BeEmpty("nothing of alice's bot is said to the next person");
    }

    [Fact]
    public async Task A_wake_sent_for_one_person_is_never_described_to_another_even_on_the_others_own_subscription()
    {
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var asAlice = await SignIn(alice);
        var asMember = await SignIn(member.Email, member.Password);
        var bot = await CreateBot(asAlice, alice.SpaceId, "Atlas");
        var thread = await OpenMain(asAlice, alice.SpaceId, bot.Id);
        await ShareWithTheSpace(alice.SpaceId, alice.UserId, thread.Id);
        var aliceDevice = Device("https://push.example/alices-browser");
        var memberDevice = Device("https://push.example/members-browser-after-the-switch");
        (await asAlice.Put("/api/push/device", aliceDevice)).EnsureSuccessStatusCode();
        (await asMember.Put("/api/push/device", memberDevice)).EnsureSuccessStatusCode();
        var forAlice = $"/api/push/wake?kind=run.finished&thread={thread.Id}&user={alice.UserId}";

        // The member's browser holds the member's own subscription, and the thread is shared with them, but the wake
        // was sent for alice: it was queued before the browser changed hands.
        var queued = await asMember.Get(forAlice, PushEndpoints.PushEndpointHeader, memberDevice.Endpoint);
        var forTheSubscriber = await asAlice.Get(forAlice, PushEndpoints.PushEndpointHeader, aliceDevice.Endpoint);
        var forTheMember = await asMember.Get($"/api/push/wake?kind=run.finished&thread={thread.Id}&user={member.UserId}", PushEndpoints.PushEndpointHeader, memberDevice.Endpoint);

        queued.StatusCode.Should().Be(HttpStatusCode.Conflict, "the wake was sent for someone else");
        (await queued.Content.ReadAsStringAsync(Token)).Should().BeEmpty();
        forTheSubscriber.StatusCode.Should().Be(HttpStatusCode.OK);
        forTheMember.StatusCode.Should().Be(HttpStatusCode.OK, "a wake sent for the member is described to the member");
    }

    [Fact]
    public async Task When_the_last_persons_subscription_is_still_installed_the_next_person_gets_a_403_before_any_recipient_check()
    {
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var asAlice = await SignIn(alice);
        var asMember = await SignIn(member.Email, member.Password);
        var bot = await CreateBot(asAlice, alice.SpaceId, "Atlas");
        var thread = await OpenMain(asAlice, alice.SpaceId, bot.Id);
        var aliceDevice = Device("https://push.example/alices-still-installed");
        (await asAlice.Put("/api/push/device", aliceDevice)).EnsureSuccessStatusCode();

        // The member signed in on alice's browser and never turned notifications on, so holds no subscription for its
        // address. A new wake for alice names alice, which is not the member: the missing subscription is what is
        // reported, so the worker ends alice's subscription, not a recipient mismatch that would keep it.
        var forAlice = await asMember.Get($"/api/push/wake?kind=run.finished&thread={thread.Id}&user={alice.UserId}", PushEndpoints.PushEndpointHeader, aliceDevice.Endpoint);

        forAlice.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await forAlice.Content.ReadAsStringAsync(Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_browsers_wake_that_names_nobody_is_not_described_even_to_the_person_who_holds_the_subscription()
    {
        var alice = await SeedPerson("alice");
        var asAlice = await SignIn(alice);
        var bot = await CreateBot(asAlice, alice.SpaceId, "Atlas");
        var thread = await OpenMain(asAlice, alice.SpaceId, bot.Id);
        var device = Device("https://push.example/queued-before-the-upgrade");
        (await asAlice.Put("/api/push/device", device)).EnsureSuccessStatusCode();

        var legacy = await asAlice.Get($"/api/push/wake?kind=run.finished&thread={thread.Id}", PushEndpoints.PushEndpointHeader, device.Endpoint);

        legacy.StatusCode.Should().Be(HttpStatusCode.Conflict, "nothing says a wake that names nobody was for the person signed in");
        (await legacy.Content.ReadAsStringAsync(Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_person_who_turned_the_same_browser_on_is_answered_and_the_first_person_still_is()
    {
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var asAlice = await SignIn(alice);
        var asMember = await SignIn(member.Email, member.Password);
        var bot = await CreateBot(asAlice, alice.SpaceId, "Atlas");
        var thread = await OpenMain(asAlice, alice.SpaceId, bot.Id);
        var device = Device("https://push.example/shared-by-two");
        (await asAlice.Put("/api/push/device", device)).EnsureSuccessStatusCode();
        (await asMember.Put("/api/push/device", device)).EnsureSuccessStatusCode();
        var wake = $"/api/push/wake?kind=run.finished&thread={thread.Id}";

        var forAlice = await asAlice.Get($"{wake}&user={alice.UserId}", PushEndpoints.PushEndpointHeader, device.Endpoint);
        var forMember = await asMember.Get($"{wake}&user={member.UserId}", PushEndpoints.PushEndpointHeader, device.Endpoint);

        forAlice.StatusCode.Should().Be(HttpStatusCode.OK);
        forMember.StatusCode.Should().Be(HttpStatusCode.NotFound, "the member holds the subscription, but alice's main thread is hers alone");
    }

    [Fact]
    public async Task A_subscription_that_was_turned_off_or_is_not_the_persons_gets_no_wake_text()
    {
        var alice = await SeedPerson("alice");
        var asAlice = await SignIn(alice);
        var bot = await CreateBot(asAlice, alice.SpaceId, "Atlas");
        var thread = await OpenMain(asAlice, alice.SpaceId, bot.Id);
        var device = Device("https://push.example/turned-off");
        var wake = $"/api/push/wake?kind=run.finished&thread={thread.Id}&user={alice.UserId}";
        (await asAlice.Put("/api/push/device", device)).EnsureSuccessStatusCode();
        (await asAlice.Post("/api/push/device/off", new PushDeviceQuery(device.Endpoint))).EnsureSuccessStatusCode();

        var afterOff = await asAlice.Get(wake, PushEndpoints.PushEndpointHeader, device.Endpoint);
        var neverSubscribed = await asAlice.Get(wake, PushEndpoints.PushEndpointHeader, "https://push.example/never-subscribed");
        var tooLong = await asAlice.Get(wake, PushEndpoints.PushEndpointHeader, $"https://push.example/{new string('a', Drover.Engine.Push.PushService.MaxEndpointLength)}");
        var noAddress = await asAlice.Get(wake, PushEndpoints.PushEndpointHeader, " ");

        afterOff.StatusCode.Should().Be(HttpStatusCode.Forbidden, "turning a device off ends its wake text");
        neverSubscribed.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        tooLong.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        noAddress.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_wake_without_a_subscription_address_is_answered_as_before_for_the_app_and_the_desktop()
    {
        var alice = await SeedPerson("alice");
        var asAlice = await SignIn(alice);
        var bot = await CreateBot(asAlice, alice.SpaceId, "Atlas");
        var thread = await OpenMain(asAlice, alice.SpaceId, bot.Id);

        var text = await GetFor<PushWakeText>(asAlice, $"/api/push/wake?kind=run.finished&thread={thread.Id}");

        text.BotId.Should().Be(bot.Id);
    }

    #endregion

    #region Bots

    [Fact]
    public async Task A_bot_notifies_by_default_and_one_persons_switch_is_theirs_alone()
    {
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var outsider = await SeedPerson("outsider");
        var asAlice = await SignIn(alice);
        var asMember = await SignIn(member.Email, member.Password);
        var asOutsider = await SignIn(outsider);
        var bot = await CreateBot(asAlice, alice.SpaceId, "Atlas");
        var path = $"/api/spaces/{alice.SpaceId}/bots/{bot.Id}/notify";

        var before = await GetFor<BotNotifySetting>(asAlice, path);
        (await asAlice.Put(path, new BotNotifySetting(false))).StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await GetFor<BotNotifySetting>(asAlice, path);
        var members = await GetFor<BotNotifySetting>(asMember, path);
        var outsiders = await asOutsider.Put($"/api/spaces/{outsider.SpaceId}/bots/{bot.Id}/notify", new BotNotifySetting(false));

        before.Notifies.Should().BeTrue();
        after.Notifies.Should().BeFalse();
        members.Notifies.Should().BeTrue("alice's choice does not silence the bot for the member");
        outsiders.StatusCode.Should().Be(HttpStatusCode.NotFound, "the bot is not in the outsider's space");
    }

    #endregion

    #region Private Methods

    private sealed class TestPushChannel : IPushChannel
    {
        #region Properties

        public string Kind => "test";

        public List<PushWakeUp> Wakes { get; } = [];

        #endregion

        #region Interface Implementations

        public Task<PushOutcome> Send(PushSubscription subscription, PushWakeUp wakeUp, CancellationToken cancellationToken)
        {
            Wakes.Add(wakeUp);
            return Task.FromResult(PushOutcome.Delivered);
        }

        #endregion
    }

    /// <summary>Shares the thread with every member of the space, as its owner would.</summary>
    private async Task ShareWithTheSpace(Guid spaceId, Guid ownerId, Guid threadId)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, ownerId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var db = scope.ServiceProvider.GetRequiredService<DroverDbContext>();
        await db.Threads.Where(found => found.Id == threadId)
            .ExecuteUpdateAsync(set => set.SetProperty(found => found.AudienceUserId, Audience.Everyone), Token);
        await work.Commit(Token);
    }

    private static PushDeviceRequest Device(string endpoint)
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var point = key.ExportParameters(false).Q;
        byte[] publicKey = [0x04, .. point.X!, .. point.Y!];
        return new PushDeviceRequest(endpoint, Base64Url.EncodeToString(publicKey), Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)));
    }

    private static async Task<bool> IsOn(ApiClient client, string endpoint)
    {
        var response = await client.Post("/api/push/device/state", new PushDeviceQuery(endpoint));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<PushDeviceState>(Token))!.On;
    }

    private Task<ApiClient> SignIn((string Email, string Password, Guid UserId, Guid SpaceId) person) => SignIn(person.Email, person.Password);

    private async Task<ApiClient> SignIn(string email, string password)
    {
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<BotSummary> CreateBot(ApiClient client, Guid spaceId, string name)
    {
        var connection = await PostFor<ModelConnectionSummary>(
            client,
            $"/api/spaces/{spaceId}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, $"For {name}", null, null, "scripted"),
            HttpStatusCode.Created);
        return await PostFor<BotSummary>(client, $"/api/spaces/{spaceId}/bots", new CreateBotRequest(name, "Answer briefly.", connection.Id, null), HttpStatusCode.Created);
    }

    private static async Task<ThreadSummary> OpenMain(ApiClient client, Guid spaceId, Guid botId)
    {
        var response = await client.Put($"/api/spaces/{spaceId}/bots/{botId}/main-thread");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<ThreadSummary>(Token))!;
    }

    private static async Task<T> PostFor<T>(ApiClient client, string path, object body, HttpStatusCode expected)
    {
        var response = await client.Post(path, body);
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private static async Task<T> GetFor<T>(ApiClient client, string path)
    {
        var response = await client.Get(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    #endregion
}
