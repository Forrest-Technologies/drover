namespace Drover.Server.Sandboxes.Broker;

/// <summary>
/// How the broker reaches the hosts sandboxes ask for: raw connections for tunnels, and an HTTP client for the hosts
/// a secret is bound to. Both refuse private, loopback and link-local addresses whatever a space allows, since a
/// sandbox's traffic is a stranger's to the server's own network. HTTP follows no redirect and keeps no cookie.
/// </summary>
public sealed class BrokerUpstream(
    Func<string, int, CancellationToken, ValueTask<Stream>> connect,
    HttpMessageInvoker client)
{
    #region Properties

    public HttpMessageInvoker Client => client;

    #endregion

    #region Public Methods

    /// <summary>
    /// The way out the server uses: public addresses only, never one of <paramref name="refused"/> (the server's own,
    /// and the operator's list), checked after each name is resolved.
    /// </summary>
    public static BrokerUpstream ForPublicAddresses(DeploymentPolicy policy, IReadOnlyCollection<IPAddress> refused)
    {
        HashSet<IPAddress> own = [.. refused.Select(Unmapped)];
        bool IsRefused(IPAddress address) => PrivateAddresses.Contains(address) || own.Contains(Unmapped(address));
        return new(
            (host, port, cancellationToken) => OutboundHttp.ConnectToAllowedAddress(host, port, Dns.GetHostAddressesAsync, IsRefused, cancellationToken),
            new HttpMessageInvoker(
                OutboundHttp.CreateHandler(policy with { AllowPrivateAddresses = false }, Dns.GetHostAddressesAsync, IsRefused, followRedirects: false, useCookies: false),
                disposeHandler: true));
    }

    private static IPAddress Unmapped(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    public ValueTask<Stream> Connect(string host, int port, CancellationToken cancellationToken) => connect(host, port, cancellationToken);

    #endregion
}
