namespace Drover.Adapters.Anthropic;

/// <summary>
/// Signs a person in with their own Claude plan: authorization code with PKCE and no client secret. A device that
/// listens gets the browser's return on the loopback address (<see cref="ModelSignInWait.Return"/>); any other device
/// is sent to the provider's page that shows <c>code#state</c> to copy (<see cref="ModelSignInWait.Paste"/>). Finishing
/// takes that text, a bare code, a whole return address or a return's query string. Refresh tokens rotate. The provider
/// has no known way to be told of a sign-out, so <see cref="Revoke"/> asks nothing and signing out deletes Drover's
/// copy. The account label stays null. See <see cref="AnthropicPlanSignInOptions"/> for the sources.
/// </summary>
public sealed class AnthropicPlanSignIn(
    IHttpClientFactory httpClients,
    IOptionsMonitor<AnthropicPlanSignInOptions> options,
    TimeProvider time) : IModelSignIn
{
    #region Private Fields

    public const string ConnectionKind = "anthropic_signin";

    public const string HttpClientName = "drover-anthropic-signin";

    private const int MaxAnswerBytes = 64 * 1024;

    /// <summary>A pasted <c>code#state</c> or return address is a few hundred bytes; anything longer is refused unread.</summary>
    public const int MaxPastedBytes = 4 * 1024;

    #endregion

    #region Properties

    public string Kind => ConnectionKind;

    public string DisplayName => "Claude plan";

    public bool IsPersonalPlan => true;

    #endregion

    #region Interface Implementations

    /// <summary>
    /// A loopback address on the configured path and any port, or no address at all: such a device pastes the code. The
    /// address sent to the provider names the configured host on the device's port (<see cref="RedirectFor"/>), so a
    /// device listening on <c>127.0.0.1</c>, as the desktop app does, is reached through <c>localhost</c>.
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
        var settings = options.CurrentValue;
        var query = string.Join(
            '&',
            "code=true",
            $"client_id={Uri.EscapeDataString(settings.ClientId)}",
            "response_type=code",
            $"redirect_uri={Uri.EscapeDataString(RedirectFor(attempt, settings).AbsoluteUri)}",
            $"scope={Uri.EscapeDataString(settings.Scopes)}",
            $"code_challenge={Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(attempt.Verifier)))}",
            "code_challenge_method=S256",
            $"state={Uri.EscapeDataString(attempt.State)}");
        var address = new UriBuilder(settings.StartAddress) { Query = query }.Uri;
        var wait = attempt.RedirectUri is null ? ModelSignInWait.Paste : ModelSignInWait.Return;

        return Task.FromResult(new ModelSignInStep(address, null, wait));
    }

    public async Task<ModelTokens?> Finish(ModelSignInAttempt attempt, ModelSignInReturn arrived, CancellationToken cancellation)
    {
        if (Encoding.UTF8.GetByteCount(arrived.Text) > MaxPastedBytes)
        {
            throw new ModelSignInRefusedException();
        }

        var answer = Read(arrived.Text);

        // A browser's return always names its state. Pasted text may be the bare code, which names none.
        var stateMatches = answer.State is { } state
            ? CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(attempt.State))
            : attempt.RedirectUri is null;

        if (answer.Refused || !stateMatches || answer.Code is not { Length: > 0 } code)
        {
            throw new ModelSignInRefusedException();
        }

        var settings = options.CurrentValue;
        var body = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = settings.ClientId,
            ["code"] = code,
            ["state"] = attempt.State,
            ["redirect_uri"] = RedirectFor(attempt, settings).AbsoluteUri,
            ["code_verifier"] = attempt.Verifier,
        };

        return await Ask(body, null, refreshing: false, cancellation);
    }

    public Task<ModelTokens> Refresh(ModelTokens current, CancellationToken cancellation)
    {
        if (current.RefreshToken is not { Length: > 0 } refreshToken)
        {
            throw new SignInEndedException();
        }

        var body = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = options.CurrentValue.ClientId,
            ["refresh_token"] = refreshToken,
        };

        return Ask(body, current, refreshing: true, cancellation);
    }

    /// <summary>No call is known that tells the provider; signing out deletes Drover's copy of the tokens.</summary>
    public Task Revoke(ModelTokens current, CancellationToken cancellation) => Task.CompletedTask;

    #endregion

    #region Token Requests

    /// <summary>
    /// Asks the token address and reads the token set. An exchange the provider will not make is a refusal. A refresh
    /// answered 400, 401 or 403 with <c>invalid_grant</c> or a <c>refresh_token_</c> code ends the sign-in; anything
    /// else may pass. No message here holds a token, a code or anything the provider sent.
    /// </summary>
    private async Task<ModelTokens> Ask(Dictionary<string, string> body, ModelTokens? current, bool refreshing, CancellationToken cancellation)
    {
        var address = options.CurrentValue.TokenAddress;

        // A relative address is the operator's setting at fault, not the provider's answer, so it is not reported as one.
        if (!address.IsAbsoluteUri)
        {
            throw ModelProviderException.FromRefusal(
                $"The server setting {AnthropicPlanSignInOptions.Section}:{nameof(AnthropicPlanSignInOptions.TokenAddress)} is not a whole address. Ask the operator to correct it.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, address) { Content = JsonContent.Create(body) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
            var status = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                throw await Failure(response, status, refreshing, cancellation);
            }

            using var document = await BoundedResponse.ReadJson(response.Content, MaxAnswerBytes, "sign-in answer", cancellation);
            return ToTokens(document.RootElement, current);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw ModelProviderException.FromMalformedResponse();
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException { InnerException: TimeoutException })
        {
            throw AnthropicFailures.TryMap(exception, out var mapped) ? mapped : ModelProviderException.FromNetwork();
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
            && (code == "invalid_grant" || code.StartsWith("refresh_token_", StringComparison.Ordinal)))
        {
            return new SignInEndedException();
        }

        return ModelProviderException.FromStatus(status);
    }

    /// <summary>The answer's error code: OAuth's <c>error</c> string, or the <c>type</c> of an <c>error</c> object.</summary>
    private static async Task<string?> ErrorCode(HttpResponseMessage response, CancellationToken cancellation)
    {
        try
        {
            using var document = await BoundedResponse.ReadJson(response.Content, MaxAnswerBytes, "sign-in answer", cancellation);
            var root = document.RootElement;

            if (root.ValueKind is not JsonValueKind.Object || !root.TryGetProperty("error", out var error))
            {
                return null;
            }

            return error.ValueKind switch
            {
                JsonValueKind.String => error.GetString(),
                JsonValueKind.Object when error.TryGetProperty("type", out var type) && type.ValueKind is JsonValueKind.String => type.GetString(),
                _ => null,
            };
        }
        // An error body that is too large or not JSON names no code: the failure is then judged by its status alone.
#pragma warning disable DRV0005
        catch (Exception exception) when (exception is JsonException or ModelProviderException)
        {
            return null;
        }
#pragma warning restore DRV0005
    }

    /// <summary>
    /// The token set in an answer. A refresh answer that names no new refresh token keeps the current one, and a refresh
    /// keeps the sign-in's account label and details.
    /// </summary>
    private ModelTokens ToTokens(JsonElement answer, ModelTokens? current)
    {
        if (answer.ValueKind is not JsonValueKind.Object || Text(answer, "access_token") is not { Length: > 0 } accessToken)
        {
            throw ModelProviderException.FromMalformedResponse();
        }

        var expiresIn = answer.TryGetProperty("expires_in", out var seconds) && seconds.ValueKind is JsonValueKind.Number && seconds.TryGetInt64(out var value) && value > 0
            ? TimeSpan.FromSeconds(value)
            : (TimeSpan?)null;
        var scopes = Text(answer, "scope") is { Length: > 0 } granted
            ? granted.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : current?.Scopes ?? options.CurrentValue.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);

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

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;

    #endregion

    #region Returns

    /// <summary>
    /// The address the provider sends the browser back to: the device's own loopback port under the configured host,
    /// or the provider's code page for a device that cannot listen. The exchange names the same address.
    /// </summary>
    private static Uri RedirectFor(ModelSignInAttempt attempt, AnthropicPlanSignInOptions settings) =>
        attempt.RedirectUri is { } listening
            ? new UriBuilder(settings.RedirectUri) { Port = listening.Port }.Uri
            : settings.CodePageRedirectUri;

    /// <summary>What came back, in any of the shapes a person may paste or a device may post.</summary>
    private static Answer Read(string text)
    {
        var value = text.Trim();

        if (value.Length == 0)
        {
            return new Answer(null, null, Refused: false);
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var address) && (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps))
        {
            return FromQuery(address.Query);
        }

        if (value.Contains('#', StringComparison.Ordinal))
        {
            var parts = value.Split('#', 2);
            return new Answer(parts[0], parts[1], Refused: false);
        }

        return value.Contains('=', StringComparison.Ordinal) ? FromQuery(value) : new Answer(value, null, Refused: false);
    }

    private static Answer FromQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            values[Uri.UnescapeDataString(parts[0].Replace('+', ' '))] = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty;
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
