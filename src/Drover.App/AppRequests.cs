namespace Drover.App;

public sealed class AppRequests(ServerAddress server, NativeSession session) : DelegatingHandler(new HttpClientHandler
{
    AllowAutoRedirect = false,
    CookieContainer = new(),
})
{
    #region Private Fields

    private const string AntiforgeryCookie = "XSRF-TOKEN";

    private const string SessionCookie = "__Host-Drover.Session";

    internal const string AntiforgeryHeader = "X-XSRF-TOKEN";

    #endregion

    #region Properties

    internal static Uri Placeholder { get; } = new("https://drover.invalid/");

    #endregion

    #region Public Methods

    public async Task<bool> IsDroverServer(Uri origin, CancellationToken cancellationToken)
    {
        await IssueToken(origin, cancellationToken);
        return ReadCookie(origin) is { Length: > 0 };
    }

    #endregion

    #region Interface Implementations

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (server.Current is not { } origin || request.RequestUri is not { } address
            || !Placeholder.IsBaseOf(address) || !address.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal))
        {
            throw new HttpRequestException("The request did not name an API on the selected Drover server.");
        }

        request.RequestUri = new(origin, address.PathAndQuery);
        var path = address.AbsolutePath.TrimEnd('/');
        var opening = request.Method == HttpMethod.Post && path is "/api/sign-in" or "/api/setup" or "/api/register";
        var access = opening || path == "/api/password-reset"
            ? null
            : await session.GetAccess(origin, base.SendAsync, cancellationToken);

        if (access is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        }
        else if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
        {
            request.Headers.Remove(AntiforgeryHeader);
            request.Headers.Add(AntiforgeryHeader, await ReadToken(origin, address, cancellationToken));
        }

        var response = await base.SendAsync(request, cancellationToken);
        try
        {
            await UpdateSession(origin, path, opening, access, response, cancellationToken);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    #endregion

    #region Private Methods

    private async Task UpdateSession(
        Uri origin, string path, bool opening, string? access, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if ((path == "/api/sign-out" && response.IsSuccessStatusCode)
            || (path == "/api/account/password" && response.StatusCode == HttpStatusCode.Unauthorized))
        {
            await session.Clear(origin, access);
            return;
        }

        if (path == "/api/session" && access is not null && response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await session.Clear(origin, access);
            return;
        }

        if (response.IsSuccessStatusCode && (opening || path == "/api/account/password"))
        {
            var token = ReadCookie(origin);
            if (token is not null && await session.Exchange(origin, base.SendAsync, token, cancellationToken))
            {
                Cookies.Add(origin, new Cookie(SessionCookie, string.Empty, "/") { Expired = true, Secure = true });
            }
        }
    }

    private async Task<string> ReadToken(Uri origin, Uri address, CancellationToken cancellationToken)
    {
        var token = ReadCookie(origin);
        if (string.IsNullOrEmpty(token) || address.AbsolutePath is "/api/sign-in" or "/api/setup" or "/api/register")
        {
            await IssueToken(origin, cancellationToken);
            token = ReadCookie(origin);
        }

        return token ?? throw new HttpRequestException("Drover did not issue a request token.");
    }

    private CookieContainer Cookies => ((HttpClientHandler)InnerHandler!).CookieContainer;

    private string? ReadCookie(Uri origin) => Cookies.GetCookies(origin)[AntiforgeryCookie]?.Value;

    private async Task IssueToken(Uri origin, CancellationToken cancellationToken)
    {
        using var issue = new HttpRequestMessage(HttpMethod.Get, new Uri(origin, "api/antiforgery"));
        using var response = await base.SendAsync(issue, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NoContent)
        {
            throw new HttpRequestException("The server did not answer with a Drover antiforgery token.");
        }
    }

    #endregion
}
