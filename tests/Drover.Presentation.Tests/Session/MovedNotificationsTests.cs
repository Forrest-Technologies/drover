namespace Drover.Presentation.Tests.Session;

/// <summary>
/// Notifications a browser had on at the client's former address, /native/, are taken up again at the root the first
/// time a person is signed in: only when the server, in the one request that writes it, finds the ended subscription
/// was that person's, and without asking when the browser still allows them. Another account's subscription at the
/// root is never ended. Every other outcome says where to turn notifications on; a server or browser that fails keeps
/// the note for the next start; and it never runs twice. The device and the server are stand-ins;
/// every key and address is a sample.
/// </summary>
public sealed class MovedNotificationsTests : IDisposable
{
    #region Private Fields

    private const string SampleKey = "BSampleServerKeyNotARealOne";

    private readonly FakeServer server = new();

    private readonly FakePushRegistration device = new();

    private readonly ToastService toasts = new(TimeProvider.System);

    private const string SampleRoot = "https://push.example.test/sample-root";

    private const string SampleOther = "https://push.example.test/sample-other";

    private static readonly Guid accountA = Samples.Session().UserId;

    private static readonly Guid accountB = Guid.Parse("00000000-0000-7000-8000-0000000000a2");

    // Answers for the account signed in, which is A unless a test signs in B.
    private static readonly PushDeviceState on = new(true, accountA);

    private static readonly PushDeviceState off = new(false, accountA);

    #endregion

    #region Public Methods

    public void Dispose() => server.Dispose();

    [Fact]
    public async Task Nothing_moved_asks_the_server_nothing_and_says_nothing()
    {
        await (await Make()).Resume(Xunit.TestContext.Current.CancellationToken);

        server.Requests.Should().BeEmpty();
        device.AskedWith.Should().BeNull();
        toasts.Toasts.Should().BeEmpty();
    }

    [Fact]
    public async Task A_subscription_ended_by_the_move_is_made_again_at_the_root_and_registered_for_the_person()
    {
        device.Moved = MovedSubscription.PermissionKept;
        device.Subscription = new("https://push.example.test/sample-root", "sample-key", "sample-auth");
        server.Answer("POST", "/api/push/device/state", on);
        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        server.Answer("POST", "/api/push/device/move", HttpStatusCode.NoContent);

        await (await Make()).Resume(Xunit.TestContext.Current.CancellationToken);

        device.AskedWith.Should().Be(SampleKey);
        server.Sent("POST", "/api/push/device/move").Should().ContainSingle().Which.Should()
            .Contain("sample-moved", "the server checks the old subscription in the request that writes the new one")
            .And.Contain("sample-root");
        server.Requests.Should().NotContain(request => request.Method == "PUT", "nothing is registered outside that one request");
        toasts.Toasts.Should().ContainSingle().Which.Message.Should().Be(MovedNotifications.Resumed);
        device.Moved.Should().Be(MovedSubscription.None, "the note is forgotten once taken up");
    }

    [Fact]
    public async Task A_person_already_subscribed_at_the_root_keeps_that_subscription()
    {
        device.Moved = MovedSubscription.PermissionKept;
        device.Endpoint = "https://push.example.test/sample-root";
        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        server.Answer("POST", "/api/push/device/state", new PushDeviceState(true));

        await (await Make()).Resume(Xunit.TestContext.Current.CancellationToken);

        device.AskedWith.Should().BeNull();
        device.Unsubscribed.Should().BeEmpty();
        server.Requests.Should().NotContain(request => request.Method == "PUT");
        toasts.Toasts.Should().BeEmpty();
        device.Moved.Should().Be(MovedSubscription.None);
    }

