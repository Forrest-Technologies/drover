namespace Drover.Presentation.ViewModels;

/// <summary>
/// Notifications on this device: what the device allows and what the server says, in one state, and the changes a
/// person makes to it. The platform's side is <see cref="IPushRegistration"/>; where a platform has none, the
/// control says so in a sentence and offers nothing.
/// </summary>
public sealed partial class DevicePushViewModel(
    DroverApi api, SessionState session, ILogger<DevicePushViewModel> logger, IPushRegistration? registration = null) : ObservableObject
{
    #region Private Fields

    private string? publicKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Checking), nameof(ShowsStatusLink), nameof(ShowsDeviceBlocked), nameof(ShowsDistributor), nameof(ShowsTest))]
    private PushState? state;

    [ObservableProperty]
    private bool working;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Checking))]
    private string? problem;

    [ObservableProperty]
    private string? testNotice;

    #endregion

    #region Properties

    public static string UnavailableSentence => "This app cannot turn notifications on yet. Turn them on in the web app, in this same menu.";

    public static string CheckingNote => "Checking this device.";

    /// <summary>False where the platform has nothing to register with.</summary>
    public bool Available => registration is not null;

    /// <summary>The platform can open its own notification and battery settings.</summary>
    public IDeviceNotificationSettings? Settings => registration as IDeviceNotificationSettings;

    /// <summary>Neither a state nor a problem is known yet: the device is being asked.</summary>
    public bool Checking => State is null && Problem is null;

    public bool ShowsStatusLink => State is PushState.ServerNotReady && session.Space?.Role is "owner" or "admin";

    public bool ShowsDeviceBlocked => Settings is not null && State is PushState.DeviceBlocked;

    public bool ShowsDistributor => Settings is not null && State is PushState.NeedsDistributor;

    public bool ShowsTest => Settings is not null && State is PushState.On;

    #endregion

    #region Public Methods

    [RelayCommand]
    public async Task Check()
    {
        Working = true;
        Problem = null;

        try
        {
            State = await ReadState();
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(DevicePushViewModel));
            Problem = $"Could not check notifications on this device. {Failures.Sentence(exception)}";
        }
        catch (DeviceCallFailedException exception)
        {
            logger.LogWarning(exception, "The device could not say whether notifications are allowed");
            Problem = "The browser did not say whether notifications are allowed here. Check again in a moment.";
        }
        finally
        {
            Working = false;
        }
    }

    [RelayCommand]
    public async Task SendTest()
    {
        Working = true;
        Problem = null;
        TestNotice = null;

        try
        {
            if ((await registration!.Check()).Endpoint is { } endpoint)
            {
                await api.TestPush(endpoint);
                TestNotice = "The distributor accepted the test. Look for a Drover notification on this device.";
            }
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(DevicePushViewModel));
            Problem = $"Could not send the test. {Failures.Sentence(exception)}";
        }
        finally
        {
            Working = false;
        }
    }

    [RelayCommand]
    public async Task Toggle()
    {
        Working = true;
        Problem = null;

        try
        {
            TestNotice = null;
            State = State is PushState.On ? await TurnOff() : await TurnOn();
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(DevicePushViewModel));
            Problem = $"Notifications did not change. {Failures.Sentence(exception)}";
        }
        catch (DeviceCallFailedException exception)
        {
            logger.LogWarning(exception, "The device refused a notification change");
            Problem = "The browser did not finish the change. Try again, or check notifications in the browser's settings for this site.";
        }
        finally
        {
            Working = false;
        }
    }

    #endregion

    #region Private Methods

    private async Task<PushState> ReadState()
    {
        var device = await registration!.Check();

        if (device.Support is not DevicePushSupport.Available)
        {
            return device.Support switch
            {
                DevicePushSupport.Blocked when registration is IDeviceNotificationSettings => PushState.DeviceBlocked,
                DevicePushSupport.Blocked => PushState.Blocked,
                DevicePushSupport.NeedsDistributor => PushState.NeedsDistributor,
                DevicePushSupport.NeedsHomeScreen => PushState.NeedsHomeScreen,
                _ => PushState.NotSupported,
            };
        }

        publicKey = (await api.GetPushKey()).PublicKey;

        if (publicKey is null)
        {
            return PushState.ServerNotReady;
        }

        return device.Endpoint is { } endpoint && (await api.GetPushDeviceState(endpoint)).On ? PushState.On : PushState.Off;
    }

    /// <summary>
    /// A subscription the device already holds is not this person's (they would be On), so it may still wake someone
    /// who used this device before. It is ended first, and a new one is made: the old address stops working, and its
    /// push service reports it gone, which removes the other person's row.
    /// </summary>
    private async Task<PushState> TurnOn()
    {
        if ((await registration!.Check()).Endpoint is not null)
        {
            await registration.Unsubscribe();
        }

        if (await registration.Subscribe(publicKey!) is { } device)
        {
            await api.TurnOnPush(device);
            return PushState.On;
        }

        if ((await registration.Check()).Support is DevicePushSupport.Blocked)
        {
            return registration is IDeviceNotificationSettings ? PushState.DeviceBlocked : PushState.Blocked;
        }

        Problem = registration is IDeviceNotificationSettings
            ? "The notification distributor did not finish setup. Check that it is running, then try again."
            : "The browser did not give permission. Choose the button again and allow notifications when the browser asks.";
        return PushState.Off;
    }

    private async Task<PushState> TurnOff()
    {
        if (await registration!.Unsubscribe() is { } ended)
        {
            await api.TurnOffPush(ended);
        }

        return PushState.Off;
    }

    #endregion
}
