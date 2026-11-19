namespace Drover.Native.UITests;

/// <summary>
/// The account and the bot the app signs in to and talks to. They are made through the API, as the setup screens
/// make them, over a connection that trusts the run's certificate.
/// </summary>
internal sealed class CheckAccount
{
    #region Constructors

    private CheckAccount(string email, string password)
    {
        Email = email;
        Password = password;
    }

    #endregion

    #region Properties

    public string Email { get; }

    public string Password { get; }

    #endregion

    #region Public Methods

    public static async Task<CheckAccount> Create(CheckServer server, X509Certificate2 certificate)
    {
        var account = new CheckAccount("screen-checks@example.test", $"pw-{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}");
        var cookies = new CookieContainer();
        using var http = Connect(server.Origin, certificate, cookies);
        var session = await Send<SessionResponse>(http, cookies, "/api/setup",
            new SetupRequest(account.Email, "Screen checks", account.Password, server.SetupCode));
        var space = $"/api/spaces/{session.Spaces[0].Id}";
        var connection = await Send<ModelConnectionSummary>(http, cookies, $"{space}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted"));
        await Send<BotSummary>(http, cookies, $"{space}/bots", new CreateBotRequest("Check bot", "Answer briefly.", connection.Id, null));
        return account;
    }

    #endregion

    #region Private Methods

    // The harness talks only to its own test server, with a cookie jar as a browser keeps one.
    private static HttpClient Connect(Uri origin, X509Certificate2 certificate, CookieContainer cookies)
    {
        var hash = TestCertificate.Hash(certificate);
        var handler = new HttpClientHandler
        {
            CookieContainer = cookies,
            ServerCertificateCustomValidationCallback = (_, presented, _, _) =>
                string.Equals(presented?.GetCertHashString(HashAlgorithmName.SHA256), hash, StringComparison.OrdinalIgnoreCase),
        };
        return new HttpClient(handler) { BaseAddress = origin };
    }

    private static async Task<T> Send<T>(HttpClient http, CookieContainer cookies, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, body.GetType()) };
        request.Headers.Add("X-XSRF-TOKEN", await Token(http, cookies));
        var response = await http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"POST {path} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return await response.Content.ReadFromJsonAsync<T>()
            ?? throw new InvalidOperationException($"POST {path} returned no body.");
    }

    private static async Task<string> Token(HttpClient http, CookieContainer cookies)
    {
        (await http.GetAsync(new Uri("/api/antiforgery", UriKind.Relative))).EnsureSuccessStatusCode();
        return cookies.GetAllCookies().FirstOrDefault(cookie => cookie.Name == "XSRF-TOKEN")?.Value
            ?? throw new InvalidOperationException("The server set no antiforgery cookie.");
    }

    #endregion
}
