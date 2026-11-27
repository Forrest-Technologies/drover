namespace Drover.Adapters.Tests.Support;

/// <summary>
/// GitHub's OAuth authorize, device, token, user, and revoke endpoints for a Copilot plan sign-in, played in memory.
/// It issues GitHub user tokens, rotates refresh tokens, and records only exchanges that produced a token set so a
/// pending device poll is not an exchange.
/// </summary>
public sealed class CopilotAuthServer
{
    #region Private Fields

    public const string TestClientId = "Iv1.test-client";

    public const string TestClientSecret = "copilot-test-secret";

    public const string DeviceCode = "device-1";

    public const string UserCode = "ABCD-EFGH";

    private int tokensIssued;

    private int refreshNumber;

    private bool approved;

    private bool refused;

    #endregion

    #region Properties

    public string VerificationUri { get; set; } = "https://github.com/login/device";

    public string? TokenError { get; set; }

    public int? TokenInterval { get; set; }

    public SignInServerAnswer RefreshAnswer { get; set; } = SignInServerAnswer.Renewed;

    public string? EndedAnswer { get; set; }

    public CopilotPlanOptions Settings { get; } = new()
    {
        ClientId = TestClientId,
        ClientSecret = TestClientSecret,
    };

    public List<SentRequest> Requests { get; } = [];

    public List<SignInTokenRequest> TokenRequests { get; } = [];

    #endregion

    #region Public Methods

    public void Approve()
    {
        approved = true;
        refused = false;
        TokenError = null;
    }

    public void Refuse()
    {
        refused = true;
        approved = false;
        TokenError = "access_denied";
    }

    public ModelSignInReturn BrowserApproval(string state) =>
        new($"code=github-code&state={Uri.EscapeDataString(state)}");

    public static ModelSignInReturn BrowserRefusal(string state) =>
        new($"error=access_denied&state={Uri.EscapeDataString(state)}");

    public CopilotPlanSignIn SignIn(CopilotPlanOptions? options = null) =>
        new(
            new FixtureHttpClientFactory(new ScriptedHandler(Respond)),
            new SettableOptions<CopilotPlanOptions>(options ?? Settings),
            TimeProvider.System);

    #endregion

    #region Private Methods

    private async Task<HttpResponseMessage> Respond(HttpRequestMessage request, int _, CancellationToken cancellation)
    {
        Requests.Add(await Capture(request));
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellation);
        var form = Parse(body);

        if (path == "/login/device/code")
        {
            return Json(
                $$"""
                {"device_code":"{{DeviceCode}}","user_code":"{{UserCode}}","verification_uri":"{{VerificationUri}}","expires_in":899,"interval":5}
                """);
        }

        if (path == "/login/oauth/access_token")
        {
            return Token(form);
        }

        if (path == "/user")
        {
            return Json("""{"login":"sam"}""");
        }

        if (path.Contains("/grant", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private HttpResponseMessage Token(Dictionary<string, string> form)
    {
        var grant = form.GetValueOrDefault("grant_type");

        if (grant == "refresh_token")
        {
            TokenRequests.Add(new SignInTokenRequest(grant, null, null, form.GetValueOrDefault("refresh_token")));
            return Refresh();
        }

        if (grant == "authorization_code")
        {
            TokenRequests.Add(new SignInTokenRequest(
                grant,
                form.GetValueOrDefault("code"),
                form.GetValueOrDefault("code_verifier"),
                null));
            return Session();
        }

        if (grant == "urn:ietf:params:oauth:grant-type:device_code")
        {
            if (refused || TokenError is "access_denied" or "expired_token")
            {
                return Json($$"""{"error":"{{TokenError ?? "access_denied"}}"}""");
            }

            if (!approved || TokenError is not null)
            {
                var interval = TokenInterval is { } seconds ? $""","interval":{seconds}""" : string.Empty;
                return Json($$"""{"error":"{{TokenError ?? "authorization_pending"}}"{{interval}}}""");
            }

            TokenRequests.Add(new SignInTokenRequest(grant, form.GetValueOrDefault("device_code"), null, null));
            return Session();
        }

        return Json("""{"error":"unsupported_grant_type"}""");
    }

    private HttpResponseMessage Refresh()
    {
        refreshNumber++;
        return RefreshAnswer switch
        {
            SignInServerAnswer.Ended when EndedAnswer is "401" => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            SignInServerAnswer.Ended when EndedAnswer is "403" => new HttpResponseMessage(HttpStatusCode.Forbidden),
            SignInServerAnswer.Ended => Json($$"""{"error":"{{EndedAnswer ?? "invalid_grant"}}"}""", HttpStatusCode.BadRequest),
            SignInServerAnswer.ServerError => new HttpResponseMessage(HttpStatusCode.BadGateway),
            SignInServerAnswer.Unreachable => throw new HttpRequestException("unreachable"),
            _ => Session(),
        };
    }

    private HttpResponseMessage Session()
    {
        tokensIssued++;
        return Json(
            $$"""
            {"access_token":"gho_access-{{tokensIssued}}","token_type":"bearer","scope":"read:user","refresh_token":"ghr_refresh-{{tokensIssued}}","expires_in":28800}
            """);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        ScriptedHandler.Json(status, body);

    private static Dictionary<string, string> Parse(string body)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            values[Uri.UnescapeDataString(parts[0].Replace('+', ' '))] =
                parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty;
        }

        return values;
    }

    private static async Task<SentRequest> Capture(HttpRequestMessage request)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync();
        var headers = request.Headers.ToDictionary(
            header => header.Key,
            header => string.Join(',', header.Value),
            StringComparer.OrdinalIgnoreCase);
        return new SentRequest(request.Method, request.RequestUri!, headers, body);
    }

    #endregion
}
