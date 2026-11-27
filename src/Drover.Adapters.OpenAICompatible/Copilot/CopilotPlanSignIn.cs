namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// Signs a person in to their own Copilot plan with the operator's registered GitHub OAuth app. A device that
/// listens gets the browser's return (<see cref="ModelSignInWait.Return"/>) with PKCE; a device that cannot listen
/// uses that same app's device-code flow (<see cref="ModelSignInWait.Poll"/>). The GitHub user token is the credential
/// a model call sends. Refresh uses GitHub's refresh token when one was issued; a token with none is long-lived and
/// is handed back unchanged. See <see cref="CopilotPlanOptions"/> for the sources.
/// </summary>
public sealed class CopilotPlanSignIn(
    IHttpClientFactory httpClients,
    IOptionsMonitor<CopilotPlanOptions> options,
    TimeProvider time) : IModelSignIn
{
    #region Private Fields

    public const string HttpClientName = "drover-copilot-signin";

    private const string DeviceGrant = "urn:ietf:params:oauth:grant-type:device_code";

    private const string DeviceCodeKey = "device_code";

    private const int MaxAnswerBytes = 64 * 1024;

    #endregion

    #region Properties

    public string Kind => ModelConnectionKinds.CopilotSignIn;

    public string DisplayName => "GitHub Copilot";

    public bool IsPersonalPlan => true;

    #endregion

    #region Interface Implementations

    /// <summary>
    /// A loopback address on the configured path and any port, or no address at all: such a device uses the device
    /// code. The address sent to GitHub names the configured host on the device's port.
    /// </summary>
    public bool AcceptsRedirect(Uri? redirectUri)
    {
        if (redirectUri is null)
        {
            return true;
        }

        var configured = options.CurrentValue.RedirectUri;

        return redirectUri.IsAbsoluteUri
            && redirectUri.Scheme == Uri.UriSchemeHttp
            && IsLoopback(redirectUri)
            && IsLoopback(configured)
            && redirectUri.AbsolutePath == configured.AbsolutePath
            && redirectUri.Query.Length == 0
            && redirectUri.Fragment.Length == 0
            && redirectUri.UserInfo.Length == 0;
    }

    public Task<ModelSignInStep> Begin(ModelSignInAttempt attempt, CancellationToken cancellation)
    {
        EnsureClientId();
        return attempt.RedirectUri is null ? BeginDevice(cancellation) : Task.FromResult(BeginReturn(attempt));
    }

    public Task<ModelTokens?> Finish(ModelSignInAttempt attempt, ModelSignInReturn arrived, CancellationToken cancellation) =>
        attempt.RedirectUri is null ? FinishDevice(attempt, cancellation) : FinishReturn(attempt, arrived, cancellation);

    public Task<ModelTokens> Refresh(ModelTokens current, CancellationToken cancellation)
    {
        if (current.RefreshToken is not { Length: > 0 } refreshToken)
        {
            return Task.FromResult(current);
        }

        EnsureClientId();
        var settings = options.CurrentValue;
        var body = TokenBody(settings, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        });

        return Ask(body, current, refreshing: true, cancellation);
    }

    public async Task Revoke(ModelTokens current, CancellationToken cancellation)
    {
        var settings = options.CurrentValue;

        if (settings.ClientId.Length == 0 || settings.ClientSecret.Length == 0 || current.AccessToken.Length == 0)
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(settings.RevokeAddress, $"{settings.ClientId}/grant"))
            {
                Content = JsonContent.Create(new Dictionary<string, string> { ["access_token"] = current.AccessToken }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ClientId}:{settings.ClientSecret}")));
            ApplyGitHubHeaders(request);
            using var response = await Auth().SendAsync(request, cancellation);
        }
        // Signing out deletes Drover's copy either way; GitHub not hearing it is not a reason to keep the tokens.
#pragma warning disable DRV0005
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            return;
        }
