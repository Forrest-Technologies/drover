namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// Signs a person in to OpenRouter with their own account, on the provider's documented flow: authorization code with
/// PKCE, no registration, no client id. The exchange answers an API key that belongs to the person. It has no refresh
/// token and no end, so <see cref="Refresh"/> hands back what it was given and the hourly renewal, which only looks at
/// sign-ins with an end, never takes it up. The provider offers the flow to any app, so it is not a personal plan.
/// See <see cref="OpenRouterSignInOptions"/> for the sources.
/// </summary>
public sealed class OpenRouterSignIn(IHttpClientFactory httpClients, IOptionsMonitor<OpenRouterSignInOptions> options) : IModelSignIn
{
    #region Private Fields

    public const string ConnectionKind = "openrouter_signin";

    public const string HttpClientName = "drover-openrouter-signin";

    private const int MaxAnswerBytes = 64 * 1024;

    #endregion

    #region Properties

    public string Kind => ConnectionKind;

    public string DisplayName => "OpenRouter";

    public bool IsPersonalPlan => false;

    /// <summary>An <c>https</c> address of this server is a callback the provider accepts, so a head that cannot listen can use it.</summary>
    public bool CanReturnToServer => true;

    #endregion

    #region Interface Implementations

    public bool AcceptsRedirect(Uri? redirectUri)
    {
        var configured = options.CurrentValue.RedirectUri;

        return redirectUri is { IsAbsoluteUri: true } candidate
            && candidate.Scheme == configured.Scheme
            && candidate.AbsolutePath == configured.AbsolutePath
            && candidate.Query.Length == 0
            && candidate.Fragment.Length == 0
            && candidate.UserInfo.Length == 0
            && (string.Equals(candidate.Host, configured.Host, StringComparison.OrdinalIgnoreCase) || (IsLoopback(candidate) && IsLoopback(configured)));
    }

    public Task<ModelSignInStep> Begin(ModelSignInAttempt attempt, CancellationToken cancellation)
    {
        if (attempt.RedirectUri is not { } redirect)
        {
            // The server never starts this kind without a return address: it refuses one from a device that cannot
            // listen unless it has its own https address to give.
            throw new InvalidOperationException("An OpenRouter sign-in needs a return address.");
        }

        var settings = options.CurrentValue;
        var callback = IsLoopback(redirect) && redirect.Scheme == "http" ? new UriBuilder(redirect) { Host = "localhost" }.Uri : redirect;
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(attempt.Verifier)));
        var query = string.Join(
            '&',
            $"callback_url={Uri.EscapeDataString(callback.AbsoluteUri)}",
            $"code_challenge={challenge}",
            "code_challenge_method=S256",
            $"state={Uri.EscapeDataString(attempt.State)}",
            $"key_label={Uri.EscapeDataString(settings.KeyLabel)}",
            "expiration=never");
        var address = new UriBuilder(settings.AuthorizeAddress) { Query = query }.Uri;

        return Task.FromResult(new ModelSignInStep(address, null, ModelSignInWait.Return));
    }

    public async Task<ModelTokens?> Finish(ModelSignInAttempt attempt, ModelSignInReturn arrived, CancellationToken cancellation)
    {
        var answer = ReadQuery(arrived.Text);

        if (answer.ContainsKey("error")
            || !answer.TryGetValue("state", out var state)
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(attempt.State))
            || !answer.TryGetValue("code", out var code)
            || code.Length == 0)
        {
            throw new ModelSignInRefusedException();
        }

        return new ModelTokens { AccessToken = await Exchange(code, attempt.Verifier, cancellation) };
    }

    /// <summary>A key has no refresh and no end: the current set is the answer.</summary>
    public Task<ModelTokens> Refresh(ModelTokens current, CancellationToken cancellation) => Task.FromResult(current);

    /// <summary>The provider has no call a key can make to end itself; signing out deletes Drover's copy.</summary>
    public Task Revoke(ModelTokens current, CancellationToken cancellation) => Task.CompletedTask;

    #endregion

    #region Private Methods

    /// <summary>Trades the one-time code and the verifier for the person's key. No message here holds the key or the code.</summary>
    private async Task<string> Exchange(string code, string verifier, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, options.CurrentValue.TokenAddress)
        {
            Content = JsonContent.Create(new Dictionary<string, string>
            {
                ["code"] = code,
                ["code_verifier"] = verifier,
                ["code_challenge_method"] = "S256",
            }),
        };

        try
        {
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);

            if (!response.IsSuccessStatusCode)
            {
                // A code the provider will not exchange is a refusal; a provider that is down or busy is not.
                throw (int)response.StatusCode is >= 400 and < 500 and not 408 and not 429
                    ? new ModelSignInRefusedException()
                    : ModelProviderException.FromStatus((int)response.StatusCode);
            }

            using var document = await BoundedResponse.ReadJson(response.Content, MaxAnswerBytes, "sign-in answer", cancellation);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("key", out var key)
                && key.ValueKind == JsonValueKind.String
                && key.GetString() is { Length: > 0 } value
                    ? value
                    : throw ModelProviderException.FromMalformedResponse();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw ModelProviderException.FromMalformedResponse();
        }
        catch (Exception exception) when (ProtocolFailureMapping.TryMap(exception, out var mapped))
        {
            throw mapped;
        }
    }

    private static bool IsLoopback(Uri address) =>
        address.IsLoopback || string.Equals(address.Host, "localhost", StringComparison.OrdinalIgnoreCase);

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static Dictionary<string, string> ReadQuery(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in text.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            values[Uri.UnescapeDataString(parts[0].Replace('+', ' '))] = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty;
        }

        return values;
    }

    #endregion
}
