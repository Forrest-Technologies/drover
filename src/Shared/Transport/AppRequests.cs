namespace Drover.Transport;

public sealed class AppRequests : DelegatingHandler
{
    #region Private Fields

    private const string AntiforgeryCookie = "XSRF-TOKEN";

    private const string SessionCookie = "__Host-Drover.Session";

    internal const string AntiforgeryHeader = "X-XSRF-TOKEN";

    private readonly ServerAddress server;

    private readonly NativeSession session;

    private readonly CookieContainer cookies;

    #endregion

    #region Constructors

    public AppRequests(ServerAddress server, NativeSession session)
        : this(server, session, new HttpClientHandler { AllowAutoRedirect = false })
    {
    }

    internal AppRequests(ServerAddress server, NativeSession session, HttpMessageHandler transport)
        : base(transport)
    {
        this.server = server;
        this.session = session;
        cookies = transport is HttpClientHandler handler ? handler.CookieContainer : new();
    }

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
            if (request.Method == HttpMethod.Get && path == "/api/session" && access is not null
                && response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return await RetrySession(request, response, origin, access, cancellationToken);
            }

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

    private async Task<HttpResponseMessage> RetrySession(
        HttpRequestMessage request,
        HttpResponseMessage rejected,
        Uri origin,
        string rejectedAccess,
        CancellationToken cancellationToken)
    {
        var renewed = await session.GetAccess(origin, base.SendAsync, cancellationToken, rejectedAccess);
        if (renewed is null)
        {
            return rejected;
        }

        rejected.Dispose();
        using var retry = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
        };
        foreach (var header in request.Headers)
        {
            retry.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", renewed);
        var response = await base.SendAsync(retry, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            throw new HttpRequestException("Drover could not verify the renewed session.", null, HttpStatusCode.Unauthorized);
        }

        return response;
    }

    private async Task UpdateSession(
        Uri origin, string path, bool opening, string? access, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (path == "/api/sign-out" && response.IsSuccessStatusCode)
        {
            await session.Clear(origin, access);
            return;
        }

        if (response.IsSuccessStatusCode && (opening || path == "/api/account/password"))
        {
            var token = ReadCookie(origin);
            if (token is not null && await session.Exchange(origin, base.SendAsync, token, cancellationToken))
            {
                cookies.Add(origin, new Cookie(SessionCookie, string.Empty, "/") { Expired = true, Secure = true });
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

    private string? ReadCookie(Uri origin) => cookies.GetCookies(origin)[AntiforgeryCookie]?.Value;

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
