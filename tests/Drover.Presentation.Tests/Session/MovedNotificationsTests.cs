namespace Drover.Presentation.Tests.Session;

/// <summary>
/// Notifications a browser had on at the client's former address, /native/, are taken up again at the root the first
/// time a person is signed in: only when the server says the ended subscription was that person's, and without asking
/// when the browser still allows them. Every other outcome says where to turn notifications on; a server or browser
/// that fails keeps the note for the next start; and it never runs twice. The device and the server are stand-ins;
/// every key and address is a sample.
/// </summary>
public sealed class MovedNotificationsTests : IDisposable
{
    #region Private Fields

    private const string SampleKey = "BSampleServerKeyNotARealOne";

    private readonly FakeServer server = new();

    private readonly FakePushRegistration device = new();

    private readonly ToastService toasts = new(TimeProvider.System);

    private static readonly PushDeviceState on = new(true);

    private static readonly PushDeviceState off = new(false);

    #endregion

    #region Public Methods

    public void Dispose() => server.Dispose();

    [Fact]
    public async Task Nothing_moved_asks_the_server_nothing_and_says_nothing()
    {
        await Make().Resume(Xunit.TestContext.Current.CancellationToken);

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
        server.Answer("PUT", "/api/push/device", HttpStatusCode.NoContent);

        await Make().Resume(Xunit.TestContext.Current.CancellationToken);

        server.Sent("POST", "/api/push/device/state").First().Should().Contain("sample-moved",
            "the server is asked whether the subscription the move ended was this person's");
        device.AskedWith.Should().Be(SampleKey);
        server.Requests.Should().Contain(request => request.Method == "PUT" && request.Path == "/api/push/device"
            && request.Body.Contains("sample-root"));
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

        await Make().Resume(Xunit.TestContext.Current.CancellationToken);

        device.AskedWith.Should().BeNull();
        device.Unsubscribed.Should().BeEmpty();
        server.Requests.Should().NotContain(request => request.Method == "PUT");
        toasts.Toasts.Should().BeEmpty();
        device.Moved.Should().Be(MovedSubscription.None);
    }

    [Fact]
    public async Task Someone_elses_subscription_on_the_device_is_ended_first_as_turning_notifications_on_does()
    {
        device.Moved = MovedSubscription.PermissionKept;
        device.Endpoint = "https://push.example.test/sample-other";
        device.Subscription = new("https://push.example.test/sample-root", "sample-key", "sample-auth");
        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        server.AnswerInTurn("POST", "/api/push/device/state", on, off);
        server.Answer("PUT", "/api/push/device", HttpStatusCode.NoContent);

        await Make().Resume(Xunit.TestContext.Current.CancellationToken);

        device.Unsubscribed.Should().Equal("https://push.example.test/sample-other");
        device.Endpoint.Should().Be("https://push.example.test/sample-root");
        toasts.Toasts.Should().ContainSingle().Which.Message.Should().Be(MovedNotifications.Resumed);
    }

    [Fact]
    public async Task A_browser_that_no_longer_allows_notifications_is_never_asked_and_the_person_is_told_where_to_turn_them_on()
    {
        device.Moved = MovedSubscription.PermissionLost;

        await Make().Resume(Xunit.TestContext.Current.CancellationToken);

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
        var moved = Make();

        await moved.Resume(Xunit.TestContext.Current.CancellationToken);

        device.Moved.Should().Be(MovedSubscription.PermissionKept);
        toasts.Toasts.Should().BeEmpty();

        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        server.Answer("PUT", "/api/push/device", HttpStatusCode.NoContent);
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

        await Make().Resume(Xunit.TestContext.Current.CancellationToken);

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

        await Make().Resume(Xunit.TestContext.Current.CancellationToken);

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

        await Make().Resume(Xunit.TestContext.Current.CancellationToken);

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

        await Make().Resume(Xunit.TestContext.Current.CancellationToken);

        device.Moved.Should().Be(MovedSubscription.PermissionKept);
        toasts.Toasts.Should().BeEmpty();
    }

    [Fact]
    public async Task The_device_is_asked_once_per_start()
    {
        var moved = Make();

        await moved.Resume(Xunit.TestContext.Current.CancellationToken);
        await moved.Resume(Xunit.TestContext.Current.CancellationToken);

        device.MovedAsked.Should().Be(1);
    }

    [Fact]
    public async Task The_gate_takes_up_moved_notifications_for_a_signed_in_person_only()
    {
        var moved = Make();
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

    private MovedNotifications Make() =>
        new(server.CreateApi(), toasts, NullLogger<MovedNotifications>.Instance, device);

    private SessionGateViewModel Gate(MovedNotifications moved) =>
        new(new SessionState(server.CreateApi()), new Navigator(), NullLogger<SessionGateViewModel>.Instance, moved);

    #endregion
}
