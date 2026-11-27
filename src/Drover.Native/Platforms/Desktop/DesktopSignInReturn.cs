namespace Drover.Native.Platform;

/// <summary>
/// Where a browser comes back into the desktop app after a tool server's sign-in page. No address scheme can be
/// claimed the same way on every desktop system, so the app listens on this computer's loopback address, at a port
/// the system chooses, and names that address when a sign-in starts. The server sends the browser there with a
/// one-time ticket, and the ticket is handed on to the tools screen, which ends the sign-in with the server.
/// </summary>
/// <remarks>
/// Any program on this computer can reach the listener. The ticket means something only to the server, and only for
/// the signed-in person who started the sign-in, so nothing here trusts it beyond its shape. The listener reads no
/// more than a request line and headers, answers with a page that loads nothing, and closes the connection. It holds
/// only a few of those reads at once. A further connection is refused at once, so it cannot pin a socket and a timer.
/// </remarks>
internal sealed class DesktopSignInReturn : IDisposable
{
    #region Private Fields

    /// <summary>The path the server sends the browser to on the listener: the host of an app's return address.</summary>
    public const string ReturnPath = "/tool-sign-in";

    // A ticket is 32 random bytes in URL-safe base64. Anything else is not one of the server's.
    private const int TicketLength = 43;

    private const int MostRequestBytes = 8192;

    /// <summary>How many incomplete requests are read at once. The next connection is refused.</summary>
    internal const int MostCallers = 4;

    private static readonly TimeSpan readPatience = TimeSpan.FromSeconds(10);

    private static readonly byte[] headersEnd = "\r\n\r\n"u8.ToArray();

    private static readonly byte[] overloaded =
        "HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n"u8.ToArray();

    private readonly TcpListener listener;

    private readonly Action<string> arrived;

    private readonly ILogger logger;

    private readonly CancellationTokenSource stopping = new();

    private readonly SemaphoreSlim callers = new(MostCallers, MostCallers);

    #endregion

    #region Constructors

    private DesktopSignInReturn(TcpListener listener, Action<string> arrived, ILogger logger)
    {
        this.listener = listener;
        this.arrived = arrived;
        this.logger = logger;
        Address = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        _ = Listen();
    }

    #endregion

    #region Properties

    /// <summary>The address the server is given, such as <c>http://127.0.0.1:53124</c>: no path, no trailing slash.</summary>
    public string Address { get; }

    #endregion

    #region Public Methods

