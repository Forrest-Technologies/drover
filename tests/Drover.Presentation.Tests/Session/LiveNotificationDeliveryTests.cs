namespace Drover.Presentation.Tests.Session;

/// <summary>
/// A finished run in a conversation that is not on screen asks the desktop sink to show a notification.
/// The sink here is a list, so the test does not need a window or a notification server.
/// </summary>
public sealed class LiveNotificationDeliveryTests
{
    #region Public Methods

    [Fact]
    public async Task A_finished_reply_in_a_background_conversation_is_shown()
    {
        var spaceId = Guid.NewGuid();
        var botId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var body = "Lead\r\nbot\u202E <b>finished</b>.";
        using var server = new FakeServer()
            .Answer("GET", "/api/push/wake", new PushWakeText(body, botId, threadId, true, spaceId))
            .Answer("GET", $"/api/spaces/{spaceId}/bots/{botId}/notify", new BotNotifySetting(true));
        var shown = new List<(string Body, string Address, long Version)>();
        var navigator = new Navigator();

        await LiveNotificationDelivery.Deliver(
            server.CreateApi(),
            navigator,
            (text, address, version) => shown.Add((text, address, version)),
            NullLogger.Instance,
            spaceId,
            version: 4,
            new UserSignal(UserSignalKinds.RunFinished, threadId, Guid.NewGuid()),
            () => true,
            () => true,
            CancellationToken.None);

        shown.Should().Equal([(NotificationText.Sanitize(body), Addresses.Talk(botId), 4L)]);
        navigator.Current.Should().Be(Addresses.Home, "the conversation that finished was not the one on screen");
    }

    [Fact]
    public void The_desktop_app_shows_that_notification_through_the_same_call()
    {
        var notifications = Repository.NativeFiles(".cs")
            .Single(file => file.Path == "Platforms/Desktop/DesktopNotifications.cs").Text;
        var sink = Repository.NativeFiles(".cs")
            .Single(file => file.Path == "Platforms/Desktop/DesktopNotificationSink.cs").Text;

        notifications.Should().Contain("LiveNotificationDelivery.Deliver");
        sink.Should().Contain("NotificationText.EscapeIfMarkup");
        sink.Should().Contain("NotificationText.Cut");
        sink.Should().Contain("GetCapabilities");
    }

    #endregion
}
