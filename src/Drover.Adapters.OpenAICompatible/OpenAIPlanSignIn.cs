namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// Signs a person in with their own ChatGPT plan, on the flow the provider documents for open source and locally
/// hosted apps: authorization code with PKCE, a public client with no secret, and a client id the provider issues to
/// the person on their first sign-in. Every value it sends is a setting (<see cref="OpenAIPlanOptions"/>).
/// </summary>
/// <remarks>
/// Sources, read 2026-11-27 (https://developers.openai.com/siwc/llms-full.txt, "Registration and sign-in", "Accounts
/// and sessions", "Token reference", "Errors and recovery", and https://auth.openai.com/.well-known/openid-configuration):
/// <list type="bullet">
/// <item>A first sign-in sends <c>client_id=dynamic_agent_client</c> with <c>agent_name_hint</c>; the return carries the
/// issued client id, which is kept in the token set (<see cref="ClientIdDetail"/>) and sent on every later sign-in,
/// exchange, refresh and revoke. A return with another client id than the one asked with is refused.</item>
/// <item><c>ext_agent_host_id</c> is required and must be opaque and stable for its host. Drover counts one host for
/// each person's sign-in on this server: a <c>urn:uuid:</c> made once and kept in the token set
/// (<see cref="HostIdDetail"/>), so it never names the person.</item>
/// <item><c>resource</c> goes on the approval, the exchange and every refresh.</item>
/// <item>The ID token is checked with the platform's token handler against the provider's published keys: issuer,
/// audience (the issued client id), expiry and the attempt's nonce. A later sign-in must be the same account
/// (<c>sub</c>). Nothing is kept unless the token answer grants <see cref="OpenAIPlanOptions.RequiredScope"/>.</item>
/// <item>Refresh tokens rotate and last 30 days; access tokens last an hour. <c>invalid_grant</c> and the provider's
/// <c>refresh_token_*</c>, <c>invalid_refresh_token</c> and <c>token_expired</c> codes end the sign-in.</item>
/// <item>Signing out revokes the refresh token at the discovery document's <c>revocation_endpoint</c>.</item>
/// </list>
/// No request is retried here: a code is spent once and a refresh token rotates, so a repeat could end the sign-in.
/// </remarks>
public sealed class OpenAIPlanSignIn(IHttpClientFactory httpClients, IOptionsMonitor<OpenAIPlanOptions> options, TimeProvider time) : IModelSignIn
{
    #region Private Fields

    public const string HttpClientName = "drover-openai-plan-sign-in";

    /// <summary>The client id the provider issued to the person, kept in <see cref="ModelTokens.Details"/>.</summary>
    public const string ClientIdDetail = "client_id";

    /// <summary>The host id sent as <c>ext_agent_host_id</c>, kept in <see cref="ModelTokens.Details"/>.</summary>
    public const string HostIdDetail = "host_id";

    /// <summary>The account's validated <c>sub</c>, kept in <see cref="ModelTokens.Details"/>.</summary>
    public const string SubjectDetail = "subject";

    /// <summary>What the person is told when the account signed in but did not allow plan use.</summary>
    public const string PlanUseNotAllowed = "ChatGPT signed you in but did not allow plan use. Try again and allow it.";

    /// <summary>The provider's codes for a refresh token that cannot be used any more ("Refresh errors").</summary>
    private static readonly HashSet<string> endedCodes =
    [
        with(StringComparer.Ordinal),
        "invalid_grant",
        "invalid_refresh_token",
        "token_expired",
        "refresh_token_expired",
        "refresh_token_invalidated",
        "refresh_token_reused",
    ];

    #endregion

    #region Properties

    public string Kind => OpenAIPlanProvider.ConnectionKind;

    public string DisplayName => OpenAIPlanProvider.DisplayName;

    public bool IsPersonalPlan => true;

    #endregion

    #region Interface Implementations

    /// <summary>
    /// The configured loopback address on any port, and nothing else: the provider matches the scheme, the host and
    /// the path exactly ("Do not substitute with localhost").
    /// </summary>
    public bool AcceptsRedirect(Uri? redirectUri)
    {
        var configured = options.CurrentValue.RedirectUri;

        return redirectUri is { IsAbsoluteUri: true }
            && redirectUri.Scheme == configured.Scheme
            && redirectUri.Host == configured.Host
            && redirectUri.AbsolutePath == configured.AbsolutePath
            && redirectUri.UserInfo.Length == 0
            && redirectUri.Query.Length == 0
            && redirectUri.Fragment.Length == 0;
    }

