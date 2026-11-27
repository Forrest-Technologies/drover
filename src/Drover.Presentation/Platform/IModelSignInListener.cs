namespace Drover.Presentation.Platform;

/// <summary>
/// Where a browser comes back to this device after a model provider's sign-in page. A provider that signs a person in
/// with their own plan sends the browser to a loopback address on the computer the browser runs on, so only a head
/// that can listen there (the desktop app) can finish that sign-in by itself. The query string the browser brings is
/// handed to the server, which makes the exchange; no token ever reaches the device.
/// </summary>
public interface IModelSignInListener
{
    /// <summary>False on a head that cannot listen on this computer's loopback address, such as a phone or a browser tab.</summary>
    bool CanListen { get; }

    /// <summary>
    /// Starts listening for one return at <paramref name="wanted"/>, the kind's loopback return address: its host and
    /// path as they are, its port when free and any free port otherwise. Null listens where the first plan kind's
    /// provider sends the browser back. Throws <see cref="DeviceCallFailedException"/> when the device cannot listen
    /// now, for example because no port is free, or when <paramref name="wanted"/> is not a loopback address.
    /// </summary>
    IModelSignInReturn Open(Uri? wanted = null);
}


/// <summary>One return being waited for. Disposing it stops listening.</summary>
public interface IModelSignInReturn : IDisposable
{
    /// <summary>The address the server is told the browser comes back to.</summary>
    Uri RedirectUri { get; }

    /// <summary>The query string the browser brought back, without its leading <c>?</c>, once it has.</summary>
    Task<string> Returned { get; }
}


/// <summary>A head that cannot listen for a sign-in's return.</summary>
public sealed class NoModelSignInListener : IModelSignInListener
{
    #region Properties

    public bool CanListen => false;

    #endregion

    #region Interface Implementations

    public IModelSignInReturn Open(Uri? wanted = null) => throw new DeviceCallFailedException("This device cannot listen for a sign-in.");

    #endregion
}
