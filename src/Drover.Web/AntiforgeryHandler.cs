namespace Drover.Web;

/// <summary>
/// Sends the antiforgery token the server expects on every request that changes something. The server keeps
/// the token in a cookie the page can read; this copies it into the header.
/// </summary>
public sealed class AntiforgeryHandler(IJSRuntime js, ILogger<AntiforgeryHandler> logger) : DelegatingHandler
{
    #region Private Fields

    private const string CookieName = "XSRF-TOKEN";

    private const string HeaderName = "X-XSRF-TOKEN";

    private const string AntiforgeryPath = "api/antiforgery";

    // These begin a session, so a token left over from an earlier one would be refused.
    private static readonly string[] sessionStarts = ["/api/sign-in", "/api/setup", "/api/register"];

    #endregion

    #region Interface Implementations

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (ChangesSomething(request))
        {
            request.Headers.Remove(HeaderName);
            request.Headers.Add(HeaderName, await GetToken(request, cancellationToken));
        }

        return await base.SendAsync(request, cancellationToken);
    }

    #endregion

    #region Private Methods

    private static bool ChangesSomething(HttpRequestMessage request) =>
        request.Method != HttpMethod.Get && request.Method != HttpMethod.Head && request.Method != HttpMethod.Options;

    private async Task<string> GetToken(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = StartsSession(request) ? null : await ReadCookie(cancellationToken);

        if (string.IsNullOrEmpty(token))
        {
            using var issue = new HttpRequestMessage(HttpMethod.Get, new Uri(request.RequestUri!, $"/{AntiforgeryPath}"));
            using var issued = await base.SendAsync(issue, cancellationToken);
            token = await ReadCookie(cancellationToken);
        }

        return token ?? string.Empty;
    }

    private static bool StartsSession(HttpRequestMessage request) =>
        sessionStarts.Contains(request.RequestUri?.AbsolutePath.TrimEnd('/'), StringComparer.OrdinalIgnoreCase);

    private async Task<string?> ReadCookie(CancellationToken cancellationToken)
    {
        try
        {
            var module = await js.InvokeAsync<IJSObjectReference>("import", cancellationToken, "./cookies.js");
            await using (module)
            {
                return await module.InvokeAsync<string?>("read", cancellationToken, CookieName);
            }
        }
        catch (JSException exception)
        {
            logger.LogError(exception, "The cookie script failed to read the antiforgery token");
            throw;
        }
    }

    #endregion
}
