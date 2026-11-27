namespace Drover.Native.Platform;

/// <summary>
/// Listens on this computer's loopback address for the browser's return from a model provider's sign-in page. Each
/// sign-in kind names the loopback address its provider takes a return on (the server's kinds route), where only the
/// port may vary, so the listener keeps that host and path, asks for that port, and takes any free port when that one
/// is in use. With none named it listens on <c>127.0.0.1</c> at <see cref="ReturnPath"/> and <see cref="PreferredPort"/>.
/// It listens on the IPv4 loopback address; a browser that tries <c>localhost</c> on IPv6 first falls back to it. One listener waits
/// for one return and stops once the screen that started it is done. The page the browser gets back does not say the
/// person is signed in: the server has not finished yet when it is drawn.
/// </summary>
internal sealed class DesktopModelSignInListener(ILogger<DesktopModelSignInListener> logger) : IModelSignInListener
{
    #region Private Fields

    /// <summary>The provider's documented example port (https://developers.openai.com/siwc/llms-full.txt, "Registration and sign-in").</summary>
    public const int PreferredPort = 1455;

    /// <summary>The path the provider sends the browser back to. It must match the server's configured return address.</summary>
    public const string ReturnPath = "/auth/callback";

    #endregion

    #region Properties

    public bool CanListen => true;

    #endregion

    #region Interface Implementations

    public IModelSignInReturn Open(Uri? wanted = null)
    {
        var at = wanted ?? new Uri($"http://127.0.0.1:{PreferredPort}{ReturnPath}");

        if (!IsLoopback(at))
        {
            throw new DeviceCallFailedException("This sign-in does not come back to this computer.");
        }

        try
        {
            var port = at.IsDefaultPort ? 0 : at.Port;
            return new Listening(Start(port) ?? Start(0) ?? throw new SocketException(), at, logger);
        }
        catch (SocketException failure)
        {
            throw new DeviceCallFailedException("This computer would not let Drover listen for the sign-in.", failure);
        }
    }

    #endregion

    #region Private Methods

    private TcpListener? Start(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };

        try
        {
            listener.Start();
            return listener;
        }
        catch (SocketException taken) when (port != 0)
        {
            logger.LogInformation("Port {Port} could not be used for a model sign-in, so another is taken: {Failure}", port, taken.SocketErrorCode);
            listener.Dispose();
            return null;
        }
    }

    /// <summary>
    /// An address the browser comes back to on this computer: plain http to <c>127.0.0.1</c> or <c>localhost</c>, with a
    /// path and nothing else. Anything else would hand a return to some other machine, and is not listened for.
    /// </summary>
    internal static bool IsLoopback(Uri address) =>
        address is { IsAbsoluteUri: true, Scheme: "http", UserInfo.Length: 0, Query.Length: 0, Fragment.Length: 0 }
        && address.Host is "127.0.0.1" or "localhost"
        && address.AbsolutePath.StartsWith('/');

    /// <summary>
    /// The query string in a request line such as <c>GET /auth/callback?code=...&amp;state=... HTTP/1.1</c>, or null for
    /// any other request. A return names its <c>state</c> and either a <c>code</c> or an <c>error</c>; anything else, such
    /// as a stray visit to the address, is not one and the listener keeps waiting.
    /// </summary>
    internal static string? QueryIn(string requestLine, string path = ReturnPath)
    {
        if (requestLine.Split(' ') is not ["GET", var target, ['H', 'T', 'T', 'P', '/', ..]]
            || !target.StartsWith($"{path}?", StringComparison.Ordinal))
        {
            return null;
        }

        var query = target[(path.Length + 1)..];
        HashSet<string> names = [.. query.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(pair => pair.Split('=', 2)[0])];
        return names.Contains("state") && (names.Contains("code") || names.Contains("error")) ? query : null;
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// One return being waited for. It reads each caller's request line, answers the return with a page that loads
    /// nothing, and any other path with a page that says it is not one; after the first return it stops.
    /// </summary>
    private sealed class Listening : IModelSignInReturn
    {
        #region Private Fields

        private readonly TcpListener listener;

        private readonly ILogger logger;

        /// <summary>The path a return arrives on: the kind's own.</summary>
        private readonly string path;

        private readonly CancellationTokenSource stopping = new();

        private readonly TaskCompletionSource<string> returned = new(TaskCreationOptions.RunContinuationsAsynchronously);

        #endregion

        #region Constructors

        public Listening(TcpListener listener, Uri wanted, ILogger logger)
        {
            this.listener = listener;
            this.logger = logger;
            path = wanted.AbsolutePath;
            RedirectUri = new Uri($"http://{wanted.Host}:{((IPEndPoint)listener.LocalEndpoint).Port}{path}");
            _ = Listen();
        }

        #endregion

        #region Properties

        public Uri RedirectUri { get; }

        public Task<string> Returned => returned.Task;

        #endregion

        #region Interface Implementations

        public void Dispose()
        {
            stopping.Cancel();
            listener.Stop();
            listener.Dispose();
            returned.TrySetCanceled();
            stopping.Dispose();
        }

        #endregion

        #region Private Methods

        private async Task Listen()
        {
            while (!returned.Task.IsCompleted)
            {
                try
                {
                    using var caller = await listener.AcceptTcpClientAsync(stopping.Token);
                    using var patience = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
                    patience.CancelAfter(TimeSpan.FromSeconds(10));
                    var stream = caller.GetStream();
                    var query = QueryIn(await DesktopSignInReturn.ReadRequestLine(stream, patience.Token), path);
                    var page = query is null
                        ? DesktopSignInReturn.Page("404 Not Found", "Nothing here", "This address only brings a sign-in back into the Drover app.")
                        : DesktopSignInReturn.Page("200 OK", "Return to Drover", "You can close this tab. Drover is finishing the sign-in.");
                    await stream.WriteAsync(page, patience.Token);

                    if (query is not null)
                    {
                        returned.TrySetResult(query);
                    }
                }
                catch (Exception ended) when (ended is OperationCanceledException or ObjectDisposedException or SocketException or IOException)
                {
                    logger.LogInformation("The model sign-in listener stopped or a caller left early: {Failure}", ended.GetType().Name);

                    if (stopping.IsCancellationRequested)
                    {
                        return;
                    }
                }
            }
        }

        #endregion
    }

    #endregion
}
