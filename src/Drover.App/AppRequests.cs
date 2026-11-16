namespace Drover.App;

public sealed class AppRequests(ServerAddress server) : DelegatingHandler(new HttpClientHandler
{
    AllowAutoRedirect = false,
    CookieContainer = new(),
})
{
    #region Private Fields

    private const string AntiforgeryCookie = "XSRF-TOKEN";

    private const string AntiforgeryHeader = "X-XSRF-TOKEN";

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
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
        {
            request.Headers.Remove(AntiforgeryHeader);
            request.Headers.Add(AntiforgeryHeader, await ReadToken(origin, address, cancellationToken));
        }

        return await base.SendAsync(request, cancellationToken);
    }

    #endregion

    #region Private Methods

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

    private string? ReadCookie(Uri origin) =>
        ((HttpClientHandler)InnerHandler!).CookieContainer.GetCookies(origin)[AntiforgeryCookie]?.Value;

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
