namespace Drover.Adapters.Tests.Support;

/// <summary>What is wrong with the ID token the fake server hands out, when a test wants something wrong with it.</summary>
public enum IdTokenFlaw
{
    None,
    SignedByAnotherKey,
    WrongNonce,
    WrongAudience,
    WrongIssuer,
    Expired,
    Missing,
}


/// <summary>
/// The provider's authorization server for a ChatGPT plan sign-in, played in memory as an HTTP handler, from the
/// shapes on https://developers.openai.com/siwc/llms-full.txt ("Registration and sign-in", "Accounts and sessions",
/// "Token reference", "Errors and recovery") and the discovery document, read 2026-11-27. It issues a client id on a
/// first registration, checks PKCE, the client id, the return address and the resource on the exchange, signs ID
/// tokens with a key it publishes at its keys address, rotates refresh tokens and refuses a spent one, and keeps
/// every request. Every token and key it makes is random, made here; none is real.
/// </summary>
public sealed class OpenAIPlanAuthServer : HttpMessageHandler
{
    #region Private Fields

    public const string IssuedClientId = "oaiapp_test_issued";

    public const string Subject = "user-test-subject";

    public const string Email = "sam@example.test";

    private readonly RSA signingKey = RSA.Create(2048);

    private readonly RSA otherKey = RSA.Create(2048);

    private readonly ConcurrentDictionary<string, PendingApproval> approvals = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, PendingApproval> codes = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, bool> refreshTokens = new(StringComparer.Ordinal);

    private readonly ConcurrentQueue<SentForm> sent = new();

    #endregion

    #region Properties

    public OpenAIPlanOptions Settings { get; } = new();

    /// <summary>How the next refreshes are answered.</summary>
    public SignInServerAnswer RefreshAnswer { get; set; } = SignInServerAnswer.Renewed;

    /// <summary>The "sign-in is over" code an ended refresh answers with.</summary>
    public string EndedCode { get; set; } = "invalid_grant";

    /// <summary>What the token answer grants. The default is everything the sign-in asks for.</summary>
    public string GrantedScope { get; set; } = "chatgpt.tokens.use.direct email offline_access openid profile resource.invoke";

    public IdTokenFlaw Flaw { get; set; } = IdTokenFlaw.None;

    /// <summary>The account's <c>sub</c> in the ID token.</summary>
    public string TokenSubject { get; set; } = Subject;

    /// <summary>The time the server's tokens are made at.</summary>
    public TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>Every form the server received, in order, at any address.</summary>
    public IReadOnlyList<SentForm> Sent => [.. sent];

    /// <summary>The requests to the token address, in the suite's shape.</summary>
    public IReadOnlyList<SignInTokenRequest> TokenRequests =>
        [.. Sent.Where(form => form.Address == Settings.TokenAddress)
            .Select(form => new SignInTokenRequest(form.Field("grant_type") ?? string.Empty, form.Field("code"), form.Field("code_verifier"), form.Field("refresh_token")))];

    #endregion

    #region Public Methods

    /// <summary>The server's half of the person approving on the page the sign-in opened: records what the page was asked.</summary>
    public void Saw(Uri authorizeAddress)
    {
        var query = Parse(authorizeAddress.Query);
        approvals[query["state"]] = new PendingApproval(query["code_challenge"], query["nonce"], query["client_id"], query["redirect_uri"]);
    }

    /// <summary>
    /// The query string the person's device receives after approving the attempt with this state. A first registration
    /// comes back with the issued client id, unless <paramref name="clientId"/> names another (or empty for none).
    /// </summary>
    public ModelSignInReturn Approve(string state, string? clientId = null)
    {
        var approval = approvals.TryGetValue(state, out var seen) ? seen : new PendingApproval(string.Empty, string.Empty, IssuedClientId, string.Empty);
        var code = Random();
        codes[code] = approval;
        var returned = clientId ?? (approval.ClientId == Settings.RegistrationClientId ? IssuedClientId : null);
        var query = $"code={code}&scope={Uri.EscapeDataString(GrantedScope)}&state={Uri.EscapeDataString(state)}";
        return new ModelSignInReturn(returned is { Length: > 0 } ? $"{query}&client_id={returned}" : query);
    }

