namespace Drover.Client.Tests;

public sealed class AndroidNotificationContentTests
{
    #region Public Methods

    [Fact]
    public void LockScreenPreviewOff_PublicVersionHasNoMessageText()
    {
        var content = new AndroidNotificationContent("Clara", "Private conversation",
            [new("The private answer is here.", 10, FromOwner: false)], Draft: "Keep this draft private.");

        var redacted = content.PublicVersion(showOnLockScreen: false);

        redacted.Should().Be(new AndroidNoticePreview("Clara", "New reply"));
        content.PublicVersion(showOnLockScreen: true).Should().BeNull();
    }

    [Fact]
    public void SameConversation_UpdatesOneNotification_NewestLast()
    {
        var target = new ConversationTarget(
            Guid.NewGuid(), Guid.NewGuid(), true, Guid.NewGuid(), "device", Guid.NewGuid());
        var newer = new AndroidNoticeMessage("Second answer", 30, FromOwner: false, Guid.NewGuid());
        var initial = new AndroidNotificationContent("Clara", "Planning", []).Add(newer);
        var updated = initial.Add(new("First answer", 10, FromOwner: false, Guid.NewGuid()))
            .Add(new("Follow-up question", 20, FromOwner: true))
            .Add(newer);

        var sameConversation = target with { Main = false };
        var otherConversation = target with { Thread = Guid.NewGuid() };
        sameConversation.Key.Should().Be(target.Key);
        otherConversation.Key.Should().NotBe(target.Key);
        otherConversation.Group.Should().Be(target.Group);
        updated.Messages.Select(message => message.Text).Should()
            .Equal("First answer", "Follow-up question", "Second answer");
        updated.Messages[1].FromOwner.Should().BeTrue();
    }

    #endregion
}
