namespace Drover.App.Platforms.Android;

/// <summary>The device's own notifications, for <see cref="PushWakeFlow"/>.</summary>
internal sealed class AndroidPushNotices : IPushNotices
{
    #region Interface Implementations

    public void Show(PushWakeText wake, AndroidPushSubscription saved, bool canReply) =>
        AndroidNotifications.Show(wake, saved, canReply);

    public void ShowChange() => AndroidNotifications.ShowChange();

    public void ShowTest() => AndroidNotifications.ShowTest();

    #endregion
}
