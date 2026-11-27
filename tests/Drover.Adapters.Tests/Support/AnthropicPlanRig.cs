namespace Drover.Adapters.Tests.Support;

/// <summary>Settings a test can change between two requests, as a configuration reload would.</summary>
public sealed class SettableOptions<T>(T value) : IOptionsMonitor<T>
{
    #region Properties

    public T CurrentValue { get; set; } = value;

    #endregion

    #region Interface Implementations

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;

    #endregion
}


/// <summary>
/// The Claude plan's token address, played in memory as its working client shows it: JSON bodies, a one-time code
/// exchanged once, and refresh tokens that rotate and are refused once spent. Every token it makes is random, so a test
/// can look for it where it must never appear. It records every request it is sent.
/// </summary>
public sealed class AnthropicTokenServer : HttpMessageHandler
{
    #region Private Fields

    private readonly ConcurrentDictionary<string, bool> codes = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, bool> refreshTokens = new(StringComparer.Ordinal);

    private readonly ConcurrentQueue<SentRequest> sent = new();

    #endregion

    #region Properties

    /// <summary>How the next refreshes are answered.</summary>
    public SignInServerAnswer RefreshAnswer { get; set; } = SignInServerAnswer.Renewed;

    /// <summary>The status of an ended answer.</summary>
    public HttpStatusCode EndedStatus { get; set; } = HttpStatusCode.BadRequest;

    /// <summary>The body of an ended answer, for the error code given.</summary>
    public Func<string, string> EndedBody { get; set; } = code => JsonSerializer.Serialize(new { error = code, error_description = "The grant is over." });

    public string EndedCode { get; set; } = "invalid_grant";

    /// <summary>The status the exchange answers with, for a test of a refused or failed exchange.</summary>
    public HttpStatusCode? ExchangeStatus { get; set; }

    /// <summary>When false, a refresh answer leaves the refresh token out.</summary>
    public bool RotatesRefreshTokens { get; set; } = true;

    public IReadOnlyList<SentRequest> Sent => [.. sent];

    public IReadOnlyList<SignInTokenRequest> Received =>
    [
        .. Sent.Select(request => request.Json).Select(body => new SignInTokenRequest(
            body.GetProperty("grant_type").GetString()!,
            Optional(body, "code"),
            Optional(body, "code_verifier"),
            Optional(body, "refresh_token"))),
    ];

    #endregion

    #region Public Methods

    /// <summary>A one-time code the provider's page hands out after the person approves.</summary>
    public string IssueCode()
    {
        var code = $"code-{Guid.NewGuid():N}";
        codes[code] = true;
        return code;
    }

    #endregion

    #region Interface Implementations

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(header => header.Key, header => string.Join(',', header.Value));
        sent.Enqueue(new SentRequest(request.Method, request.RequestUri!, headers, body));
        var json = JsonElement.Parse(body);

        return json.GetProperty("grant_type").GetString() == "refresh_token" ? Refresh(json) : Exchange(json);
    }

    #endregion

    #region Private Methods

    private HttpResponseMessage Exchange(JsonElement json)
    {
        if (ExchangeStatus is { } status)
        {
            return ScriptedHandler.Json(status, """{"error":"invalid_request"}""");
        }

        return Optional(json, "code") is { } code && codes.TryRemove(code, out _)
            ? Tokens(issueRefresh: true)
            : ScriptedHandler.Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
    }

    private HttpResponseMessage Refresh(JsonElement json) =>
        RefreshAnswer switch
        {
            SignInServerAnswer.Ended => ScriptedHandler.Json(EndedStatus, EndedBody(EndedCode)),
            SignInServerAnswer.ServerError => ScriptedHandler.Json(HttpStatusCode.ServiceUnavailable, """{"error":"temporarily_unavailable"}"""),
            SignInServerAnswer.Unreachable => throw new HttpRequestException("The test network is down."),
            _ when Optional(json, "refresh_token") is { } token && refreshTokens.TryRemove(token, out _) => Tokens(RotatesRefreshTokens),
            _ => ScriptedHandler.Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}"""),
        };

    private HttpResponseMessage Tokens(bool issueRefresh)
    {
        var refresh = $"refresh-{Guid.NewGuid():N}";
        Dictionary<string, object> answer = new()
        {
            ["token_type"] = "Bearer",
            ["access_token"] = $"access-{Guid.NewGuid():N}",
            ["expires_in"] = 28800,
            ["scope"] = "user:inference user:profile",
        };

        if (issueRefresh)
        {
            refreshTokens[refresh] = true;
            answer["refresh_token"] = refresh;
        }

        return ScriptedHandler.Json(HttpStatusCode.OK, JsonSerializer.Serialize(answer));
    }

    private static string? Optional(JsonElement body, string name) =>
        body.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;

    #endregion
}


/// <summary>A Claude plan sign-in aimed at <see cref="AnthropicTokenServer"/>, and the settings it reads.</summary>
public sealed record AnthropicPlanSignInRig(AnthropicPlanSignIn SignIn, AnthropicTokenServer Server, SettableOptions<AnthropicPlanSignInOptions> Options)
{
    #region Public Methods

    public static AnthropicPlanSignInRig Start(TimeProvider? time = null)
    {
        var server = new AnthropicTokenServer();
        var options = new SettableOptions<AnthropicPlanSignInOptions>(new AnthropicPlanSignInOptions());
        var signIn = new AnthropicPlanSignIn(new FixtureHttpClientFactory(server), options, time ?? TimeProvider.System);

        return new AnthropicPlanSignInRig(signIn, server, options);
    }

    /// <summary>What the desktop head posts after the person approves: the query string of the return.</summary>
    public ModelSignInReturn Approval(string state) => new($"code={Server.IssueCode()}&state={Uri.EscapeDataString(state)}");

    /// <summary>What the provider's code page shows the person to copy after approving.</summary>
    public ModelSignInReturn Pasted(string state) => new($"{Server.IssueCode()}#{state}");

    public static ModelSignInReturn Refusal(string state) => new($"error=access_denied&error_description=denied&state={Uri.EscapeDataString(state)}");

    #endregion
}


/// <summary>The Claude plan provider against a recorded Anthropic fixture, for the shared model provider suite.</summary>
public static class AnthropicPlanRig
{
    #region Public Methods

    public static Rig Start(string fixtureName)
    {
        var handler = new RecordedHandler(RecordedFixture.Load("anthropic", fixtureName));
        var provider = new AnthropicPlanProvider(new FixtureHttpClientFactory(handler), new SettableOptions<AnthropicPlanSignInOptions>(new AnthropicPlanSignInOptions()));
        var connection = new ModelEndpoint(AnthropicPlanSignIn.ConnectionKind, AnthropicProvider.BaseAddress);
        var subject = new ProviderUnderTest(provider, connection, TestKeys.Generate(), AnthropicRig.ModelId, handler.Wire) { Wire = AnthropicWire.Instance };

        return new Rig(subject, handler);
    }

    #endregion
}
