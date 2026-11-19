namespace Drover.Ui.Session;

/// <summary>Starts notifications that are delivered while this app is running.</summary>
public interface ILiveNotifications
{
    void Start();
}


/// <summary>What the notifications dialog truthfully says about delivery that works only while the app runs.</summary>
public interface ILiveNotificationStatus
{
    string StatusSentence { get; }
}


/// <summary>A target whose own push path handles notifications has no live notification service to start.</summary>
public sealed class NoLiveNotifications : ILiveNotifications
{
    public void Start()
    {
    }
}
