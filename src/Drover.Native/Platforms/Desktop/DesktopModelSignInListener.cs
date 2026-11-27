namespace Drover.Native.Platform;

/// <summary>
/// Listens on this computer's loopback address for the browser's return from a model provider's sign-in page. The
/// provider takes a return on <c>127.0.0.1</c> at <see cref="ReturnPath"/>, where only the port may vary, so the
/// listener asks for <see cref="PreferredPort"/> and takes any free port when that one is in use. One listener waits
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

    public IModelSignInReturn Open()
    {
        try
        {
            return new Listening(Start(PreferredPort) ?? Start(0) ?? throw new SocketException(), logger);
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
    /// The query string in a request line such as <c>GET /auth/callback?code=...&amp;state=... HTTP/1.1</c>, or null for
    /// any other request. A return names its <c>state</c> and either a <c>code</c> or an <c>error</c>; anything else, such
    /// as a stray visit to the address, is not one and the listener keeps waiting.
    /// </summary>
    internal static string? QueryIn(string requestLine)
    {
        if (requestLine.Split(' ') is not ["GET", var target, ['H', 'T', 'T', 'P', '/', ..]]
            || !target.StartsWith($"{ReturnPath}?", StringComparison.Ordinal))
        {
            return null;
        }

        var query = target[(ReturnPath.Length + 1)..];
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

        private readonly CancellationTokenSource stopping = new();

        private readonly TaskCompletionSource<string> returned = new(TaskCreationOptions.RunContinuationsAsynchronously);

        #endregion

        #region Constructors

        public Listening(TcpListener listener, ILogger logger)
        {
            this.listener = listener;
            this.logger = logger;
            RedirectUri = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}{ReturnPath}");
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
                    var query = QueryIn(await DesktopSignInReturn.ReadRequestLine(stream, patience.Token));
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