    /// <summary>Starts listening. <paramref name="arrived"/> is given each well-formed ticket a browser brings, on a background thread.</summary>
    public static DesktopSignInReturn Open(Action<string> arrived, ILogger<DesktopSignInReturn> logger)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new DesktopSignInReturn(listener, arrived, logger);
    }

    /// <summary>The ticket in a request line such as <c>GET /tool-sign-in?ticket=... HTTP/1.1</c>, or null when it carries none.</summary>
    public static string? TicketIn(string requestLine)
    {
        var parts = requestLine.Split(' ');

        if (parts is not ["GET", var target, ['H', 'T', 'T', 'P', '/', ..]]
            || !target.StartsWith($"{ReturnPath}?", StringComparison.Ordinal))
        {
            return null;
        }

        var ticket = target[(ReturnPath.Length + 1)..].Split('&')
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair is ["ticket", _])
            .Select(pair => pair[1])
            .FirstOrDefault();
        return ticket is { Length: TicketLength } && ticket.All(letter => char.IsAsciiLetterOrDigit(letter) || letter is '-' or '_') ? ticket : null;
    }

    #endregion

    #region Interface Implementations

    public void Dispose()
    {
        stopping.Cancel();
        listener.Stop();
        listener.Dispose();
        stopping.Dispose();
        callers.Dispose();
    }

    #endregion

    #region Private Methods

    private async Task Listen()
    {
        while (!stopping.IsCancellationRequested)
        {
            TcpClient caller;

            try
            {
                caller = await listener.AcceptTcpClientAsync(stopping.Token);
            }
            catch (Exception ended) when (ended is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                // Stopped with the app, or the system took the port away: the next sign-in says it could not come back.
                logger.LogInformation("The desktop sign-in listener stopped: {Failure}", ended.GetType().Name);
                return;
            }

            var room = false;

            try
            {
                room = await callers.WaitAsync(TimeSpan.Zero);
            }
            catch (ObjectDisposedException disposed)
            {
                caller.Dispose();
                logger.LogInformation("The desktop sign-in listener stopped while accepting: {Failure}", disposed.GetType().Name);
                return;
            }

            if (!room)
            {
                Refuse(caller);
                continue;
            }

            _ = Answer(caller);
        }
    }

    private async Task Answer(TcpClient caller)
    {
        try
        {
            using var connection = caller;
            using var patience = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
            patience.CancelAfter(readPatience);

            try
            {
                var stream = connection.GetStream();
                var ticket = TicketIn(await ReadRequestLine(stream, patience.Token));

                if (ticket is not null)
                {
                    arrived(ticket);
                }

                await stream.WriteAsync(Reply(ticket is not null), patience.Token);
            }
            catch (Exception failure) when (failure is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                logger.LogInformation("A request to the desktop sign-in listener ended early: {Failure}", failure.GetType().Name);
            }
        }
        finally
        {
            try
            {
                callers.Release();
            }
            catch (ObjectDisposedException disposed)
            {
                logger.LogInformation("The desktop sign-in listener closed while a request finished: {Failure}", disposed.GetType().Name);
            }
        }
    }

    // A connection past the limit is not read. The reply is short so this does not sit on the accept loop.
    private void Refuse(TcpClient caller)
    {
        using var connection = caller;

        try
        {
            connection.NoDelay = true;
            connection.Client.SendTimeout = 200;
            var sent = 0;

            while (sent < overloaded.Length)
            {
                sent += connection.Client.Send(overloaded, sent, overloaded.Length - sent, SocketFlags.None);
            }

            connection.Client.Shutdown(SocketShutdown.Send);
            logger.LogInformation("The desktop sign-in listener refused a connection because it was already busy");
        }
        catch (Exception failure) when (failure is SocketException or IOException or ObjectDisposedException)
        {
            logger.LogInformation("A refused desktop sign-in connection closed early: {Failure}", failure.GetType().Name);
        }
    }

    // Reads the request up to the end of its headers, so the connection is not reset under the answer, and returns
    // its first line. A request too long to be a browser's return is cut off where the limit falls.
    internal static async Task<string> ReadRequestLine(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MostRequestBytes];
        var read = 0;

        while (read < buffer.Length && buffer.AsSpan(0, read).IndexOf(headersEnd) < 0)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken);

            if (count == 0)
            {
                break;
            }

            read += count;
        }

        var head = Encoding.ASCII.GetString(buffer, 0, read);
        var lineEnd = head.IndexOf("\r\n", StringComparison.Ordinal);
        return lineEnd < 0 ? string.Empty : head[..lineEnd];
    }

    private static byte[] Reply(bool returned) =>
        returned
            ? Page("200 OK", "Return to Drover", "Go back to the Drover app. It is finishing the sign-in, and you can close this tab.")
            : Page("404 Not Found", "Nothing here", "This address only brings a sign-in back into the Drover app.");

    // A page that loads nothing and sends no referrer, so what is in the address goes nowhere else.
    internal static byte[] Page(string status, string title, string words)
    {
        var body = Encoding.UTF8.GetBytes(
            $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="color-scheme" content="dark light">
            <title>{title}</title>
            </head>
            <body>
            <main>
            <h1>{title}</h1>
            <p>{words}</p>
            </main>
            </body>
            </html>
            """);
        string[] headers =
        [
            $"HTTP/1.1 {status}",
            "Content-Type: text/html; charset=utf-8",
            $"Content-Length: {body.Length}",
            "Content-Security-Policy: default-src 'none'",
            "Referrer-Policy: no-referrer",
            "Cache-Control: no-store",
            "X-Content-Type-Options: nosniff",
            "Connection: close",
            string.Empty,
            string.Empty,
        ];
        var head = Encoding.ASCII.GetBytes(string.Join("\r\n", headers));
        return [.. head, .. body];
    }

    #endregion
}
