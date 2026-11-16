namespace Drover.Ui.Tests.Session;

/// <summary>
/// Notifications on this device: every state is said in a sentence, the one action is a real button only when
/// Drover can do something, and the device's choice reaches the server. The browser is a stand-in; every key and
/// address here is a sample.
/// </summary>
public sealed class PushControlTests : ScreenTest
{
    #region Private Fields

    private const string SampleKey = "BSampleServerKeyNotARealOne";

    private readonly FakePushRegistration device = new();

    #endregion

    #region Constructors

    public PushControlTests()
    {
        Services.AddSingleton<IPushRegistration>(device);
    }

    #endregion

    #region The Control

    [Theory]
    [InlineData(PushState.Off, "Turn on notifications on this device")]
    [InlineData(PushState.On, "Turn off notifications on this device")]
    [InlineData(PushState.Blocked, null)]
    [InlineData(PushState.NotSupported, null)]
    [InlineData(PushState.NeedsHomeScreen, null)]
    [InlineData(PushState.ServerNotReady, null)]
    public void Notifications_offer_a_button_only_when_drover_can_do_something(PushState state, string? action)
    {
        var control = Render<PushControl>(set => set.Add(view => view.State, state));

        control.Find(".dv-push-state").TextContent.Should().Be(PushWords.Sentence(state));
        control.FindAll("button").Select(button => button.TextContent.Trim()).Should().Equal(action is null ? [] : [action]);
        control.FindAll(".dv-push-contents").Select(line => line.TextContent).Should().Equal(
            action is null ? [] : [PushWords.Contents],
            "whoever can turn notifications on or off is told what one will show");
    }

    [Fact]
    public void Every_state_has_its_own_sentence_and_the_section_is_named_by_its_heading()
    {
        Enum.GetValues<PushState>().Select(PushWords.Sentence).Should().OnlyHaveUniqueItems();
        PushWords.Sentence(PushState.NeedsHomeScreen).Should().Contain("Home Screen");

        var control = Render<PushControl>(set => set.Add(view => view.State, PushState.Off));

        var section = control.Find("section");
        control.Find($"#{section.GetAttribute("aria-labelledby")}").TextContent.Should().Be("Notifications on this device");
    }

    #endregion

    #region This Device

    [Theory]
    [InlineData(DevicePushSupport.Blocked, PushState.Blocked)]
    [InlineData(DevicePushSupport.NotSupported, PushState.NotSupported)]
    [InlineData(DevicePushSupport.NeedsHomeScreen, PushState.NeedsHomeScreen)]
    public void A_device_that_cannot_be_woken_says_why_without_asking_the_server(DevicePushSupport support, PushState state)
    {
        device.Support = support;

        var control = Render<DevicePush>();

        control.WaitForAssertion(() => control.Find(".dv-push-state").TextContent.Should().Be(PushWords.Sentence(state)));
        control.FindAll("button").Should().BeEmpty();
        Server.Requests.Should().BeEmpty();
    }

    [Fact]
    public void A_server_that_cannot_send_says_so()
    {
        Server.Answer("GET", "/api/push/key", new PushKeyResponse(null, "Sample: no address."));

        var control = Render<DevicePush>();

        control.WaitForAssertion(() => control.Find(".dv-push-state").TextContent.Should().Be(PushWords.Sentence(PushState.ServerNotReady)));
        control.FindAll("button").Should().BeEmpty();
    }

    [Fact]
    public void Turning_on_asks_the_browser_with_the_servers_key_and_tells_the_server_the_device()
    {
        Server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        Server.Answer("PUT", "/api/push/device", HttpStatusCode.NoContent);
        device.Subscription = new PushDeviceRequest("https://push.example/sample-device", "sample-key", "sample-secret");
        var control = Render<DevicePush>();
        control.WaitForAssertion(() => control.Find("button").TextContent.Trim().Should().Be("Turn on notifications on this device"));

        control.Find("button").Click();

        control.WaitForAssertion(() => control.Find(".dv-push-state").TextContent.Should().Be(PushWords.Sentence(PushState.On)));
        device.AskedWith.Should().Be(SampleKey);
        Server.Requests.Should().Contain(request => request.Method == "PUT" && request.Path == "/api/push/device" && request.Body.Contains("sample-device"));
        control.Find("button").TextContent.Trim().Should().Be("Turn off notifications on this device");
    }

    [Fact]
    public void A_device_already_on_turns_off_in_the_browser_and_on_the_server()
    {
        device.Endpoint = "https://push.example/sample-device";
        Server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        Server.Answer("POST", "/api/push/device/state", new PushDeviceState(true));
        Server.Answer("POST", "/api/push/device/off", HttpStatusCode.NoContent);
        var control = Render<DevicePush>();
        control.WaitForAssertion(() => control.Find(".dv-push-state").TextContent.Should().Be(PushWords.Sentence(PushState.On)));

        control.Find("button").Click();

        control.WaitForAssertion(() => control.Find(".dv-push-state").TextContent.Should().Be(PushWords.Sentence(PushState.Off)));
        device.Endpoint.Should().BeNull();
        Server.Requests.Should().Contain(request => request.Method == "POST" && request.Path == "/api/push/device/off" && request.Body.Contains("sample-device"));
    }

