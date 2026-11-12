namespace Drover.Server.Tests.Support;

/// <summary>
/// Sends requests to the in-process server that is running now, as one signed-in person. While no server is attached
/// it fails the way an unreachable host does, which is what a client sees during a restart.
/// </summary>
public sealed class ServerRelay(ApiClient signedIn) : HttpMessageHandler
{
    #region Private Fields

    private HttpMessageInvoker? server;

    #endregion

    #region Public Methods

    public void Attach(HttpMessageHandler handler) => Volatile.Write(ref server, new HttpMessageInvoker(handler));

    public void Detach() => Volatile.Write(ref server, null);

    #endregion

    #region Protected Methods

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref server) is not { } running)
        {
            throw new HttpRequestException("No server is running.");
        }

        request.Headers.Remove("Cookie");

        if (signedIn.CookieHeader is { Length: > 0 } cookies)
        {
            request.Headers.Add("Cookie", cookies);
        }

        try
        {
            return await running.SendAsync(request, cancellationToken);
        }
        catch (Exception stopped) when (stopped is ObjectDisposedException or InvalidOperationException or IOException)
        {
            throw new HttpRequestException("The server stopped.", stopped);
        }
    }

    #endregion
}
