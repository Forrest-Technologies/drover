namespace Drover.Tests.Common;

/// <summary>
/// A web server on every local address that answers each request with whatever the test says, and counts them.
/// It is hand-made so the test controls the host name a request arrives with.
/// </summary>
public sealed class TestWebServer : IDisposable
{
    #region Private Fields

    private readonly TcpListener listener = new(IPAddress.Any, 0);

    private readonly Func<string, string> respond;

    private int requests;

    #endregion

    #region Constructors

    public TestWebServer(Func<string, string> respond)
    {
        this.respond = respond;
        listener.Start();
        _ = Serve();
    }

    #endregion

    #region Properties

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public int Requests => Volatile.Read(ref requests);

    #endregion

    #region Public Methods

    public static string Ok(string body) =>
        $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}";

    public static string Redirect(string location) =>
        $"HTTP/1.1 302 Found\r\nLocation: {location}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";

    #endregion

    #region Interface Implementations

    public void Dispose() => listener.Stop();

    #endregion

    #region Private Methods

    private async Task Serve()
    {
        try
        {
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();
                _ = Answer(client);
            }
        }
        // Dispose stops the listener to end this test-only accept loop; shutdown has no failure to propagate.
#pragma warning disable DRV0005
        catch (Exception failure) when (failure is ObjectDisposedException or SocketException)
        {
            // The test finished and stopped the listener.
        }
#pragma warning restore DRV0005
    }

    private async Task Answer(TcpClient client)
    {
        using (client)
        {
            var buffer = new byte[4096];
            var read = await client.GetStream().ReadAsync(buffer);
            Interlocked.Increment(ref requests);
            var host = Encoding.ASCII.GetString(buffer, 0, read);
            await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(respond(host)));
        }
    }

    #endregion
}
