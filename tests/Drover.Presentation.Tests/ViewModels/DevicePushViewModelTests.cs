namespace Drover.Presentation.Tests.ViewModels;

/// <summary>
/// Notifications on this device: every state is said in a sentence, the one action is a real button only when
/// Drover can do something, and the device's choice reaches the server. The device is a stand-in; every key and
/// address here is a sample.
/// </summary>
public sealed class DevicePushViewModelTests : IDisposable
{
    #region Private Fields

    private const string SampleKey = "BSampleServerKeyNotARealOne";

    private readonly FakeServer server = new();

    private readonly FakePushRegistration device = new();

    private readonly SessionState session;

    #endregion

    #region Constructors

    public DevicePushViewModelTests()
    {
        session = new(server.CreateApi());
    }

    #endregion

    #region Public Methods

    public void Dispose() => server.Dispose();

    #endregion

    #region The Control

    [Theory]
    [InlineData(PushState.Off, "Turn on notifications on this device")]
    [InlineData(PushState.On, "Turn off notifications on this device")]
    [InlineData(PushState.Blocked, null)]
    [InlineData(PushState.NotSupported, null)]
    [InlineData(PushState.NeedsHomeScreen, null)]
    [InlineData(PushState.ServerNotReady, null)]
    public void Notifications_offer_an_action_only_when_drover_can_do_something(PushState state, string? action)
    {
        PushWords.Action(state).Should().Be(action);
        PushWords.Sentence(state).Should().NotBeNullOrEmpty();
        PushWords.Contents.Should().Be("A notification names the bot and what happened. It never shows what was said.");
    }

    [Fact]
    public void Every_state_has_its_own_sentence()
    {
        Enum.GetValues<PushState>().Select(PushWords.Sentence).Should().OnlyHaveUniqueItems();
        PushWords.Sentence(PushState.NeedsHomeScreen).Should().Contain("Home Screen");
    }

    [Fact]
    public void A_platform_with_nothing_to_register_with_says_so_and_offers_nothing()
    {
        var control = Make(registration: null);

        control.Available.Should().BeFalse();
        DevicePushViewModel.UnavailableSentence.Should().Be("This app cannot turn notifications on yet. Turn them on in the web app, in this same menu.");
        control.StatusSentence.Should().Be(DevicePushViewModel.UnavailableSentence);
    }

    [Fact]
    public void Live_delivery_explains_its_own_limit_without_offering_a_push_switch()
    {
        var sentence = "This app can show notifications while it is running. System settings can still silence them.";
        var control = Make(registration: null, live: new LiveStatus(sentence));

        control.Available.Should().BeFalse();
        control.StatusSentence.Should().Be(sentence);
    }

    #endregion

    #region This Device

    [Fact]
    public void Until_the_device_has_answered_the_control_says_it_is_checking()
    {
        var control = Make();

        control.Checking.Should().BeTrue();
        DevicePushViewModel.CheckingNote.Should().Be("Checking this device.");
    }

