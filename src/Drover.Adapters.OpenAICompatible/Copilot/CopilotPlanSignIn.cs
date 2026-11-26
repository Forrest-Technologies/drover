namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// Signs a person in to their own Copilot plan with GitHub's device-code flow. The GitHub token is the refresh
/// token; exchanging it at GitHub's Copilot token endpoint is the refresh. Revoking is a no-op: GitHub has no
/// revoke for this client.
/// </summary>
public sealed class CopilotPlanSignIn(
    IHttpClientFactory httpClients,
    IOptions<CopilotPlanOptions> options,
    TimeProvider time,
    ILogger<CopilotPlanSignIn> logger) : IModelSignIn
{
    #region Private Fields

    private const string DeviceGrant = "urn:ietf:params:oauth:grant-type:device_code";

    private readonly ConcurrentDictionary<Guid, PendingDevice> pending = new();

    #endregion

    #region Properties

    public string Kind => ModelConnectionKinds.CopilotSignIn;

    public string DisplayName => "GitHub Copilot";

    public bool IsPersonalPlan => true;

    #endregion

    #region Interface Implementations

    public bool AcceptsRedirect(Uri? redirectUri) => true;

    public TimeSpan PollWait(ModelSignInAttempt attempt) =>
        pending.TryGetValue(attempt.Id, out var device)
            ? device.Interval + (device.Slowed ? TimeSpan.FromSeconds(5) : TimeSpan.Zero)
            : TimeSpan.FromSeconds(5);

    public async Task<ModelSignInStep> Begin(ModelSignInAttempt attempt, CancellationToken cancellation)
    {
        var started = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, started.DeviceCodeAddress)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = started.ClientId,
                ["scope"] = started.Scope,
            }),
        };
        request.Headers.Accept.ParseAdd("application/json");
        CopilotHeaders.Apply(request, started);

        using var response = await Auth().SendAsync(request, cancellation);
        using var document = await ReadJson(response, cancellation);
        var root = document.RootElement;
        var deviceCode = Text(root, "device_code");
        var userCode = Text(root, "user_code");
        var verification = Text(root, "verification_uri");
        var expiresIn = Number(root, "expires_in");
        var interval = root.TryGetProperty("interval", out var intervalElement) && intervalElement.TryGetInt32(out var seconds)
            ? TimeSpan.FromSeconds(Math.Max(seconds, 1))
            : TimeSpan.FromSeconds(5);

        if (deviceCode is null || userCode is null || verification is null || expiresIn is null)
        {
            throw ModelProviderException.FromMalformedResponse();
        }

        if (!Uri.TryCreate(verification, UriKind.Absolute, out var address)
            || address.Scheme is not ("https" or "http"))
        {
            throw ModelProviderException.FromRefusal("The provider returned a verification address that is not a web address.");
        }

        var lifetime = TimeSpan.FromSeconds(expiresIn.Value);
        pending[attempt.Id] = new PendingDevice(deviceCode, interval, time.GetUtcNow() + lifetime);
        return new ModelSignInStep(address, userCode, ModelSignInWait.Poll) { Interval = interval, Lifetime = lifetime };
    }

    public async Task<ModelTokens?> Finish(ModelSignInAttempt attempt, ModelSignInReturn arrived, CancellationToken cancellation)
    {
        if (!pending.TryGetValue(attempt.Id, out var device))
        {
            throw new ModelSignInRefusedException();
        }

        if (time.GetUtcNow() >= device.ExpiresAt)
        {
            pending.TryRemove(attempt.Id, out _);
            throw new ModelSignInRefusedException();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.TokenAddress)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = options.Value.ClientId,
                ["device_code"] = device.DeviceCode,
                ["grant_type"] = DeviceGrant,
            }),
        };
        request.Headers.Accept.ParseAdd("application/json");
        CopilotHeaders.Apply(request, options.Value);

        using var response = await Auth().SendAsync(request, cancellation);
        using var document = await ReadJson(response, cancellation);
        var root = document.RootElement;

        if (root.TryGetProperty("access_token", out var tokenElement) && tokenElement.GetString() is { Length: > 0 } githubToken)
        {
            pending.TryRemove(attempt.Id, out _);
            return await Exchange(githubToken, cancellation);
        }

        var error = Text(root, "error");

        if (error is "authorization_pending")
        {
            return null;
        }

        if (error is "slow_down")
        {
            var interval = root.TryGetProperty("interval", out var intervalElement) && intervalElement.TryGetInt32(out var seconds)
                ? TimeSpan.FromSeconds(Math.Max(seconds, 1))
                : device.Interval + TimeSpan.FromSeconds(5);
            pending[attempt.Id] = device with { Interval = interval, Slowed = true };
            return null;
        }

        pending.TryRemove(attempt.Id, out _);
        throw new ModelSignInRefusedException();
    }

    public async Task<ModelTokens> Refresh(ModelTokens current, CancellationToken cancellation)
    {
        if (current.RefreshToken is not { Length: > 0 } githubToken)
        {
            throw new SignInEndedException();
        }

        try
        {
            return await Exchange(githubToken, cancellation);
        }
        catch (ModelProviderException failure) when (failure.Failure is ModelFailure.InvalidKey)
        {
            throw new SignInEndedException();
        }
    }

    public Task Revoke(ModelTokens current, CancellationToken cancellation) => Task.CompletedTask;

    #endregion

    #region Exchange

    private async Task<ModelTokens> Exchange(string githubToken, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, options.Value.ExchangeAddress);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", githubToken);
        CopilotHeaders.Apply(request, options.Value);

        using var response = await Auth().SendAsync(request, cancellation);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new SignInEndedException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw ModelProviderException.FromStatus((int)response.StatusCode);
        }

        using var document = await ReadJson(response, cancellation);
        var root = document.RootElement;
        var access = Text(root, "token");
        var expiresAt = root.TryGetProperty("expires_at", out var expires) && expires.TryGetInt64(out var unix)
            ? DateTimeOffset.FromUnixTimeSeconds(unix)
            : (DateTimeOffset?)null;

        if (access is null)
        {
            throw ModelProviderException.FromMalformedResponse();
        }

        if (root.TryGetProperty("chat_enabled", out var chat) && chat.ValueKind is JsonValueKind.False)
        {
            throw new SignInEndedException();
        }

        Dictionary<string, string> details = [];
        var api = CopilotApiAddress.FromToken(access)
            ?? NamedApi(root)
            ?? CopilotApiAddress.WithTrailingSlash(options.Value.ApiAddress);
        details["api"] = api.AbsoluteUri;
        var label = await ReadAccountLabel(githubToken, cancellation);

        return new ModelTokens
        {
            AccessToken = access,
            RefreshToken = githubToken,
            ExpiresAt = expiresAt,
            AccountLabel = label,
            Details = details,
        };
    }

    private async Task<string?> ReadAccountLabel(string githubToken, CancellationToken cancellation)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.github.com/user"));
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", githubToken);
            CopilotHeaders.Apply(request, options.Value);
            using var response = await Auth().SendAsync(request, cancellation);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = await ReadJson(response, cancellation);
            return Text(document.RootElement, "login");
        }
        catch (Exception failure) when (failure is HttpRequestException or JsonException or IOException or ModelProviderException)
        {
            logger.LogDebug(failure, "The GitHub account name could not be read after a Copilot sign-in.");
            return null;
        }
    }

    #endregion

    #region HTTP

    private HttpClient Auth() => httpClients.CreateClient(CopilotPlanProvider.AuthHttpClientName);

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response, CancellationToken cancellation)
    {
        var text = await response.Content.ReadAsStringAsync(cancellation);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw ModelProviderException.FromMalformedResponse();
        }

        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            throw ModelProviderException.FromMalformedResponse();
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static int? Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;

    private static Uri? NamedApi(JsonElement root) =>
        root.TryGetProperty("endpoints", out var endpoints)
        && endpoints.ValueKind is JsonValueKind.Object
        && endpoints.TryGetProperty("api", out var named)
        && named.GetString() is { Length: > 0 } address
        && Uri.TryCreate(address, UriKind.Absolute, out var parsed)
            ? CopilotApiAddress.WithTrailingSlash(parsed)
            : null;

    #endregion

    #region Nested Types

    private sealed record PendingDevice(string DeviceCode, TimeSpan Interval, DateTimeOffset ExpiresAt)
    {
        #region Properties

        public bool Slowed { get; init; }

        #endregion

        #region Public Methods

        public override string ToString() => nameof(PendingDevice);

        #endregion
    }

    #endregion
}
