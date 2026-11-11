namespace Drover.Engine.Policy;

/// <summary>Raised when an outbound request is refused because it would reach a private address.</summary>
public sealed class OutboundBlockedException(string message) : HttpRequestException(message);

/// <summary>
/// The one handler every outbound client uses. When the deployment forbids private addresses it resolves the
/// host, refuses the request if any returned address is private, and connects to the address it checked, so a
/// second lookup cannot return a different answer. The check runs for every new connection, so a redirect to
/// another host is checked again. A proxy would be asked to connect on the server's behalf, so a guarded handler
/// does not use one.
/// </summary>
public static class OutboundHttp
{
    #region Public Methods

    public static HttpMessageHandler CreateHandler(DeploymentPolicy policy) =>
        CreateHandler(policy, Dns.GetHostAddressesAsync, PrivateAddresses.Contains);

    /// <summary>The same handler with its address lookup and its test for a refused address supplied by the caller.</summary>
    public static HttpMessageHandler CreateHandler(
        DeploymentPolicy policy,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        Func<IPAddress, bool> isRefused)
    {
        var sockets = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };

        if (!policy.AllowPrivateAddresses)
        {
            sockets.UseProxy = false;
            sockets.ConnectCallback = (context, cancellationToken) =>
                ConnectToCheckedAddress(context, resolve, isRefused, cancellationToken);
        }

        return new BlockedRequestHandler(sockets);
    }

    #endregion

    #region Private Methods

    private static async ValueTask<Stream> ConnectToCheckedAddress(
        SocketsHttpConnectionContext context,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        Func<IPAddress, bool> isRefused,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await resolve(host, cancellationToken);

        if (addresses.Length == 0 || addresses.Any(isRefused))
        {
            throw new OutboundBlockedException(
                $"This server does not allow requests to private addresses, and {host} is one. Use a public address, or ask the operator to allow private addresses.");
        }

        return await ConnectToFirstReachable(addresses, context.DnsEndPoint.Port, cancellationToken);
    }

    private static async ValueTask<Stream> ConnectToFirstReachable(
        IPAddress[] addresses,
        int port,
        CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;

        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException failure)
            {
                socket.Dispose();
                lastFailure = failure;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new HttpRequestException(
            HttpRequestError.ConnectionError,
            "None of the addresses for the host could be reached.",
            lastFailure);
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// The connection callback's refusal reaches the caller wrapped in a general failure. This puts the refusal
    /// back as itself, so its message is the one the caller sees.
    /// </summary>
    private sealed class BlockedRequestHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            try
            {
                return await base.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException failure) when (failure.InnerException is OutboundBlockedException blocked)
            {
                throw blocked;
            }
        }
    }

    #endregion
}