    public static ModelSignInReturn Refuse(string state) => new($"error=access_denied&state={Uri.EscapeDataString(state)}");

    #endregion

    #region Interface Implementations

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var form = new SentForm(request.Method, request.RequestUri!, Parse(body));
        sent.Enqueue(form);

        if (request.Method == HttpMethod.Get && request.RequestUri == Settings.KeysAddress)
        {
            return Json(HttpStatusCode.OK, Keys(signingKey));
        }

        if (request.RequestUri == Settings.RevokeAddress)
        {
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        if (request.RequestUri != Settings.TokenAddress)
        {
            return Json(HttpStatusCode.NotFound, "{}");
        }

        return form.Field("grant_type") switch
        {
            "authorization_code" => Exchange(form),
            "refresh_token" => Refresh(form),
            _ => Json(HttpStatusCode.BadRequest, """{"error":"unsupported_grant_type"}"""),
        };
    }

    #endregion

    #region Answers

    private HttpResponseMessage Exchange(SentForm form)
    {
        if (form.Field("code") is not { } code || !codes.TryRemove(code, out var approval)
            || Challenge(form.Field("code_verifier") ?? string.Empty) != approval.Challenge
            || form.Field("redirect_uri") != approval.RedirectUri
            || form.Field("resource") != Settings.Resource
            || form.Field("client_id") is not { } clientId || clientId == Settings.RegistrationClientId)
        {
            return Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
        }

        return Tokens(clientId, approval.Nonce);
    }

    private HttpResponseMessage Refresh(SentForm form)
    {
        switch (RefreshAnswer)
        {
            case SignInServerAnswer.ServerError:
                return Json(HttpStatusCode.InternalServerError, "{}");
            case SignInServerAnswer.Unreachable:
                throw new HttpRequestException(HttpRequestError.ConnectionError, "unreachable");
            case SignInServerAnswer.Ended:
                return Json(HttpStatusCode.BadRequest, EndedCode == "invalid_grant"
                    ? """{"error":"invalid_grant"}"""
                    : new JsonObject { ["error"] = new JsonObject { ["code"] = EndedCode, ["message"] = "Made-up message for a test." } }.ToJsonString());
        }

        if (form.Field("refresh_token") is not { } token || !refreshTokens.TryUpdate(token, true, false))
        {
            return Json(HttpStatusCode.BadRequest, """{"error":{"code":"refresh_token_reused","message":"Made-up message for a test."}}""");
        }

        return Tokens(form.Field("client_id") ?? string.Empty, nonce: null);
    }

    private HttpResponseMessage Tokens(string clientId, string? nonce)
    {
        var refresh = Random();
        refreshTokens[refresh] = false;
        var idToken = nonce is null || Flaw is IdTokenFlaw.Missing ? null : IdToken(clientId, nonce);
        var answer = new JsonObject
        {
            ["access_token"] = Random(),
            ["refresh_token"] = refresh,
            ["token_type"] = "Bearer",
            ["expires_in"] = 3600,
            ["scope"] = GrantedScope,
            ["earliest_refresh_at"] = Clock.GetUtcNow().AddMinutes(30).ToUnixTimeSeconds(),
        };

        if (idToken is not null)
        {
            answer["id_token"] = idToken;
        }

        return Json(HttpStatusCode.OK, answer.ToJsonString());
    }

