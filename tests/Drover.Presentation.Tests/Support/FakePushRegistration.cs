namespace Drover.Presentation.Tests.Support;

/// <summary>A device stand-in: what it supports, the subscription it holds, and what it answers when asked.</summary>
public class FakePushRegistration : IPushRegistration
{
    #region Properties

    public DevicePushSupport Support { get; set; } = DevicePushSupport.Available;

    /// <summary>What the device reports once the person was asked and did not allow it.</summary>
    public DevicePushSupport SupportAfterAsking { get; set; } = DevicePushSupport.Available;

    public string? Endpoint { get; set; }

    /// <summary>What the device gives when permission is granted. Null means it was not.</summary>
    public PushDeviceRequest? Subscription { get; set; }

    public string? AskedWith { get; private set; }

    /// <summary>The subscriptions ended, in order.</summary>
    public List<string> Unsubscribed { get; } = [];

    /// <summary>The next check fails as a platform call does.</summary>
    public bool FailNextCheck { get; set; }

    /// <summary>What moving the browser client to the root left on this device.</summary>
    public MovedSubscription Moved { get; set; }

    /// <summary>The push service address of the subscription the move ended.</summary>
    public string? MovedEndpoint { get; set; } = "https://push.example.test/sample-moved";

    /// <summary>How many times the device was asked whether moving left anything.</summary>
    public int MovedAsked { get; private set; }

    /// <summary>The subscription an attempt to take up the moved one made, kept with the note.</summary>
    public string? MoveCandidate { get; set; }

    /// <summary>What happens on the device right after it subscribes, such as another tab replacing the subscription.</summary>
    public Action? AfterSubscribe { get; set; }

    #endregion

    #region Interface Implementations

    public Task<DevicePushCheck> Check(CancellationToken cancellationToken = default)
    {
        if (FailNextCheck)
        {
            FailNextCheck = false;
            throw new DeviceCallFailedException("Sample: the device call failed.");
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
        AfterSubscribe?.Invoke();
        return Task.FromResult<PushDeviceRequest?>(Subscription);
    }

    public Task<string?> Unsubscribe(CancellationToken cancellationToken = default)
    {
        var ended = Endpoint;

        if (ended is not null)
        {
            Unsubscribed.Add(ended);
        }

        Endpoint = null;
        return Task.FromResult(ended);
    }

    public Task<MovedPush> FindMovedSubscription(CancellationToken cancellationToken = default)
    {
        MovedAsked++;
        return Task.FromResult(Moved is MovedSubscription.None
            ? new MovedPush(Moved)
            : new MovedPush(Moved, MovedEndpoint, MoveCandidate));
    }

    public Task ForgetMovedSubscription(CancellationToken cancellationToken = default)
    {
        Moved = MovedSubscription.None;
        MoveCandidate = null;
        return Task.CompletedTask;
    }

    public Task RememberMoveCandidate(string endpoint, CancellationToken cancellationToken = default)
    {
        MoveCandidate = endpoint;
        return Task.CompletedTask;
    }

    public async Task<bool> UnsubscribeOnly(string endpoint, CancellationToken cancellationToken = default) =>
        Endpoint == endpoint && await Unsubscribe(cancellationToken) is not null;

    #endregion
}

/// <summary>A device stand-in that also opens the system's notification settings, as a phone does.</summary>
public sealed class FakeSettingsPushRegistration : FakePushRegistration, IDeviceNotificationSettings
{
    #region Properties

    public int NotificationSettingsOpened { get; private set; }

    public int BatterySettingsOpened { get; private set; }

    #endregion

    #region Interface Implementations

    public void OpenNotificationSettings() => NotificationSettingsOpened++;

    public Task OpenBatterySettings()
    {
        BatterySettingsOpened++;
        return Task.CompletedTask;
    }

    #endregion
}
