namespace Drover.Adapters.Tests.Support;

/// <summary>Options that can be changed while the sign-in runs, as a reloaded configuration is.</summary>
public sealed class ChangingOptions<T>(T value) : IOptionsMonitor<T>
    where T : class
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
/// OpenRouter's sign-in page and key exchange, played in memory from what its documentation says
/// (https://openrouter.ai/docs/use-cases/oauth-pkce): the exchange takes a one-time code with the PKCE verifier and
/// answers a key; a code is refused when the verifier does not match the challenge the page was opened with, or when
/// it was already used. Every key it makes is random, so a test can look for it where it must never appear.
/// </summary>
public sealed class FakeOpenRouter
{
    #region Private Fields

    private readonly ConcurrentDictionary<string, string> challengesByState = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, string> challengesByCode = new(StringComparer.Ordinal);

    private readonly ConcurrentQueue<SentRequest> requests = new();

    private readonly ConcurrentQueue<string> issued = new();

    #endregion

    #region Properties

    /// <summary>What the exchange answers; a status other than 200 is the provider refusing or failing.</summary>
    public HttpStatusCode ExchangeStatus { get; set; } = HttpStatusCode.OK;

    /// <summary>When set, the exchange answers this body whatever it was asked.</summary>
    public string? ExchangeBody { get; set; }

    public bool Unreachable { get; set; }

    public IReadOnlyList<SentRequest> Requests => [.. requests];

    public IReadOnlyList<string> Issued => [.. issued];

    #endregion

    #region Public Methods

    /// <summary>The sign-in's address for an attempt, read so the page knows the challenge the code must be proved against.</summary>
    public void Opened(Uri address)
    {
        var query = Read(address.Query);
        challengesByState[query["state"]] = query["code_challenge"];
    }

    /// <summary>What the person's browser brings back to the callback once they approve for the state.</summary>
    public string Approve(string state)
    {
        var code = $"code-{Guid.NewGuid():N}";
        challengesByCode[code] = challengesByState.GetValueOrDefault(state) ?? string.Empty;
        return $"code={code}&state={Uri.EscapeDataString(state)}";
    }

    public async Task<HttpResponseMessage> Answer(HttpRequestMessage request, CancellationToken cancellation)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellation);
        requests.Enqueue(new SentRequest(request.Method, request.RequestUri!, new Dictionary<string, string>(), body));

        if (Unreachable)
        {
            throw new HttpRequestException("The network is down.");
        }

        if (ExchangeBody is { } fixedBody)
        {
            return ScriptedHandler.Json(ExchangeStatus, fixedBody);
        }

        var json = JsonElement.Parse(body);
        var code = json.GetProperty("code").GetString()!;
        var verifier = json.GetProperty("code_verifier").GetString()!;
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        if (ExchangeStatus != HttpStatusCode.OK || !challengesByCode.TryRemove(code, out var expected) || expected != challenge)
        {
            return ScriptedHandler.Json(ExchangeStatus == HttpStatusCode.OK ? HttpStatusCode.Forbidden : ExchangeStatus, """{"error":{"message":"refused"}}""");
        }

        var key = $"sk-or-v1-{Guid.NewGuid():N}";
        issued.Enqueue(key);
        return ScriptedHandler.Json(HttpStatusCode.OK, $$"""{"key":"{{key}}","user_id":"user_{{Guid.NewGuid():N}}"}""");
    }

    public static Dictionary<string, string> Read(string query) =>
        query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty);

    #endregion
}


/// <summary>An OpenRouter sign-in aimed at <see cref="FakeOpenRouter"/>, which learns each page's challenge as it is opened.</summary>
public sealed class OpenRouterSignInRig
{
    #region Constructors

    public OpenRouterSignInRig(OpenRouterSignInOptions? settings = null)
    {
        Options = new ChangingOptions<OpenRouterSignInOptions>(settings ?? new OpenRouterSignInOptions());
        var handler = new ScriptedHandler((request, _, cancellation) => Provider.Answer(request, cancellation));
        SignIn = new Opening(new OpenRouterSignIn(new FixtureHttpClientFactory(handler), Options), Provider);
    }

    #endregion

    #region Properties

    public FakeOpenRouter Provider { get; } = new();

    public ChangingOptions<OpenRouterSignInOptions> Options { get; }

    public IModelSignIn SignIn { get; }

    #endregion

    #region Nested Types

    private sealed class Opening(IModelSignIn inner, FakeOpenRouter provider) : IModelSignIn
    {
        #region Interface Implementations

        public string Kind => inner.Kind;

        public string DisplayName => inner.DisplayName;

        public bool IsPersonalPlan => inner.IsPersonalPlan;

        public bool CanReturnToServer => inner.CanReturnToServer;

        public bool AcceptsRedirect(Uri? redirectUri) => inner.AcceptsRedirect(redirectUri);

        public async Task<ModelSignInStep> Begin(ModelSignInAttempt attempt, CancellationToken cancellation)
        {
            var step = await inner.Begin(attempt, cancellation);
            provider.Opened(step.Address);
            return step;
        }

        public Task<ModelTokens?> Finish(ModelSignInAttempt attempt, ModelSignInReturn arrived, CancellationToken cancellation) =>
            inner.Finish(attempt, arrived, cancellation);

        public Task<ModelTokens> Refresh(ModelTokens current, CancellationToken cancellation) => inner.Refresh(current, cancellation);

        public Task Revoke(ModelTokens current, CancellationToken cancellation) => inner.Revoke(current, cancellation);

        #endregion
    }

    #endregion
}