    [Fact]
    public async Task Another_accounts_active_subscription_at_the_root_is_never_ended_and_the_person_is_told_how_to_choose()
    {
        device.Moved = MovedSubscription.PermissionKept;
        device.Endpoint = "https://push.example.test/sample-other";
        device.Subscription = new("https://push.example.test/sample-root", "sample-key", "sample-auth");
        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));

        // The subscription at the root is asked about first, and it is not this person's.
        server.Answer("POST", "/api/push/device/state", off);

        await (await Make()).Resume(Xunit.TestContext.Current.CancellationToken);

        device.Unsubscribed.Should().BeEmpty("only the person can choose to replace another account's notifications");
        device.Endpoint.Should().Be("https://push.example.test/sample-other");
        device.AskedWith.Should().BeNull();
        server.Requests.Should().NotContain(request => request.Path == "/api/push/device/move" || request.Method == "PUT");
        var toast = toasts.Toasts.Should().ContainSingle().Which;
        toast.Message.Should().Be(MovedNotifications.HeldByAnother);
        toast.Kind.Should().Be(ToastKind.Warning);
        device.Moved.Should().Be(MovedSubscription.None);
    }

    [Fact]
    public async Task An_account_that_changed_before_the_restore_is_refused_by_the_server_and_nothing_is_left_subscribed()
    {
        // The lookup was answered for the person who had the old subscription; by the time of the write another
        // account is signed in, so the server, checking again under that account, finds the old one is not theirs.
        device.Moved = MovedSubscription.PermissionKept;
        device.Subscription = new("https://push.example.test/sample-root", "sample-key", "sample-auth");
        server.Answer("POST", "/api/push/device/state", on);
        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        server.Answer("POST", "/api/push/device/move", HttpStatusCode.NotFound);

        await (await Make()).Resume(Xunit.TestContext.Current.CancellationToken);

        device.Unsubscribed.Should().Equal("https://push.example.test/sample-root");
        device.Endpoint.Should().BeNull();
        server.Requests.Should().NotContain(request => request.Method == "PUT");
        ShowsTurnOnAgain();
        device.Moved.Should().Be(MovedSubscription.None);
    }

    [Fact]
    public async Task A_restore_whose_answer_was_lost_keeps_the_new_subscription_and_the_note_for_the_next_start()
    {
        device.Moved = MovedSubscription.PermissionKept;
        device.Subscription = new(SampleRoot, "sample-key", "sample-auth");
        server.Answer("POST", "/api/push/device/state", on);
        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        server.Answer("POST", "/api/push/device/move", HttpStatusCode.InternalServerError);

        await (await Make()).Resume(Xunit.TestContext.Current.CancellationToken);

        device.Unsubscribed.Should().BeEmpty("the server may have taken it, and the previous subscription may be gone");
        device.Endpoint.Should().Be(SampleRoot);
        device.MoveCandidate.Should().Be(SampleRoot, "the next start must know this subscription is this attempt's");
        device.Moved.Should().Be(MovedSubscription.PermissionKept);
        toasts.Toasts.Should().BeEmpty();
    }

    [Fact]
    public async Task A_move_the_server_took_though_its_answer_was_lost_is_kept_at_the_next_start()
    {
        device.Moved = MovedSubscription.PermissionKept;
        device.Subscription = new(SampleRoot, "sample-key", "sample-auth");
        server.Answer("POST", "/api/push/device/state", on);
        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        server.Answer("POST", "/api/push/device/move", HttpStatusCode.InternalServerError);
        var moved = await Make();
        await moved.Resume(Xunit.TestContext.Current.CancellationToken);

        // The server had written it: the previous subscription is gone, and the new one is this person's.
        server.Requests.Clear();
        server.AnswerInTurn("POST", "/api/push/device/state", on);
        await moved.Resume(Xunit.TestContext.Current.CancellationToken);

        server.Sent("POST", "/api/push/device/state").Should().ContainSingle().Which.Should().Contain("sample-root");
        server.Requests.Should().NotContain(request => request.Path == "/api/push/device/move");
        device.Unsubscribed.Should().BeEmpty();
        device.Endpoint.Should().Be(SampleRoot);
        toasts.Toasts.Should().ContainSingle().Which.Message.Should().Be(MovedNotifications.Resumed);
        device.Moved.Should().Be(MovedSubscription.None);
    }

    [Fact]
    public async Task A_move_the_server_did_not_take_is_ended_and_made_again_at_the_next_start()
    {
        device.Moved = MovedSubscription.PermissionKept;
        device.Subscription = new(SampleRoot, "sample-key", "sample-auth");
        server.Answer("POST", "/api/push/device/state", on);
        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        server.Answer("POST", "/api/push/device/move", HttpStatusCode.InternalServerError);
        var moved = await Make();
        await moved.Resume(Xunit.TestContext.Current.CancellationToken);

        // The server had not written it: the new subscription is nobody's, and the previous one is still this person's.
        server.AnswerInTurn("POST", "/api/push/device/state", off, on);
        server.Answer("POST", "/api/push/device/move", HttpStatusCode.NoContent);
        await moved.Resume(Xunit.TestContext.Current.CancellationToken);

        device.Unsubscribed.Should().Equal(SampleRoot);
        server.Sent("POST", "/api/push/device/move").Should().HaveCount(2);
        device.Endpoint.Should().Be(SampleRoot);
        toasts.Toasts.Should().ContainSingle().Which.Message.Should().Be(MovedNotifications.Resumed);
        device.Moved.Should().Be(MovedSubscription.None);
    }

    [Fact]
    public async Task A_move_whose_answer_was_lost_is_left_alone_while_another_account_is_signed_in_and_settled_by_its_own()
    {
        // A's move was taken by the server, but its answer was lost: the candidate is a live subscription for A.
        device.Moved = MovedSubscription.PermissionKept;
        device.Subscription = new(SampleRoot, "sample-key", "sample-auth");
        server.Answer("POST", "/api/push/device/state", on);
        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        server.Answer("POST", "/api/push/device/move", HttpStatusCode.InternalServerError);
        await (await Make()).Resume(Xunit.TestContext.Current.CancellationToken);
        device.MoveCandidateAccount.Should().Be(accountA);

        // B signs in on the next start. To B the server would say the candidate is off, which proves nothing about A.
        server.Answer("POST", "/api/push/device/state", new PushDeviceState(false, accountB));
        await (await Make(accountB)).Resume(Xunit.TestContext.Current.CancellationToken);

        server.Requests.Should().BeEmpty("nothing is asked or written for B about A's subscription");
        device.Unsubscribed.Should().BeEmpty();
        device.Endpoint.Should().Be(SampleRoot);
        device.Moved.Should().Be(MovedSubscription.PermissionKept, "the note is left for A");
        device.MoveCandidate.Should().Be(SampleRoot);
        device.MoveCandidateAccount.Should().Be(accountA);
        toasts.Toasts.Should().BeEmpty();

        // A signs in again: the server says the candidate is A's, so the move was taken.
        server.Answer("POST", "/api/push/device/state", on);
        await (await Make()).Resume(Xunit.TestContext.Current.CancellationToken);

        device.Unsubscribed.Should().BeEmpty();
        server.Requests.Should().NotContain(request => request.Path == "/api/push/device/move");
        toasts.Toasts.Should().ContainSingle().Which.Message.Should().Be(MovedNotifications.Resumed);
        device.Moved.Should().Be(MovedSubscription.None);
    }

    [Fact]
    public async Task An_answer_for_an_account_switched_in_another_tab_never_settles_the_candidate()
    {
        device.Moved = MovedSubscription.PermissionKept;
        device.Subscription = new(SampleRoot, "sample-key", "sample-auth");
        server.Answer("POST", "/api/push/device/state", on);
        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        server.Answer("POST", "/api/push/device/move", HttpStatusCode.InternalServerError);
        await (await Make()).Resume(Xunit.TestContext.Current.CancellationToken);

        // This start loaded A, but by the time it asks, the browser's sign-in is B's.
        server.Answer("POST", "/api/push/device/state", new PushDeviceState(false, accountB));
        await (await Make()).Resume(Xunit.TestContext.Current.CancellationToken);

        device.Unsubscribed.Should().BeEmpty("an answer for B does not show A's move was never taken");
        device.Endpoint.Should().Be(SampleRoot);
        device.Moved.Should().Be(MovedSubscription.PermissionKept);
        device.MoveCandidate.Should().Be(SampleRoot);
        toasts.Toasts.Should().BeEmpty();
    }

    [Fact]
    public async Task A_candidate_with_no_account_is_never_settled()
    {
        device.Moved = MovedSubscription.PermissionKept;
        device.Endpoint = SampleRoot;
        device.MoveCandidate = SampleRoot;

        await (await Make()).Resume(Xunit.TestContext.Current.CancellationToken);

        server.Requests.Should().BeEmpty();
        device.Unsubscribed.Should().BeEmpty();
        device.Moved.Should().Be(MovedSubscription.PermissionKept);
        toasts.Toasts.Should().BeEmpty();
    }

    [Fact]
    public async Task A_refused_move_never_ends_a_subscription_another_account_made_meanwhile()
    {
        // Another tab, signed in as another account, turned notifications on while this attempt waited for the server.
        device.Moved = MovedSubscription.PermissionKept;
        device.Subscription = new(SampleRoot, "sample-key", "sample-auth");
        device.AfterSubscribe = () => device.Endpoint = SampleOther;
        server.Answer("POST", "/api/push/device/state", on);
        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        server.Answer("POST", "/api/push/device/move", HttpStatusCode.NotFound);

        await (await Make()).Resume(Xunit.TestContext.Current.CancellationToken);

        device.Unsubscribed.Should().BeEmpty("only the subscription this attempt made may be ended");
        device.Endpoint.Should().Be(SampleOther);
        ShowsTurnOnAgain();
    }

    [Fact]
    public async Task A_subscription_another_account_made_after_a_lost_answer_is_never_ended_at_the_next_start()
    {
        device.Moved = MovedSubscription.PermissionKept;
        device.Subscription = new(SampleRoot, "sample-key", "sample-auth");
        server.Answer("POST", "/api/push/device/state", on);
        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        server.Answer("POST", "/api/push/device/move", HttpStatusCode.InternalServerError);
        var moved = await Make();
        await moved.Resume(Xunit.TestContext.Current.CancellationToken);

        device.Endpoint = SampleOther;
        server.Answer("POST", "/api/push/device/state", off);
        await moved.Resume(Xunit.TestContext.Current.CancellationToken);

        device.Unsubscribed.Should().BeEmpty();
        device.Endpoint.Should().Be(SampleOther);
        toasts.Toasts.Should().ContainSingle().Which.Message.Should().Be(MovedNotifications.HeldByAnother);
    }

    [Fact]
    public async Task A_browser_that_no_longer_allows_notifications_is_never_asked_and_the_person_is_told_where_to_turn_them_on()
    {
        device.Moved = MovedSubscription.PermissionLost;

        await (await Make()).Resume(Xunit.TestContext.Current.CancellationToken);

        device.AskedWith.Should().BeNull("permission is asked only when the person turns notifications on");
        server.Requests.Should().BeEmpty();
        var toast = toasts.Toasts.Should().ContainSingle().Which;
        toast.Message.Should().Be(MovedNotifications.TurnOnAgain);
        toast.Kind.Should().Be(ToastKind.Warning);
        device.Moved.Should().Be(MovedSubscription.None);
    }

    [Fact]
    public async Task A_server_that_cannot_be_reached_keeps_the_note_for_the_next_start()
    {
        device.Moved = MovedSubscription.PermissionKept;
        server.Answer("POST", "/api/push/device/state", on);
        server.Answer("GET", "/api/push/key", HttpStatusCode.InternalServerError);
        var moved = await Make();

        await moved.Resume(Xunit.TestContext.Current.CancellationToken);

        device.Moved.Should().Be(MovedSubscription.PermissionKept);
        toasts.Toasts.Should().BeEmpty();

        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        server.Answer("POST", "/api/push/device/move", HttpStatusCode.NoContent);
        device.Subscription = new("https://push.example.test/sample-root", "sample-key", "sample-auth");
        await moved.Resume(Xunit.TestContext.Current.CancellationToken);

        device.Moved.Should().Be(MovedSubscription.None);
        toasts.Toasts.Should().ContainSingle().Which.Message.Should().Be(MovedNotifications.Resumed);
    }

    [Fact]
    public async Task A_subscription_another_account_held_is_never_taken_up_for_the_person_signed_in_now()
    {
        device.Moved = MovedSubscription.PermissionKept;
        device.Subscription = new("https://push.example.test/sample-root", "sample-key", "sample-auth");
        server.Answer("POST", "/api/push/device/state", off);
        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));

        await (await Make()).Resume(Xunit.TestContext.Current.CancellationToken);

        device.AskedWith.Should().BeNull("only the person who had notifications on gets them back without choosing to");
        server.Requests.Should().NotContain(request => request.Method == "PUT");
        ShowsTurnOnAgain();
        device.Moved.Should().Be(MovedSubscription.None);
    }

    [Fact]
    public async Task A_note_without_an_address_is_never_taken_up()
    {
        device.Moved = MovedSubscription.PermissionKept;
        device.MovedEndpoint = null;

        await (await Make()).Resume(Xunit.TestContext.Current.CancellationToken);

        server.Requests.Should().BeEmpty();
        device.AskedWith.Should().BeNull();
        ShowsTurnOnAgain();
    }

    [Theory]
    [InlineData("no key")]
    [InlineData("device unavailable")]
    [InlineData("no subscription")]
    public async Task A_subscription_that_could_not_be_made_again_says_where_to_turn_notifications_on(string cause)
    {
        device.Moved = MovedSubscription.PermissionKept;
        device.Support = cause is "device unavailable" ? DevicePushSupport.NotSupported : DevicePushSupport.Available;
        device.Subscription = null;
        server.Answer("POST", "/api/push/device/state", on);
        server.Answer("GET", "/api/push/key", new PushKeyResponse(cause is "no key" ? null : SampleKey));

        await (await Make()).Resume(Xunit.TestContext.Current.CancellationToken);

        server.Requests.Should().NotContain(request => request.Method == "PUT");
        ShowsTurnOnAgain();
        device.Moved.Should().Be(MovedSubscription.None, "the person was told, so the note is not needed again");
    }

    [Fact]
    public async Task A_browser_call_that_fails_keeps_the_note_and_says_nothing_yet()
    {
        device.Moved = MovedSubscription.PermissionKept;
        device.FailNextCheck = true;
        server.Answer("POST", "/api/push/device/state", on);

        await (await Make()).Resume(Xunit.TestContext.Current.CancellationToken);

        device.Moved.Should().Be(MovedSubscription.PermissionKept);
        toasts.Toasts.Should().BeEmpty();
    }

    [Fact]
    public async Task The_device_is_asked_once_per_start()
    {
        var moved = await Make();

        await moved.Resume(Xunit.TestContext.Current.CancellationToken);
        await moved.Resume(Xunit.TestContext.Current.CancellationToken);

        device.MovedAsked.Should().Be(1);
    }

    [Fact]
    public async Task The_gate_takes_up_moved_notifications_for_a_signed_in_person_only()
    {
        var moved = await Make();
        server.Answer("GET", "/api/session", HttpStatusCode.Unauthorized);
        server.Answer("GET", "/api/setup", HttpStatusCode.NotFound);

        await Gate(moved).Check();

        device.MovedAsked.Should().Be(0);

        server.Answer("GET", "/api/session", Samples.Session());
        var gate = Gate(moved);
        await gate.Check();

        gate.State.Should().Be(GateState.SignedIn);
        device.MovedAsked.Should().Be(1);
    }

    #endregion

    #region Private Methods

    private void ShowsTurnOnAgain()
    {
        var toast = toasts.Toasts.Should().ContainSingle().Which;
        toast.Message.Should().Be(MovedNotifications.TurnOnAgain);
        toast.Kind.Should().Be(ToastKind.Warning);
    }

    /// <summary>A start of the app, signed in as account A or as <paramref name="account"/>.</summary>
    private async Task<MovedNotifications> Make(Guid? account = null)
    {
        var person = Samples.Session() with { UserId = account ?? accountA };
        var session = new SessionState(server.CreateApi(), new ShareInbox());
        server.Answer("GET", "/api/session", person);
        await session.Load(Xunit.TestContext.Current.CancellationToken);
        server.Requests.Clear();
        return new(server.CreateApi(), session, toasts, NullLogger<MovedNotifications>.Instance, device);
    }

    private SessionGateViewModel Gate(MovedNotifications moved) =>
        new(
            new SessionState(server.CreateApi(), new ShareInbox()),
            new Navigator(),
            NullLogger<SessionGateViewModel>.Instance,
            moved);

    #endregion
}
