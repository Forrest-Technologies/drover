namespace Drover.Engine.Policy;

/// <summary>Raised when an outbound request is refused because it would reach a private address.</summary>
public sealed class OutboundBlockedException(string message) : HttpRequestException(message);

/// <summary>
/// The one handler every outbound client uses. A request is allowed to reach private addresses only when the
/// deployment allows it and the request says so: the caller that knows the space puts the space's effective value on
/// the request with <see cref="ForSpace"/>, and a request that says nothing is treated as forbidden. A forbidden
/// request goes through a handler that resolves the host, refuses the request if any returned address is private,
/// and connects to the address it checked, so a second lookup cannot return a different answer. The check runs for
/// every new connection, so a redirect to another host is checked again. Forbidden and allowed requests never share
/// a pooled connection. HTTP/3 is refused, because it does not go through the connection check. A proxy would be
/// asked to connect on the server's behalf, so the forbidden path does not use one.
/// </summary>
public static class OutboundHttp
{
    #region Private Fields

    private static readonly HttpRequestOptionsKey<bool> allowPrivateAddresses = new("Drover.AllowPrivateAddresses");

    #endregion

    #region Public Methods

    /// <summary>States whether this request may reach private addresses, from the space's effective policy.</summary>
    public static HttpRequestMessage ForSpace(this HttpRequestMessage request, EffectivePolicy policy)
    {
        request.Options.Set(allowPrivateAddresses, policy.AllowPrivateAddresses);
        return request;
    }

    public static HttpMessageHandler CreateHandler(DeploymentPolicy policy) =>
        CreateHandler(policy, Dns.GetHostAddressesAsync, PrivateAddresses.Contains);

    /// <summary>The same handler with its address lookup and its test for a refused address supplied by the caller.</summary>
    public static HttpMessageHandler CreateHandler(
        DeploymentPolicy policy,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        Func<IPAddress, bool> isRefused)
    {
        var guarded = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5), UseProxy = false };
        guarded.ConnectCallback = (context, cancellationToken) =>
            ConnectToCheckedAddress(context, resolve, isRefused, cancellationToken);
        var open = policy.AllowPrivateAddresses
            ? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }
            : null;
        return new RoutingHandler(guarded, open);
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
    /// Sends each request through the handler its policy calls for, and puts a refusal from the connection check
    /// back as itself, so its message is the one the caller sees.
    /// </summary>
    private sealed class RoutingHandler(SocketsHttpHandler guarded, SocketsHttpHandler? open) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker guardedInvoker = new(guarded);

        private readonly HttpMessageInvoker? openInvoker = open is null ? null : new(open);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Version.Major >= 3)
            {
                throw new OutboundBlockedException("This server does not make HTTP/3 requests. Use HTTP/2 or HTTP/1.1.");
            }

            // A request that could be upgraded to HTTP/3 by the server's Alt-Svc header would skip the check.
            request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            var mayReachPrivate = request.Options.TryGetValue(allowPrivateAddresses, out var allowed) && allowed;
            var invoker = mayReachPrivate && openInvoker is not null ? openInvoker : guardedInvoker;

            try
            {
                return await invoker.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException failure) when (failure.InnerException is OutboundBlockedException blocked)
            {
                throw blocked;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                guardedInvoker.Dispose();
                openInvoker?.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    #endregion
}
