namespace Drover.App.Platforms.Android;

/// <summary>Posts only while the receiving registration still owns the device.</summary>
internal sealed class AndroidPushNotices(AndroidPushRegistration registration, AndroidPushSubscription saved)
    : IPushNotices
{
    #region Interface Implementations

    public Task Show(PushWakeText wake, bool canReply) =>
        registration.WhileRegistered(saved.UserId, saved.Token, () => AndroidNotifications.Show(wake, saved, canReply));

    public Task ShowChange() =>
        registration.WhileRegistered(saved.UserId, saved.Token, AndroidNotifications.ShowChange);

    public Task ShowTest() =>
        registration.WhileRegistered(saved.UserId, saved.Token, AndroidNotifications.ShowTest);

    #endregion
}
