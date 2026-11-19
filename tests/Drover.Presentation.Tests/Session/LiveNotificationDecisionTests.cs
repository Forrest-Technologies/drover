namespace Drover.Presentation.Tests.Session;

public sealed class LiveNotificationDecisionTests
{
    #region Kind

    [Theory]
    [InlineData(UserSignalKinds.RunFinished)]
    [InlineData(UserSignalKinds.ApprovalWaiting)]
    public void FinishedAndWaitingRunsNotify(string kind)
    {
        var signal = new UserSignal(kind, Guid.NewGuid(), Guid.NewGuid());

        LiveNotificationDecision.Kind(signal).Should().Be(kind);
    }

    [Fact]
    public void ThreadChangesDoNotNotify()
    {
        var signal = new UserSignal(UserSignalKinds.ThreadChanged, Guid.NewGuid(), Guid.NewGuid());

        LiveNotificationDecision.Kind(signal).Should().BeNull();
    }

    [Fact]
    public void SignalWithoutThreadDoesNotNotify()
    {
        var signal = new UserSignal(UserSignalKinds.RunFinished, null, Guid.NewGuid());

        LiveNotificationDecision.Kind(signal).Should().BeNull();
    }

    #endregion

    #region Visibility And Mute

    [Fact]
    public void MutedBotDoesNotNotify()
    {
        var wake = Wake(main: true);

        LiveNotificationDecision.ShouldShow(wake, false, Addresses.Home, appActive: true).Should().BeFalse();
    }

    [Fact]
    public void MainConversationOnScreenDoesNotNotify()
    {
        var wake = Wake(main: true);

        LiveNotificationDecision.ShouldShow(
            wake,
            true,
            Addresses.Activity(wake.BotId),
            appActive: true).Should().BeFalse();
    }

    [Fact]
    public void SideConversationOnScreenDoesNotNotify()
    {
        var wake = Wake(main: false);

        LiveNotificationDecision.ShouldShow(
            wake,
            true,
            Addresses.SideChat(wake.BotId, wake.ThreadId),
            appActive: true).Should().BeFalse();
    }

    [Fact]
    public void BackgroundConversationNotifies()
    {
        var wake = Wake(main: false);

        LiveNotificationDecision.ShouldShow(wake, true, Addresses.Talk(wake.BotId), appActive: true).Should().BeTrue();
    }

    [Fact]
    public void InactiveWindowNotifiesForItsSelectedConversation()
    {
        var wake = Wake(main: true);

        LiveNotificationDecision.ShouldShow(
            wake,
            true,
            Addresses.Talk(wake.BotId),
            appActive: false).Should().BeTrue();
    }

    #endregion

    #region Address

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SelectedNotificationOpensItsConversation(bool main)
    {
        var wake = Wake(main);
        var expected = main ? Addresses.Talk(wake.BotId) : Addresses.SideChat(wake.BotId, wake.ThreadId);

        LiveNotificationDecision.Address(wake).Should().Be(expected);
    }

    #endregion

    #region Test Data

    private static PushWakeText Wake(bool main) =>
        new("Bot finished.", Guid.NewGuid(), Guid.NewGuid(), main, Guid.NewGuid());

    #endregion
}