    private string IdToken(string clientId, string nonce)
    {
        var now = Clock.GetUtcNow().UtcDateTime;
        var expired = Flaw is IdTokenFlaw.Expired;
        var key = new RsaSecurityKey(Flaw is IdTokenFlaw.SignedByAnotherKey ? otherKey : signingKey) { KeyId = "test-key" };

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Flaw is IdTokenFlaw.WrongIssuer ? "https://issuer.example.test" : Settings.Issuer,
            Audience = Flaw is IdTokenFlaw.WrongAudience ? "oaiapp_someone_else" : clientId,
            IssuedAt = expired ? now.AddHours(-3) : now,
            NotBefore = expired ? now.AddHours(-3) : now,
            Expires = expired ? now.AddHours(-2) : now.AddHours(1),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = TokenSubject,
                ["email"] = Email,
                ["nonce"] = Flaw is IdTokenFlaw.WrongNonce ? Random() : nonce,
            },
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
        });
    }

    #endregion

    #region Private Methods

    private static string Keys(RSA key)
    {
        var parameters = key.ExportParameters(false);
        return new JsonObject
        {
            ["keys"] = new JsonArray(new JsonObject
            {
                ["kty"] = "RSA",
                ["kid"] = "test-key",
                ["use"] = "sig",
                ["alg"] = "RS256",
                ["n"] = Base64Url.EncodeToString(parameters.Modulus),
                ["e"] = Base64Url.EncodeToString(parameters.Exponent),
            }),
        }.ToJsonString();
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string Random() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(24));

    private static string Challenge(string verifier) => Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    internal static Dictionary<string, string> Parse(string text) =>
        text.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')),
                pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : string.Empty);

    #endregion

    #region Nested Types

    private sealed record PendingApproval(string Challenge, string Nonce, string ClientId, string RedirectUri);

    #endregion
}


/// <summary>One request the fake server received: where it went and its form fields.</summary>
public sealed record SentForm(HttpMethod Method, Uri Address, IReadOnlyDictionary<string, string> Fields)
{
    public string? Field(string name) => Fields.TryGetValue(name, out var value) ? value : null;
}


/// <summary>The sign-in with the fake server as its network, and a hook that shows the server each page the sign-in opens.</summary>
public sealed class ObservedPlanSignIn(OpenAIPlanSignIn signIn, OpenAIPlanAuthServer server) : IModelSignIn
{
    #region Properties

    public OpenAIPlanSignIn Inner => signIn;

    public string Kind => signIn.Kind;

    public string DisplayName => signIn.DisplayName;

    public bool IsPersonalPlan => signIn.IsPersonalPlan;

    #endregion

    #region Interface Implementations

    public bool AcceptsRedirect(Uri? redirectUri) => signIn.AcceptsRedirect(redirectUri);

    public async Task<ModelSignInStep> Begin(ModelSignInAttempt attempt, CancellationToken cancellation)
    {
        var step = await signIn.Begin(attempt, cancellation);
        server.Saw(step.Address);
        return step;
    }

    public Task<ModelTokens?> Finish(ModelSignInAttempt attempt, ModelSignInReturn arrived, CancellationToken cancellation) =>
        signIn.Finish(attempt, arrived, cancellation);

    public Task<ModelTokens> Refresh(ModelTokens current, CancellationToken cancellation) => signIn.Refresh(current, cancellation);

    public Task Revoke(ModelTokens current, CancellationToken cancellation) => signIn.Revoke(current, cancellation);

    #endregion

    #region Public Methods

    /// <summary>A sign-in on <paramref name="server"/>'s settings, over it as the network.</summary>
    public static ObservedPlanSignIn Over(OpenAIPlanAuthServer server, TimeProvider? time = null, IOptionsMonitor<OpenAIPlanOptions>? settings = null) =>
        new(new OpenAIPlanSignIn(new FixtureHttpClientFactory(server), settings ?? new SettableOptions<OpenAIPlanOptions>(server.Settings), time ?? TimeProvider.System, NullLogger<OpenAIPlanSignIn>.Instance), server);

    #endregion
}