    public Task<ModelSignInStep> Begin(ModelSignInAttempt attempt, CancellationToken cancellation)
    {
        var settings = options.CurrentValue;
        var redirect = attempt.RedirectUri ?? throw new ModelSignInRefusedException();
        var issued = attempt.Earlier.GetValueOrDefault(ClientIdDetail);
        List<KeyValuePair<string, string>> query =
        [
            new("client_id", issued ?? settings.RegistrationClientId),
            new("ext_agent_host_id", HostIdFor(attempt)),
            new("response_type", "code"),
            new("redirect_uri", redirect.AbsoluteUri),
            new("scope", settings.Scopes),
            new("resource", settings.Resource),
            new("state", attempt.State),
            new("nonce", attempt.Nonce),
            new("code_challenge_method", "S256"),
            new("code_challenge", Challenge(attempt.Verifier)),
        ];

        if (issued is null)
        {
            query.Insert(1, new("agent_name_hint", settings.AgentName));
        }

        var address = new UriBuilder(settings.AuthorizeAddress) { Query = Encode(query) }.Uri;
        return Task.FromResult(new ModelSignInStep(address, null, ModelSignInWait.Return));
    }

    public async Task<ModelTokens?> Finish(ModelSignInAttempt attempt, ModelSignInReturn arrived, CancellationToken cancellation)
    {
        var settings = options.CurrentValue;
        var returned = ParseQuery(arrived.Text);

        if (attempt.RedirectUri is not { } redirect
            || !returned.TryGetValue("state", out var state)
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(attempt.State))
            || returned.ContainsKey("error")
            || !returned.TryGetValue("code", out var code) || code.Length == 0)
        {
            throw new ModelSignInRefusedException();
        }

        var clientId = ClientIdFor(attempt, returned, settings);
        var answer = await Exchange(settings, clientId, code, attempt.Verifier, redirect, cancellation);

        if (!answer.Scopes.Contains(settings.RequiredScope, StringComparer.Ordinal))
        {
            throw new ModelSignInRefusedException(PlanUseNotAllowed);
        }

        var identity = await Validate(settings, answer.IdToken, clientId, attempt.Nonce, cancellation);

        if (attempt.Earlier.TryGetValue(SubjectDetail, out var earlier) && earlier != identity.Subject)
        {
            // A sign-in with the person's issued client came back as another account: their registration is not replaced.
            throw new ModelSignInRefusedException();
        }