#pragma warning restore DRV0005
    }

    #endregion

    #region Starting

    private ModelSignInStep BeginReturn(ModelSignInAttempt attempt)
    {
        var settings = options.CurrentValue;
        var query = string.Join(
            '&',
            $"client_id={Uri.EscapeDataString(settings.ClientId)}",
            "response_type=code",
            $"redirect_uri={Uri.EscapeDataString(RedirectFor(attempt, settings).AbsoluteUri)}",
            $"scope={Uri.EscapeDataString(settings.Scope)}",
            $"code_challenge={Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(attempt.Verifier)))}",
            "code_challenge_method=S256",
            $"state={Uri.EscapeDataString(attempt.State)}");

        return new ModelSignInStep(new UriBuilder(settings.AuthorizeAddress) { Query = query }.Uri, null, ModelSignInWait.Return);
    }

    private async Task<ModelSignInStep> BeginDevice(CancellationToken cancellation)
    {
        var settings = options.CurrentValue;
        EnsureAbsolute(settings.DeviceCodeAddress, nameof(CopilotPlanOptions.DeviceCodeAddress));
        using var request = Form(
            settings.DeviceCodeAddress,
            new Dictionary<string, string>
            {
                ["client_id"] = settings.ClientId,
                ["scope"] = settings.Scope,
            });
        using var response = await Auth().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);

        if (!response.IsSuccessStatusCode)
        {
            throw ModelProviderException.FromStatus((int)response.StatusCode);
        }

        using var document = await ReadAnswer(response, cancellation);
        var root = document.RootElement;
        var deviceCode = Text(root, DeviceCodeKey);
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

        var lifetime = TimeSpan.FromSeconds(Math.Min(expiresIn.Value, (int)TimeSpan.FromMinutes(15).TotalSeconds));
        return new ModelSignInStep(address, userCode, ModelSignInWait.Poll)
        {
            Interval = interval,
            Lifetime = lifetime,
            Kept = new Dictionary<string, string>(StringComparer.Ordinal) { [DeviceCodeKey] = deviceCode },
        };
    }

    #endregion

    #region Finishing

    private async Task<ModelTokens?> FinishReturn(ModelSignInAttempt attempt, ModelSignInReturn arrived, CancellationToken cancellation)
    {
        var answer = Read(arrived.Text);
        var stateMatches = answer.State is { } state
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(attempt.State));

        if (answer.Refused || !stateMatches || answer.Code is not { Length: > 0 } code)
        {
            throw new ModelSignInRefusedException();
        }

        EnsureClientSecret();
        var settings = options.CurrentValue;
        var body = TokenBody(settings, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectFor(attempt, settings).AbsoluteUri,
            ["code_verifier"] = attempt.Verifier,
        });

        return await Ask(body, null, refreshing: false, cancellation);
    }

    private async Task<ModelTokens?> FinishDevice(ModelSignInAttempt attempt, CancellationToken cancellation)
    {
        if (attempt.Begun.TryGetValue(DeviceCodeKey, out var deviceCode) is false || deviceCode.Length == 0)
        {
            throw new ModelSignInRefusedException();
        }

        var settings = options.CurrentValue;
        var body = TokenBody(settings, new Dictionary<string, string>
        {
            ["grant_type"] = DeviceGrant,
            [DeviceCodeKey] = deviceCode,
        });
        EnsureAbsolute(settings.TokenAddress, nameof(CopilotPlanOptions.TokenAddress));
        using var request = Form(settings.TokenAddress, body);
        using var response = await Auth().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        using var document = await ReadAnswer(response, cancellation);
        var root = document.RootElement;

        if (Text(root, "access_token") is { Length: > 0 })
        {
            return await WithAccount(ToTokens(root, null), cancellation);
        }

        var error = Text(root, "error");

        if (error is "authorization_pending")
        {
            return null;
        }

        if (error is "slow_down")
        {
            throw SlowDown(root);
        }

        if (error is "access_denied" or "expired_token")
        {
            throw new ModelSignInRefusedException();
        }

        throw response.IsSuccessStatusCode
            ? ModelProviderException.FromMalformedResponse()
            : ModelProviderException.FromStatus((int)response.StatusCode);
    }

    #endregion

    #region Token Requests

    /// <summary>
    /// Asks the token address and reads the token set. An exchange the provider will not make is a refusal. A refresh
    /// answered 400, 401 or 403 with <c>invalid_grant</c> or <c>bad_refresh_token</c> ends the sign-in; anything else
    /// may pass. No message here holds a token, a code or anything the provider sent.
    /// </summary>
    private async Task<ModelTokens> Ask(Dictionary<string, string> body, ModelTokens? current, bool refreshing, CancellationToken cancellation)
    {
        var address = options.CurrentValue.TokenAddress;
        EnsureAbsolute(address, nameof(CopilotPlanOptions.TokenAddress));
        using var request = Form(address, body);

        try
        {
            using var response = await Auth().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
            var status = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                throw await Failure(response, status, refreshing, cancellation);
            }

            using var document = await ReadAnswer(response, cancellation);
            var tokens = ToTokens(document.RootElement, current);
            return refreshing ? tokens : await WithAccount(tokens, cancellation);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw ModelProviderException.FromMalformedResponse();
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException { InnerException: TimeoutException })
        {
            throw ModelProviderException.FromNetwork();
        }
    }

    private static async Task<Exception> Failure(HttpResponseMessage response, int status, bool refreshing, CancellationToken cancellation)
    {
        if (!refreshing)
        {
            return status is >= 400 and < 500 and not 408 and not 429
                ? new ModelSignInRefusedException()
                : ModelProviderException.FromStatus(status);
        }

        if (status is 400 or 401 or 403 && await ErrorCode(response, cancellation) is { } code
            && (code is "invalid_grant" or "bad_refresh_token" || code.StartsWith("refresh_token_", StringComparison.Ordinal)))
        {
            return new SignInEndedException();
        }

        if (status is 401 or 403)
        {
            return new SignInEndedException();
        }

        return ModelProviderException.FromStatus(status);
    }

    private static async Task<string?> ErrorCode(HttpResponseMessage response, CancellationToken cancellation)
    {
        try
        {
            using var document = await BoundedResponse.ReadJson(response.Content, MaxAnswerBytes, "sign-in answer", cancellation);
            return Text(document.RootElement, "error");
        }
#pragma warning disable DRV0005
        catch (Exception exception) when (exception is JsonException or ModelProviderException)
        {
            return null;
        }
#pragma warning restore DRV0005
    }

    private ModelTokens ToTokens(JsonElement answer, ModelTokens? current)
    {
        if (answer.ValueKind is not JsonValueKind.Object || Text(answer, "access_token") is not { Length: > 0 } accessToken)
        {
            throw ModelProviderException.FromMalformedResponse();
        }

        var expiresIn = answer.TryGetProperty("expires_in", out var seconds) && seconds.ValueKind is JsonValueKind.Number
            && seconds.TryGetInt64(out var value) && value > 0
            ? TimeSpan.FromSeconds(value)
            : (TimeSpan?)null;
        var scopes = Text(answer, "scope") is { Length: > 0 } granted
            ? granted.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : current?.Scopes ?? options.CurrentValue.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return new ModelTokens
        {
            AccessToken = accessToken,
            RefreshToken = Text(answer, "refresh_token") ?? current?.RefreshToken,
            ExpiresAt = expiresIn is { } lifetime ? time.GetUtcNow() + lifetime : null,
            Scopes = scopes,
            AccountLabel = current?.AccountLabel,
            Details = current?.Details ?? new Dictionary<string, string>(StringComparer.Ordinal),
        };
    }

    private async Task<ModelTokens> WithAccount(ModelTokens tokens, CancellationToken cancellation)
    {
        var label = await ReadAccountLabel(tokens.AccessToken, cancellation);
        return label is null ? tokens : tokens with { AccountLabel = label };
    }

    private async Task<string?> ReadAccountLabel(string accessToken, CancellationToken cancellation)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, options.CurrentValue.UserAddress);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            ApplyGitHubHeaders(request);
            using var response = await Auth().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = await BoundedResponse.ReadJson(response.Content, MaxAnswerBytes, "account", cancellation);
            return Text(document.RootElement, "login");
        }
        // The account name is a label only: a missing or malformed answer still leaves a usable token set.
