namespace Drover.Ui.Session;

/// <summary>What this browser or app allows for notifications, before Drover's own server is asked.</summary>
public enum DevicePushSupport
{
    Available,
    Blocked,
    NotSupported,
    NeedsHomeScreen,
}


/// <summary>What the device allows, and the push service address it is subscribed at, if it is.</summary>
public sealed record DevicePushCheck(DevicePushSupport Support, string? Endpoint);


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
}
