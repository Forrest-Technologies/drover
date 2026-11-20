namespace Drover.Engine.Policy;

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

    /// <summary>
    /// States that this request goes to an address the operator configured for the whole server, such as its search
    /// service, so it may reach a private address whenever the deployment allows private addresses. Never for an
    /// address a space, a person, or a bot supplied: those carry the space's value with <see cref="ForSpace"/>.
    /// </summary>
    public static HttpRequestMessage ForOperatorAddress(this HttpRequestMessage request)
    {
        request.Options.Set(allowPrivateAddresses, true);
        return request;
    }

    public static HttpMessageHandler CreateHandler(DeploymentPolicy policy) =>
        CreateHandler(policy, Dns.GetHostAddressesAsync, PrivateAddresses.Contains);

    /// <summary>
    /// The same handler for a client that must never follow a redirect or store cookies: one whose requests carry
    /// a credential in a header of the caller's choosing or read untrusted sites across spaces, so neither a redirect
    /// nor a prior cookie may leak across requests or origins.
    /// </summary>
    public static HttpMessageHandler CreateHandlerWithoutRedirects(DeploymentPolicy policy) =>
        CreateHandler(policy, Dns.GetHostAddressesAsync, PrivateAddresses.Contains, followRedirects: false, useCookies: false);

    /// <summary>The same handler with its address lookup and its test for a refused address supplied by the caller.</summary>
    public static HttpMessageHandler CreateHandler(
        DeploymentPolicy policy,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        Func<IPAddress, bool> isRefused,
        bool followRedirects = true,
        bool useCookies = true) =>
        CreateHandler(policy, resolve, isRefused, Connect, followRedirects, useCookies);

    internal static HttpMessageHandler CreateHandler(
        DeploymentPolicy policy,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        Func<IPAddress, bool> isRefused,
        Func<IPAddress, int, CancellationToken, ValueTask<Stream>> connect,
        bool followRedirects = true,
        bool useCookies = true)
    {
        var guarded = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            UseProxy = false,
            AllowAutoRedirect = followRedirects,
            UseCookies = useCookies,
        };
        guarded.ConnectCallback = (context, cancellationToken) =>
            ConnectToCheckedAddress(context, resolve, isRefused, connect, cancellationToken);
        var open = policy.AllowPrivateAddresses
            ? new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AllowAutoRedirect = followRedirects,
                UseCookies = useCookies,
            }
            : null;
        return new RoutingHandler(guarded, open);
    }

    #endregion

    #region Private Methods

    private static async ValueTask<Stream> ConnectToCheckedAddress(
        SocketsHttpConnectionContext context,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        Func<IPAddress, bool> isRefused,
        Func<IPAddress, int, CancellationToken, ValueTask<Stream>> connect,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await resolve(host, cancellationToken);

        if (addresses.Length == 0 || addresses.Any(isRefused))
        {
            throw new OutboundBlockedException(
                $"This server does not allow requests to private addresses, and {host} is one. Use a public address, or ask the operator to allow private addresses.");
        }

        return await ConnectToFirstReachable(addresses, context.DnsEndPoint.Port, connect, cancellationToken);
    }

    private static async ValueTask<Stream> ConnectToFirstReachable(
        IPAddress[] addresses,
        int port,
        Func<IPAddress, int, CancellationToken, ValueTask<Stream>> connect,
        CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;

        foreach (var address in addresses)
        {
            try
            {
                return await connect(address, port, cancellationToken);
            }
            catch (SocketException failure)
            {
                lastFailure = failure;
            }
        }

        throw new HttpRequestException(
            HttpRequestError.ConnectionError,
            "None of the addresses for the host could be reached.",
            lastFailure);
    }

    private static async ValueTask<Stream> Connect(
        IPAddress address,
        int port,
        CancellationToken cancellationToken)
    {
        Socket? socket = null;

        try
        {
            socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket?.Dispose();
            throw;
        }
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// Sends each request through the handler its policy calls for, and puts a refusal from the connection check
    /// back as itself, so its message is the one the caller sees.
    /// </summary>
    private sealed class RoutingHandler(SocketsHttpHandler guarded, SocketsHttpHandler? open) : HttpMessageHandler
    {
        #region Private Fields

        private readonly HttpMessageInvoker guardedInvoker = new(guarded);

        private readonly HttpMessageInvoker? openInvoker = open is null ? null : new(open);

        #endregion

        #region Public Methods

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

        #endregion
    }

    #endregion
}