#pragma warning disable DRV0005
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException or ModelProviderException)
        {
            return null;
        }
#pragma warning restore DRV0005
    }

    #endregion

    #region Requests

    private HttpClient Auth() => httpClients.CreateClient(HttpClientName);

    private static HttpRequestMessage Form(Uri address, Dictionary<string, string> body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, address) { Content = new FormUrlEncodedContent(body) };
        ApplyGitHubHeaders(request);
        return request;
    }

    private static void ApplyGitHubHeaders(HttpRequestMessage request)
    {
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.Clear();
        request.Headers.UserAgent.ParseAdd(CopilotPlanOptions.UserAgent);
    }

    private static Dictionary<string, string> TokenBody(CopilotPlanOptions settings, Dictionary<string, string> fields)
    {
        fields["client_id"] = settings.ClientId;

        if (settings.ClientSecret.Length > 0)
        {
            fields["client_secret"] = settings.ClientSecret;
        }

        return fields;
    }

    private static async Task<JsonDocument> ReadAnswer(HttpResponseMessage response, CancellationToken cancellation) =>
        await BoundedResponse.ReadJson(response.Content, MaxAnswerBytes, "sign-in answer", cancellation);

    private void EnsureClientId()
    {
        if (options.CurrentValue.ClientId.Length == 0)
        {
            throw ModelProviderException.FromRefusal(
                $"The operator has not set the GitHub app's client id. Set {CopilotPlanOptions.Section}:{nameof(CopilotPlanOptions.ClientId)}.");
        }
    }

    private void EnsureClientSecret()
    {
        if (options.CurrentValue.ClientSecret.Length == 0)
        {
            throw ModelProviderException.FromRefusal(
                $"The operator has not set the GitHub app's client secret. Set {CopilotPlanOptions.Section}:{nameof(CopilotPlanOptions.ClientSecret)}.");
        }
    }

    private static void EnsureAbsolute(Uri address, string name)
    {
        if (!address.IsAbsoluteUri)
        {
            throw ModelProviderException.FromRefusal(
                $"The server setting {CopilotPlanOptions.Section}:{name} is not a whole address. Ask the operator to correct it.");
        }
    }

    private static ModelSignInSlowDownException SlowDown(JsonElement root)
    {
        var interval = root.TryGetProperty("interval", out var named) && named.TryGetInt32(out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : (TimeSpan?)null;
        return new ModelSignInSlowDownException(interval);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;

    private static int? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;

    #endregion

    #region Returns

    private static Uri RedirectFor(ModelSignInAttempt attempt, CopilotPlanOptions settings) =>
        attempt.RedirectUri is { } listening
            ? new UriBuilder(settings.RedirectUri) { Port = listening.Port }.Uri
            : settings.RedirectUri;

    private static Answer Read(string text)
    {
        var value = text.Trim();

        if (value.Length == 0)
        {
            return new Answer(null, null, Refused: false);
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var address)
            && (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps))
        {
            return FromQuery(address.Query);
        }

        return value.Contains('=', StringComparison.Ordinal) ? FromQuery(value) : new Answer(value, null, Refused: false);
    }

    private static Answer FromQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            values[Uri.UnescapeDataString(parts[0].Replace('+', ' '))] =
                parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty;
        }

        return new Answer(values.GetValueOrDefault("code"), values.GetValueOrDefault("state"), values.ContainsKey("error"));
    }

    private static bool IsLoopback(Uri address) =>
        address.IsLoopback || string.Equals(address.Host, "localhost", StringComparison.OrdinalIgnoreCase);

    #endregion

    #region Nested Types

    /// <summary>A code and its state as they came back, or a refusal. It holds a one-time code, so it prints nothing of it.</summary>
    private sealed record Answer(string? Code, string? State, bool Refused)
    {
        public override string ToString() => nameof(Answer);
    }

    #endregion
}
