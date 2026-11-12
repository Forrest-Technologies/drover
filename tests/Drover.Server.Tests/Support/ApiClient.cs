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

    #endregion

    #region Public Methods

    public Task<HttpResponseMessage> Get(string path) => Send(HttpMethod.Get, path, body: null, token: false);

    /// <summary>A state-changing request that carries the antiforgery token, fetching one first if there is none.</summary>
    public async Task<HttpResponseMessage> Post(string path, object? body = null)
    {
        if (Cookie("XSRF-TOKEN") is null)
        {
            (await Get("/api/antiforgery")).EnsureSuccessStatusCode();
        }

        return await Send(HttpMethod.Post, path, body, token: true);
    }

    /// <summary>A state-changing request with no antiforgery token at all.</summary>
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
