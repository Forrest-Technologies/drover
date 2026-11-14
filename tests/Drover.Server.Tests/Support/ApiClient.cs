namespace Drover.Server.Tests.Support;

/// <summary>
/// A browser's part of the conversation: it keeps the cookies the server sets and sends the antiforgery token back
/// in the header. The cookie jar is by hand because the cookies are <c>Secure</c> and a test server has no scheme.
/// </summary>
public sealed class ApiClient(HttpClient http)
{
    #region Private Fields

    private readonly Dictionary<string, string> cookies = [];

    #endregion

    #region Properties

    public string? Cookie(string name) => cookies.GetValueOrDefault(name);

    public string? RemoteIp { get; set; }

    /// <summary>The cookies as one request header, for a client that is not this one but acts as the same person.</summary>
    public string CookieHeader => string.Join("; ", cookies.Select(cookie => $"{cookie.Key}={cookie.Value}"));

    /// <summary>Headers a proxy would add, sent with every request.</summary>
    public Dictionary<string, string> ForwardedHeaders { get; } = [];

    #endregion

    #region Public Methods

    /// <summary>Copies another client's cookies, as someone who stole them would.</summary>
    public void TakeCookiesOf(ApiClient other)
    {
        foreach (var (name, value) in other.cookies)
        {
            cookies[name] = value;
        }
    }

    public Task<HttpResponseMessage> Get(string path) => Send(HttpMethod.Get, path, body: null, token: false);

    /// <summary>Opens a response without reading its body, for a stream. The caller disposes it to disconnect.</summary>
    public async Task<HttpResponseMessage> Open(string path, long? lastEventId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (cookies.Count > 0)
        {
            request.Headers.Add("Cookie", CookieHeader);
        }

        if (lastEventId is { } seen)
        {
            request.Headers.Add("Last-Event-ID", seen.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    /// <summary>A state-changing request that carries the antiforgery token, fetching one first if there is none.</summary>
    public async Task<HttpResponseMessage> Post(string path, object? body = null)
    {
        if (Cookie("XSRF-TOKEN") is null)
        {
            (await Get("/api/antiforgery")).EnsureSuccessStatusCode();
        }

        return await Send(HttpMethod.Post, path, body, token: true);
    }

    public async Task<HttpResponseMessage> Put(string path, object? body = null)
    {
        if (Cookie("XSRF-TOKEN") is null)
        {
            (await Get("/api/antiforgery")).EnsureSuccessStatusCode();
        }

        return await Send(HttpMethod.Put, path, body, token: true);
    }

    /// <summary>A state-changing request with no antiforgery token at all.</summary>
    public async Task<HttpResponseMessage> Delete(string path)
    {
        if (Cookie("XSRF-TOKEN") is null)
        {
            (await Get("/api/antiforgery")).EnsureSuccessStatusCode();
        }

        return await Send(HttpMethod.Delete, path, body: null, token: true);
    }

    public Task<HttpResponseMessage> PostWithoutToken(string path, object? body = null) =>
        Send(HttpMethod.Post, path, body, token: false);

    public async Task<HttpResponseMessage> SignIn(string email, string password) =>
        await Post("/api/sign-in", new SignInRequest(email, password));

    #endregion

    #region Private Methods

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body, bool token)
    {
        var request = new HttpRequestMessage(method, path);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        if (cookies.Count > 0)
        {
            request.Headers.Add("Cookie", string.Join("; ", cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));
        }

        if (token && Cookie("XSRF-TOKEN") is { } xsrf)
        {
            request.Headers.Add("X-XSRF-TOKEN", xsrf);
        }

        if (RemoteIp is not null)
        {
            request.Headers.Add(DroverFactory.RemoteIpHeader, RemoteIp);
        }

        foreach (var (name, value) in ForwardedHeaders)
        {
            request.Headers.Add(name, value);
        }

        var response = await http.SendAsync(request);
        Remember(response);
        return response;
    }

    private void Remember(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var lines))
        {
            return;
        }

        foreach (var cookie in SetCookieHeaderValue.ParseList([.. lines]))
        {
            var name = cookie.Name.Value!;
            var expired = cookie.Expires is { } expires && expires < DateTimeOffset.UtcNow;

            if (expired || string.IsNullOrEmpty(cookie.Value.Value))
            {
                cookies.Remove(name);
            }
            else
            {
                cookies[name] = cookie.Value.Value;
            }
        }
    }

    #endregion
}