        return answer.ToTokens(time.GetUtcNow(), refreshToken: null) with
        {
            AccountLabel = identity.Email,
            Details = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ClientIdDetail] = clientId,
                [HostIdDetail] = HostIdFor(attempt),
                [SubjectDetail] = identity.Subject,
            },
        };
    }

    public async Task<ModelTokens> Refresh(ModelTokens current, CancellationToken cancellation)
    {
        var settings = options.CurrentValue;

        if (current.RefreshToken is not { Length: > 0 } refreshToken || !current.Details.TryGetValue(ClientIdDetail, out var clientId))
        {
            throw new SignInEndedException();
        }

        // "omit scope to retain the grant"
        var answer = await PostToken(
            settings.TokenAddress,
            [
                new("grant_type", "refresh_token"),
                new("client_id", clientId),
                new("refresh_token", refreshToken),
                new("resource", settings.Resource),
            ],
            ended: true,
            cancellation);

        return answer.ToTokens(time.GetUtcNow(), refreshToken) with
        {
            AccountLabel = current.AccountLabel,
            Details = current.Details,
            Scopes = answer.Scopes.Count > 0 ? answer.Scopes : current.Scopes,
        };
    }

    /// <summary>An empty 200 is success, also for a token that was already invalid.</summary>
    public async Task Revoke(ModelTokens current, CancellationToken cancellation)
    {
        if (current.RefreshToken is not { Length: > 0 } refreshToken || !current.Details.TryGetValue(ClientIdDetail, out var clientId))
        {
            return;
        }

        using var request = Form(options.CurrentValue.RevokeAddress, [
            new("token", refreshToken),
            new("token_type_hint", "refresh_token"),
            new("client_id", clientId),
        ]);
        using var response = await Send(request, cancellation);

        if (!response.IsSuccessStatusCode)
        {
            throw ModelProviderException.FromStatus((int)response.StatusCode);
        }
    }

    #endregion

    #region Exchange

    /// <summary>
    /// The issued client id to exchange with. A person who signed in before is asked with theirs, and a return that
    /// names another is refused rather than replace their registration. A first sign-in must come back with one that is
    /// not the registration entry point, or the registration is not complete.
    /// </summary>
    private static string ClientIdFor(ModelSignInAttempt attempt, Dictionary<string, string> returned, OpenAIPlanOptions settings)
    {
        var came = returned.GetValueOrDefault("client_id");

        if (attempt.Earlier.TryGetValue(ClientIdDetail, out var issued))
        {
            return came is null || came == issued ? issued : throw new ModelSignInRefusedException();
        }

        return came is { Length: > 0 } && came != settings.RegistrationClientId ? came : throw new ModelSignInRefusedException();
    }

    private Task<TokenAnswer> Exchange(OpenAIPlanOptions settings, string clientId, string code, string verifier, Uri redirect, CancellationToken cancellation) =>
        PostToken(
            settings.TokenAddress,
            [
                new("grant_type", "authorization_code"),
                new("client_id", clientId),
                new("code", code),
                new("code_verifier", verifier),
                new("redirect_uri", redirect.AbsoluteUri),
                new("resource", settings.Resource),
            ],
            ended: false,
            cancellation);

    /// <summary>
    /// One request to the token address. A refusal that names a spent or unusable grant ends a refresh
    /// (<paramref name="ended"/>) or the attempt; anything else is a failure that may pass. Nothing the provider sent
    /// goes into an exception.
    /// </summary>
    private async Task<TokenAnswer> PostToken(Uri address, List<KeyValuePair<string, string>> fields, bool ended, CancellationToken cancellation)
    {
        using var request = Form(address, fields);
        using var response = await Send(request, cancellation);
        using var document = await ReadJson(response, cancellation);

        if (response.IsSuccessStatusCode)
        {
            return TokenAnswer.Read(document?.RootElement ?? default);
        }

        var status = (int)response.StatusCode;

        if (status is 400 or 401 or 403 && NamesAnEndedGrant(document?.RootElement ?? default))
        {
            throw ended ? new SignInEndedException() : new ModelSignInRefusedException();
        }

        throw ModelProviderException.FromStatus(status);
    }

    private static bool NamesAnEndedGrant(JsonElement body) =>
        ErrorCodes(body).Any(code => endedCodes.Contains(code) || code.StartsWith("refresh_token_", StringComparison.Ordinal));

    /// <summary>The codes an error answer carries: <c>error</c> as a word or as an object's <c>code</c>, and <c>code</c> or <c>error_code</c>.</summary>
    private static IEnumerable<string> ErrorCodes(JsonElement body)
    {
        if (body.ValueKind is not JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var name in (string[])["error", "code", "error_code"])
        {
            if (!body.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind is JsonValueKind.String && value.GetString() is { } word)
            {
                yield return word;
            }
            else if (value.ValueKind is JsonValueKind.Object && value.TryGetProperty("code", out var inner) && inner.ValueKind is JsonValueKind.String)
            {
                yield return inner.GetString()!;
            }
        }
    }

    #endregion

    #region Identity

    /// <summary>
    /// Checks the ID token against the keys the provider publishes, with nothing taken from the token itself to find
    /// them: only RS256, the configured issuer, the issued client id as audience, an expiry, and the attempt's nonce.
    /// </summary>
    private async Task<Identity> Validate(OpenAIPlanOptions settings, string? idToken, string clientId, string nonce, CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(idToken))
        {
            throw new ModelSignInRefusedException();
        }

        var keys = await ReadKeys(settings.KeysAddress, cancellation);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidIssuer = settings.Issuer,
            ValidAudience = clientId,
            IssuerSigningKeys = keys,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            LifetimeValidator = (notBefore, expires, _, _) => IsCurrent(notBefore, expires),
        });

        if (!result.IsValid || result.SecurityToken is not JsonWebToken token || !HasNonce(token, nonce) || string.IsNullOrEmpty(token.Subject))
        {
            throw new ModelSignInRefusedException();
        }

        return new Identity(token.Subject, token.TryGetPayloadValue<string>("email", out var email) ? email : null);
    }

    /// <summary>The token's lifetime against Drover's clock, with two minutes either way for clocks that disagree.</summary>
    private bool IsCurrent(DateTime? notBefore, DateTime? expires)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var skew = TimeSpan.FromMinutes(2);
        return expires is { } end && now < end + skew && (notBefore is not { } start || now >= start - skew);
    }

    private static bool HasNonce(JsonWebToken token, string nonce) =>
        token.TryGetPayloadValue<string>("nonce", out var said) && said is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(said), Encoding.UTF8.GetBytes(nonce));

    private async Task<IList<SecurityKey>> ReadKeys(Uri address, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        using var response = await Send(request, cancellation);

        if (!response.IsSuccessStatusCode)
        {
            throw ModelProviderException.FromStatus((int)response.StatusCode);
        }

        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellation);
            return new JsonWebKeySet(text).GetSigningKeys();
        }
        catch (ArgumentException)
        {
            throw ModelProviderException.FromMalformedResponse();
        }
    }

    #endregion

    #region Transport

    private async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken cancellation)
    {
        try
        {
            return await httpClients.CreateClient(HttpClientName).SendAsync(request, cancellation);
        }
        catch (Exception failure) when (ProtocolFailureMapping.FindRefusal(failure) is { } refusal)
        {
            throw refusal;
        }
        catch (HttpRequestException)
        {
            throw ModelProviderException.FromNetwork();
        }
        catch (TaskCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw ModelProviderException.FromTimeout();
        }
    }

    private static HttpRequestMessage Form(Uri address, List<KeyValuePair<string, string>> fields) =>
        new(HttpMethod.Post, address) { Content = new FormUrlEncodedContent(fields) };

    /// <summary>The answer's JSON, or null when it has none Drover can read.</summary>
    private static async Task<JsonDocument?> ReadJson(HttpResponseMessage response, CancellationToken cancellation)
    {
        try
        {
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellation);
            return bytes.Length is 0 or > 1_000_000 ? null : JsonDocument.Parse(bytes);
        }
        // An answer that is not JSON is read as one with no code; its text is never kept.