    [Theory]
    [InlineData(DevicePushSupport.Blocked, PushState.Blocked)]
    [InlineData(DevicePushSupport.NotSupported, PushState.NotSupported)]
    [InlineData(DevicePushSupport.NeedsHomeScreen, PushState.NeedsHomeScreen)]
    public async Task A_device_that_cannot_be_woken_says_why_without_asking_the_server(DevicePushSupport support, PushState state)
    {
        device.Support = support;
        var control = Make();

        await control.Check();

        control.State.Should().Be(state);
        control.Checking.Should().BeFalse();
        PushWords.Action(state).Should().BeNull();
        server.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_server_that_cannot_send_says_so()
    {
        server.Answer("GET", "/api/push/key", new PushKeyResponse(null, "Sample: no address."));
        var control = Make();

        await control.Check();

        control.State.Should().Be(PushState.ServerNotReady);
        PushWords.Action(PushState.ServerNotReady).Should().BeNull();
    }

    [Fact]
    public async Task Turning_on_asks_the_device_with_the_servers_key_and_tells_the_server_the_device()
    {
        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        server.Answer("PUT", "/api/push/device", HttpStatusCode.NoContent);
        device.Subscription = new PushDeviceRequest("https://push.example/sample-device", "sample-key", "sample-secret");
        var control = Make();
        await control.Check();
        control.State.Should().Be(PushState.Off);

        await control.ToggleCommand.ExecuteAsync(null);

        control.State.Should().Be(PushState.On);
        device.AskedWith.Should().Be(SampleKey);
        server.Requests.Should().Contain(request => request.Method == "PUT" && request.Path == "/api/push/device" && request.Body.Contains("sample-device"));
        PushWords.Action(control.State!.Value).Should().Be("Turn off notifications on this device");
        control.Working.Should().BeFalse();
    }

    [Fact]
    public async Task A_device_already_on_turns_off_on_the_device_and_on_the_server()
    {
        device.Endpoint = "https://push.example/sample-device";
        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        server.Answer("POST", "/api/push/device/state", new PushDeviceState(true));
        server.Answer("POST", "/api/push/device/off", HttpStatusCode.NoContent);
        var control = Make();
        await control.Check();
        control.State.Should().Be(PushState.On);

        await control.ToggleCommand.ExecuteAsync(null);

        control.State.Should().Be(PushState.Off);
        device.Endpoint.Should().BeNull();
        server.Requests.Should().Contain(request => request.Method == "POST" && request.Path == "/api/push/device/off" && request.Body.Contains("sample-device"));
    }

    [Fact]
    public async Task A_subscription_this_device_held_for_someone_else_is_ended_and_a_new_one_made()
    {
        device.Endpoint = "https://push.example/someone-elses";
        device.Subscription = new PushDeviceRequest("https://push.example/a-new-one", "sample-key", "sample-secret");
        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        server.Answer("POST", "/api/push/device/state", new PushDeviceState(false));
        server.Answer("PUT", "/api/push/device", HttpStatusCode.NoContent);
        var control = Make();
        await control.Check();
        control.State.Should().Be(PushState.Off);

        await control.ToggleCommand.ExecuteAsync(null);

        control.State.Should().Be(PushState.On);
        device.Unsubscribed.Should().Equal("https://push.example/someone-elses");
        server.Requests.Should().Contain(request => request.Path == "/api/push/device" && request.Body.Contains("a-new-one"))
            .And.NotContain(request => request.Path == "/api/push/device" && request.Body.Contains("someone-elses"));
    }

    [Theory]
    [InlineData(DevicePushSupport.Available, PushState.Off)]
    [InlineData(DevicePushSupport.Blocked, PushState.Blocked)]
    public async Task A_permission_not_given_leaves_notifications_off_or_says_they_are_blocked(DevicePushSupport afterAsking, PushState state)
    {
        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        var control = Make();
        await control.Check();
        device.SupportAfterAsking = afterAsking;

        await control.ToggleCommand.ExecuteAsync(null);

        control.State.Should().Be(state);
        server.Requests.Should().NotContain(request => request.Path == "/api/push/device");
        control.Problem.Should().Be(
            state is PushState.Off ? "The browser did not give permission. Choose the button again and allow notifications when the browser asks." : null,
            "a dismissed prompt is said, so the button does not seem to do nothing");
    }

    [Fact]
    public async Task A_server_that_cannot_be_reached_is_said_in_a_sentence()
    {
        server.Unreachable = true;
        var control = Make();

        await control.Check();

        control.Problem.Should().StartWith("Could not check notifications on this device.");
        control.State.Should().BeNull();
        control.Checking.Should().BeFalse("the control says what went wrong and offers to check again");
    }

    [Fact]
    public async Task A_device_that_fails_to_answer_is_an_error_to_check_again_not_a_device_without_support()
    {
        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        device.FailNextCheck = true;
        var control = Make();
        await control.Check();

        control.Problem.Should().StartWith("The browser did not say");
        control.State.Should().BeNull();

        await control.CheckCommand.ExecuteAsync(null);

        control.State.Should().Be(PushState.Off);
        control.Problem.Should().BeNull();
    }

    [Fact]
    public async Task A_device_that_fails_a_change_says_to_try_again()
    {
        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        var control = Make();
        await control.Check();
        device.FailNextCheck = true;

        await control.ToggleCommand.ExecuteAsync(null);

        control.Problem.Should().Be("The browser did not finish the change. Try again, or check notifications in the browser's settings for this site.");
        control.State.Should().Be(PushState.Off);
    }

    [Fact]
    public async Task A_server_that_refuses_a_change_says_what_it_said()
    {
        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        server.Refuse("PUT", "/api/push/device", HttpStatusCode.BadRequest, "Sample refusal.");
        device.Subscription = new PushDeviceRequest("https://push.example/sample-device", "sample-key", "sample-secret");
        var control = Make();
        await control.Check();

        await control.ToggleCommand.ExecuteAsync(null);

        control.Problem.Should().StartWith("Notifications did not change.").And.Contain("Sample refusal.");
    }

    [Fact]
    public async Task An_owner_on_a_server_that_cannot_send_is_pointed_to_the_status_page()
    {
        session.Begin(Samples.Session());
        server.Answer("GET", "/api/push/key", new PushKeyResponse(null, "Sample: no address."));
        var control = Make();

        await control.Check();

        control.ShowsStatusLink.Should().BeTrue();
        Addresses.Status.Should().Be("/status");
    }

    [Fact]
    public async Task A_member_on_a_server_that_cannot_send_is_not_pointed_to_the_status_page()
    {
        session.Begin(Samples.Session("member"));
        server.Answer("GET", "/api/push/key", new PushKeyResponse(null, "Sample: no address."));
        var control = Make();

        await control.Check();

        control.ShowsStatusLink.Should().BeFalse();
    }

    #endregion

    #region A Phone

    [Fact]
    public async Task A_phone_blocked_by_the_system_offers_its_settings_and_checking_again()
    {
        var phone = new FakeSettingsPushRegistration { Support = DevicePushSupport.Blocked };
        var control = Make(phone);

        await control.Check();

        control.State.Should().Be(PushState.DeviceBlocked);
        control.ShowsDeviceBlocked.Should().BeTrue();
        control.Settings!.OpenNotificationSettings();
        phone.NotificationSettingsOpened.Should().Be(1);
    }

    [Fact]
    public async Task A_phone_with_no_distributor_says_to_install_one()
    {
        var phone = new FakeSettingsPushRegistration { Support = DevicePushSupport.NeedsDistributor };
        var control = Make(phone);

        await control.Check();

        control.State.Should().Be(PushState.NeedsDistributor);
        control.ShowsDistributor.Should().BeTrue();
    }

    [Fact]
    public async Task A_phone_with_notifications_on_can_send_a_test_and_open_its_battery_settings()
    {
        var phone = new FakeSettingsPushRegistration { Endpoint = "https://push.example/sample-device" };
        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        server.Answer("POST", "/api/push/device/state", new PushDeviceState(true));
        server.Answer("POST", "/api/push/device/test", HttpStatusCode.NoContent);
        var control = Make(phone);
        await control.Check();

        await control.SendTestCommand.ExecuteAsync(null);

        control.ShowsTest.Should().BeTrue();
        control.Problem.Should().BeNull();
        control.TestNotice.Should().Be("The distributor accepted the test. Look for a Drover notification on this device.");
        await control.Settings!.OpenBatterySettings();
        phone.BatterySettingsOpened.Should().Be(1);
    }

    [Fact]
    public async Task A_phone_that_did_not_finish_setup_says_to_check_the_distributor()
    {
        var phone = new FakeSettingsPushRegistration();
        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        var control = Make(phone);
        await control.Check();

        await control.ToggleCommand.ExecuteAsync(null);

        control.Problem.Should().Be("The notification distributor did not finish setup. Check that it is running, then try again.");
    }

    [Fact]
    public async Task A_phone_that_is_blocked_after_asking_sends_the_person_to_system_settings()
    {
        var phone = new FakeSettingsPushRegistration { SupportAfterAsking = DevicePushSupport.Blocked };
        server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        var control = Make(phone);
        await control.Check();

        await control.ToggleCommand.ExecuteAsync(null);

        control.State.Should().Be(PushState.DeviceBlocked);
    }

    #endregion

    #region Private Methods

    private DevicePushViewModel Make() => Make(device);

    private DevicePushViewModel Make(IPushRegistration? registration, ILiveNotificationStatus? live = null) =>
        new(server.CreateApi(), session, NullLogger<DevicePushViewModel>.Instance, registration, live);

    private sealed record LiveStatus(string StatusSentence) : ILiveNotificationStatus;

    #endregion
}
