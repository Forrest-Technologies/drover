namespace Drover.Ui.Session;

/// <summary>What this browser or app allows for notifications, before Drover's own server is asked.</summary>
public enum DevicePushSupport
{
    Available,
    Blocked,
    NotSupported,
    NeedsHomeScreen,
    NeedsDistributor,
}


/// <summary>What the device allows, and the push service address it is subscribed at, if it is.</summary>
public sealed record DevicePushCheck(DevicePushSupport Support, string? Endpoint);


/// <summary>
/// Whether this device had notifications on at the address the browser client was served under before it moved to
/// the root on 2026-11-20, and whether the permission it gave then still stands. Moving ended that subscription.
/// </summary>
public enum MovedSubscription
{
    None,
    PermissionKept,
    PermissionLost,
}


/// <summary>
/// What moving left on this device: the push service address of the subscription it ended, if known, and the address
/// of the subscription an earlier attempt made to take its place, if that attempt's outcome is not known yet.
/// </summary>
public sealed record MovedPush(MovedSubscription State, string? Endpoint = null, string? Candidate = null);


/// <summary>
/// The platform's side of notifications: the web implementation uses the service worker and the Push API, a native
/// shell its own. Permission is asked only in <see cref="Subscribe"/>, which runs when the person turns
/// notifications on, never at launch.
/// </summary>
public interface IPushRegistration
{
    Task<DevicePushCheck> Check(CancellationToken cancellationToken = default);

    /// <summary>Asks for permission and subscribes with the server's key. Null when permission was not given.</summary>
    Task<PushDeviceRequest?> Subscribe(string publicKey, CancellationToken cancellationToken = default);

    /// <summary>Ends the device's subscription. Returns the address it had, or null when it had none.</summary>
    Task<string?> Unsubscribe(CancellationToken cancellationToken = default);

    /// <summary>A subscription ended when the browser client moved. Only a browser ever had one.</summary>
    Task<MovedPush> FindMovedSubscription(CancellationToken cancellationToken = default) =>
        Task.FromResult(new MovedPush(MovedSubscription.None));

    /// <summary>Forgets the subscription that moving ended, once it is taken up again or cannot be.</summary>
    Task ForgetMovedSubscription(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Keeps, with the note, the address of the subscription made to take the moved one's place.</summary>
    Task RememberMoveCandidate(string endpoint, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// Ends the device's subscription only while it is the one at <paramref name="endpoint"/>. False when the device
    /// holds another, which is left alone: it may be one another account made since.
    /// </summary>
    async Task<bool> UnsubscribeOnly(string endpoint, CancellationToken cancellationToken = default) =>
        (await Check(cancellationToken)).Endpoint == endpoint && await Unsubscribe(cancellationToken) is not null;
}


/// <summary>Opens settings that a native shell can show for notification delivery.</summary>
public interface IDeviceNotificationSettings
{
    void OpenNotificationSettings();

    Task OpenBatterySettings();
}