#pragma warning disable DRV0005
        catch (JsonException)
        {
            return null;
        }
#pragma warning restore DRV0005
    }

    #endregion

    #region Query Strings

    /// <summary>
    /// The host id for this sign-in: the person's earlier one, or for a first sign-in a <c>urn:uuid:</c> made from the
    /// attempt's own secret verifier, so <see cref="Begin"/> and <see cref="Finish"/> agree on it without keeping
    /// anything between them, and nothing outside the attempt can work it out.
    /// </summary>
    private static string HostIdFor(ModelSignInAttempt attempt)
    {
        if (attempt.Earlier.TryGetValue(HostIdDetail, out var earlier))
        {
            return earlier;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"ext_agent_host_id:{attempt.Verifier}")).AsSpan(0, 16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return $"urn:uuid:{new Guid(bytes, bigEndian: true):D}";
    }

    private static string Challenge(string verifier) => Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Encode(List<KeyValuePair<string, string>> fields) =>
        string.Join('&', fields.Select(field => $"{Uri.EscapeDataString(field.Key)}={Uri.EscapeDataString(field.Value)}"));

    /// <summary>The query string the device received, with or without its leading <c>?</c>. A name given twice is not trusted.</summary>
    private static Dictionary<string, string> ParseQuery(string text)
    {
        Dictionary<string, string> fields = [with(StringComparer.Ordinal)];
        HashSet<string> repeated = [with(StringComparer.Ordinal)];

        foreach (var pair in text.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var name = Unescape(parts[0]);

            if (!fields.TryAdd(name, parts.Length > 1 ? Unescape(parts[1]) : string.Empty))
            {
                repeated.Add(name);
            }
        }

        foreach (var name in repeated)
        {
            fields.Remove(name);
        }

        return fields;
    }

    private static string Unescape(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));

    #endregion

    #region Nested Types

    private sealed record Identity(string Subject, string? Email);

    /// <summary>What the token address answered, read without keeping anything else of it.</summary>
    private sealed record TokenAnswer(string AccessToken, string? RefreshToken, string? IdToken, int? ExpiresIn, IReadOnlyList<string> Scopes)
    {
        public static TokenAnswer Read(JsonElement body)
        {
            if (body.ValueKind is not JsonValueKind.Object || Text(body, "access_token") is not { Length: > 0 } access)
            {
                throw ModelProviderException.FromMalformedResponse();
            }

            var expiresIn = body.TryGetProperty("expires_in", out var value) && value.TryGetInt32(out var seconds) ? seconds : (int?)null;
            var scopes = Text(body, "scope")?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
            return new TokenAnswer(access, Text(body, "refresh_token"), Text(body, "id_token"), expiresIn, scopes);
        }

        /// <summary>The token set. A refresh answer without a new refresh token keeps <paramref name="refreshToken"/>.</summary>
        public ModelTokens ToTokens(DateTimeOffset now, string? refreshToken) =>
            new()
            {
                AccessToken = AccessToken,
                RefreshToken = RefreshToken ?? refreshToken,
                ExpiresAt = now.AddSeconds(ExpiresIn ?? 3600),
                Scopes = Scopes,
            };

        private static string? Text(JsonElement body, string name) =>
            body.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;
    }

    #endregion
}