    [Theory]
    [InlineData(DevicePushSupport.Available, PushState.Off)]
    [InlineData(DevicePushSupport.Blocked, PushState.Blocked)]
    public void A_permission_not_given_leaves_notifications_off_or_says_they_are_blocked(DevicePushSupport afterAsking, PushState state)
    {
        Server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        var control = Render<DevicePush>();
        control.WaitForAssertion(() => control.Find("button").Should().NotBeNull());
        device.SupportAfterAsking = afterAsking;

        control.Find("button").Click();

        control.WaitForAssertion(() => control.Find(".dv-push-state").TextContent.Should().Be(PushWords.Sentence(state)));
        Server.Requests.Should().NotContain(request => request.Path == "/api/push/device");
        control.FindAll(".dv-note").Select(note => note.TextContent.Trim()).Should().Equal(
            state is PushState.Off ? ["The browser did not give permission. Choose the button again and allow notifications when the browser asks."] : [],
            "a dismissed prompt is said, so the button does not seem to do nothing");
    }

    [Fact]
    public void A_server_that_cannot_be_reached_is_said_in_a_sentence()
    {
        Server.Unreachable = true;

        var control = Render<DevicePush>();

        control.WaitForAssertion(() => control.Find(".dv-note").TextContent.Trim().Should().StartWith("Could not check notifications on this device."));
        control.FindAll("[role=status]").Should().NotContain(status => status.TextContent.Contains("Checking"));
        control.Find("button").TextContent.Trim().Should().Be("Check again");
    }

    [Fact]
    public void A_browser_that_fails_to_answer_is_an_error_to_check_again_not_a_browser_without_support()
    {
        Server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        device.FailNextCheck = true;
        var control = Render<DevicePush>();
        control.WaitForAssertion(() => control.Find(".dv-note").TextContent.Trim().Should().StartWith("The browser did not say"));

        control.Find("button").Click();

        control.WaitForAssertion(() => control.Find(".dv-push-state").TextContent.Should().Be(PushWords.Sentence(PushState.Off)));
    }

    [Fact]
    public void An_owner_on_a_server_that_cannot_send_is_pointed_to_the_status_page()
    {
        Services.GetRequiredService<SessionState>().Begin(Person);
        Server.Answer("GET", "/api/push/key", new PushKeyResponse(null, "Sample: no address."));

        var control = Render<DevicePush>();

        control.WaitForAssertion(() => control.Find(".dv-device-push-next a").GetAttribute("href").Should().Be(Addresses.Status));
    }

    #endregion

    #region Account Menu

    [Fact]
    public void The_account_menu_opens_notifications_for_this_device()
    {
        SignedIn();
        Server.Answer("GET", "/api/push/key", new PushKeyResponse(SampleKey));
        var menu = Render<AccountMenu>();

        menu.FindAll("[role=menuitem]").Single(item => item.TextContent.Trim() == "Notifications").Click();

        menu.WaitForAssertion(() => menu.Find(".dv-dialog-title").TextContent.Should().Be("Notifications on this device"));
        menu.WaitForAssertion(() => menu.Find(".dv-push-state").TextContent.Should().Be(PushWords.Sentence(PushState.Off)));
        menu.Find(".dv-push h3").ClassList.Should().Contain("dv-visually-hidden", "the dialog's title already says it");
        var focusCalls = FocusCalls();

        menu.Find("dialog").TriggerEvent("onclose", EventArgs.Empty);

        menu.WaitForAssertion(() => FocusCalls().Should().Be(focusCalls + 1, "focus goes back to the account menu's button"));
    }

    #endregion

    #region Private Methods

    private int FocusCalls() => JSInterop.Invocations.Count(call => call.Identifier.EndsWith("focus", StringComparison.Ordinal));

    #endregion
}


/// <summary>A browser stand-in: what it supports, the subscription it holds, and what it answers when asked.</summary>
public sealed class FakePushRegistration : IPushRegistration
{
    #region Properties

    public DevicePushSupport Support { get; set; } = DevicePushSupport.Available;

    /// <summary>What the browser reports once the person was asked and did not allow it.</summary>
    public DevicePushSupport SupportAfterAsking { get; set; } = DevicePushSupport.Available;

    public string? Endpoint { get; set; }

    /// <summary>What the browser gives when permission is granted. Null means it was not.</summary>
    public PushDeviceRequest? Subscription { get; set; }

    public string? AskedWith { get; private set; }

    /// <summary>The next check fails as a browser call does.</summary>
    public bool FailNextCheck { get; set; }

    #endregion

    #region Interface Implementations

    public Task<DevicePushCheck> Check(CancellationToken cancellationToken = default)
    {
        if (FailNextCheck)
        {
            FailNextCheck = false;
            throw new JSException("Sample: the browser call failed.");
        }

        return Task.FromResult(new DevicePushCheck(Support, Endpoint));
    }

    public Task<PushDeviceRequest?> Subscribe(string publicKey, CancellationToken cancellationToken = default)
    {
        AskedWith = publicKey;

        if (Subscription is null)
        {
            Support = SupportAfterAsking;
            return Task.FromResult<PushDeviceRequest?>(null);
        }

        Endpoint = Subscription.Endpoint;
        return Task.FromResult<PushDeviceRequest?>(Subscription);
    }

    public Task<string?> Unsubscribe(CancellationToken cancellationToken = default)
    {
        var ended = Endpoint;
        Endpoint = null;
        return Task.FromResult(ended);
    }

    #endregion
}
