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
public sealed class OpenAIPlanSignIn(
    IHttpClientFactory httpClients,
    IOptionsMonitor<OpenAIPlanOptions> options,
    TimeProvider time,
    ILogger<OpenAIPlanSignIn> logger) : IModelSignIn
{
    #region Private Fields

    public const string HttpClientName = "drover-openai-plan-sign-in";

    /// <summary>The client id the provider issued to the person, kept in <see cref="ModelTokens.Details"/>.</summary>
    public const string ClientIdDetail = "client_id";

    /// <summary>The host id sent as <c>ext_agent_host_id</c>, kept in <see cref="ModelTokens.Details"/>.</summary>
    public const string HostIdDetail = "host_id";

    /// <summary>The account's validated <c>sub</c>, kept in <see cref="ModelTokens.Details"/>.</summary>
    public const string SubjectDetail = "subject";

    /// <summary>Kept in <see cref="ModelTokens.Details"/> when the person signed in but did not allow plan use.</summary>
    public const string PlanUseDetail = "plan_use";

    public const string PlanUseNotAllowedValue = "not_allowed";

    /// <summary>Which flow made the token set, kept in <see cref="ModelTokens.Details"/>: <see cref="CodeFlow"/>, or absent for the browser.</summary>
    public const string FlowDetail = "flow";

    public const string CodeFlow = "code";

    /// <summary>The ChatGPT account a sign-in by code calls as, kept in <see cref="ModelTokens.Details"/>.</summary>
    public const string AccountDetail = "account_id";

    /// <summary>What the person is told when ChatGPT will not give or take a code.</summary>
    public const string CodeRefused = "ChatGPT refused the code. Turn on device code sign-in in ChatGPT's security settings, then try again.";

    /// <summary>What a sign-in by code keeps between starting and finishing (<see cref="ModelSignInStep.Kept"/>).</summary>
    private const string DeviceAuthKept = "device_auth_id";

    private const string UserCodeKept = "user_code";

    /// <summary>What the person is told when a sign-in with their own registration came back as another account.</summary>
    public const string AnotherAccount = "That is a different ChatGPT account. Sign out first.";

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
    /// the path exactly ("Do not substitute with localhost"). A device that cannot listen (null) signs in by code.
    /// </summary>
    public bool AcceptsRedirect(Uri? redirectUri)
    {
        var configured = options.CurrentValue.RedirectUri;

        return redirectUri is null || redirectUri is { IsAbsoluteUri: true }
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

        if (attempt.RedirectUri is not { } redirect)
        {
            return BeginByCode(settings.Code, cancellation);
        }

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

        // After a sign-in that did not allow plan use, the provider asks for the whole scope set again with consent
        // ("ChatGPT plan use isn't enabled": prompt=consent until force_reconsent is confirmed for the integration).
        if (attempt.Earlier.GetValueOrDefault(PlanUseDetail) == PlanUseNotAllowedValue)
        {
            query.Add(new("prompt", "consent"));
        }

        var address = new UriBuilder(settings.AuthorizeAddress) { Query = Encode(query) }.Uri;
        return Task.FromResult(new ModelSignInStep(address, null, ModelSignInWait.Return));
    }

    public async Task<ModelTokens?> Finish(ModelSignInAttempt attempt, ModelSignInReturn arrived, CancellationToken cancellation)
    {
        var settings = options.CurrentValue;

        if (attempt.RedirectUri is null)
        {
            return await FinishByCode(settings, attempt, cancellation);
        }

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

        try
        {
            return await Accept(settings, attempt, clientId, answer, cancellation);
        }
        catch (ModelSignInRefusedException)
        {
            // The code is spent and the provider issued a renewable session nobody will use: end it.
            await Abandon(settings, clientId, answer.RefreshToken);
            throw;
        }
    }

    public async Task<ModelTokens> Refresh(ModelTokens current, CancellationToken cancellation)
    {
        var settings = options.CurrentValue;

        if (IsByCode(current))
        {
            return await RefreshByCode(settings.Code, current, cancellation);
        }

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
        if (IsByCode(current))
        {
            await RevokeByCode(options.CurrentValue.Code, current.RefreshToken, cancellation);
            return;
        }

        if (current.RefreshToken is not { Length: > 0 } refreshToken || !current.Details.TryGetValue(ClientIdDetail, out var clientId))
        {
            return;
        }

        await RevokeAt(options.CurrentValue.RevokeAddress, clientId, refreshToken, cancellation);
    }

    private async Task RevokeAt(Uri address, string clientId, string refreshToken, CancellationToken cancellation)
    {
        using var request = Form(address, [
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
    /// What a successful exchange comes to. The ID token is checked first ("Validate the ID token"), then that it is the
    /// person's own account, then the plan scope. A valid sign-in without the plan scope is kept as the provider asks
    /// ("ChatGPT plan use isn't enabled": retain the sign-in, mark plan use disabled): the issued client id, host id and
    /// subject, with no token, so the next attempt asks again with the same registration and for consent.
    /// </summary>
    private async Task<ModelTokens> Accept(OpenAIPlanOptions settings, ModelSignInAttempt attempt, string clientId, TokenAnswer answer, CancellationToken cancellation)
    {
        var identity = await Validate(settings, answer.IdToken, clientId, attempt.Nonce, cancellation);

        if (attempt.Earlier.TryGetValue(SubjectDetail, out var earlier) && earlier != identity.Subject)
        {
            // A sign-in with the person's issued client came back as another account: their registration is not replaced.
            throw new ModelSignInRefusedException(AnotherAccount);
        }

        Dictionary<string, string> details = new(StringComparer.Ordinal)
        {
            [ClientIdDetail] = clientId,
            [HostIdDetail] = HostIdFor(attempt),
            [SubjectDetail] = identity.Subject,
        };

        if (!answer.Scopes.Contains(settings.RequiredScope, StringComparer.Ordinal))
        {
            details[PlanUseDetail] = PlanUseNotAllowedValue;
            throw new ModelSignInRefusedException(PlanUseNotAllowed) { Kept = details };
        }

        return answer.ToTokens(time.GetUtcNow(), refreshToken: null) with { AccountLabel = identity.Email, Details = details };
    }

    /// <summary>Revokes a refresh token that will not be kept. It is a courtesy to the provider: a failure is logged by kind and passes.</summary>
    private async Task Abandon(OpenAIPlanOptions settings, string clientId, string? refreshToken)
    {
        if (refreshToken is not { Length: > 0 })
        {
            return;
        }

        try
        {
            await RevokeAt(settings.RevokeAddress, clientId, refreshToken, CancellationToken.None);
        }
        catch (Exception failure) when (failure is ModelProviderException or HttpRequestException or IOException or OperationCanceledException)
        {
            logger.LogInformation("A refused ChatGPT plan sign-in's session could not be revoked: {Failure}", failure.GetType().Name);
        }
    }

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
    private Task<TokenAnswer> PostToken(Uri address, List<KeyValuePair<string, string>> fields, bool ended, CancellationToken cancellation) =>
        PostToken(address, new FormUrlEncodedContent(fields), ended, cancellation);

    private async Task<TokenAnswer> PostToken(Uri address, HttpContent content, bool ended, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, address) { Content = content };
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

    #region Sign-In By Code

    private static bool IsByCode(ModelTokens tokens) => tokens.Details.GetValueOrDefault(FlowDetail) == CodeFlow;

    /// <summary>
    /// Asks the provider for a code the person enters on its page. A provider that will not give one is told to the
    /// person in plain words; the answer's own text is never kept.
    /// </summary>
    private async Task<ModelSignInStep> BeginByCode(OpenAIPlanCodeOptions settings, CancellationToken cancellation)
    {
        using var request = Json(settings.StartAddress, new JsonObject { ["client_id"] = settings.ClientId });
        using var response = await Send(request, cancellation);
        using var document = await ReadJson(response, cancellation);
        var status = (int)response.StatusCode;

        if (!response.IsSuccessStatusCode)
        {
            throw status is >= 400 and < 500 and not 408 and not 429
                ? new ModelSignInRefusedException(CodeRefused)
                : ModelProviderException.FromStatus(status);
        }

        var body = document?.RootElement ?? default;

        if (Text(body, "device_auth_id") is not { Length: > 0 } deviceAuthId
            || (Text(body, "user_code") ?? Text(body, "usercode")) is not { Length: > 0 } userCode)
        {
            throw ModelProviderException.FromMalformedResponse();
        }

        return new ModelSignInStep(settings.PageAddress, userCode, ModelSignInWait.Poll)
        {
            Interval = Seconds(body, "interval"),
            Lifetime = settings.CodeLifetime,
            Kept = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [DeviceAuthKept] = deviceAuthId,
                [UserCodeKept] = userCode,
            },
        };
    }

    /// <summary>
    /// One ask: null while the person has not approved (a 403 or 404, or the provider's pending code), and once they
    /// have, the exchange of the code it hands back. A refusal ends the attempt with the sentence that says what to do.
    /// </summary>
    private async Task<ModelTokens?> FinishByCode(OpenAIPlanOptions settings, ModelSignInAttempt attempt, CancellationToken cancellation)
    {
        var code = settings.Code;

        if (attempt.Begun.GetValueOrDefault(DeviceAuthKept) is not { Length: > 0 } deviceAuthId
            || attempt.Begun.GetValueOrDefault(UserCodeKept) is not { Length: > 0 } userCode)
        {
            throw new ModelSignInRefusedException();
        }

        using var request = Json(code.PollAddress, new JsonObject { ["device_auth_id"] = deviceAuthId, ["user_code"] = userCode });
        using var response = await Send(request, cancellation);
        using var document = await ReadJson(response, cancellation);
        var body = document?.RootElement ?? default;
        var status = (int)response.StatusCode;
        var said = ErrorCodes(body).ToList();

        if (said.Contains("slow_down") || status is 429)
        {
            throw new ModelSignInSlowDownException(Seconds(body, "interval"));
        }

        if (status is 403 or 404 || said.Contains("deviceauth_authorization_pending") || said.Contains("authorization_pending"))
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw status is >= 400 and < 500 and not 408 ? new ModelSignInRefusedException(CodeRefused) : ModelProviderException.FromStatus(status);
        }

        if (Text(body, "authorization_code") is not { Length: > 0 } authorizationCode || Text(body, "code_verifier") is not { Length: > 0 } verifier)
        {
            throw ModelProviderException.FromMalformedResponse();
        }

        var answer = await PostToken(
            code.TokenAddress,
            [
                new("grant_type", "authorization_code"),
                new("client_id", code.ClientId),
                new("code", authorizationCode),
                new("code_verifier", verifier),
                new("redirect_uri", code.RedirectUri.AbsoluteUri),
            ],
            ended: false,
            cancellation);

        try
        {
            return await AcceptByCode(settings, attempt, answer, cancellation);
        }
        catch (ModelSignInRefusedException)
        {
            // The code is spent and the provider issued a renewable session nobody will use: end it.
            await AbandonByCode(code, answer.RefreshToken);
            throw;
        }
    }

    /// <summary>
    /// What an exchange by code comes to: the ID token checked as on the browser flow (with no nonce, which the device
    /// flow does not take), and the account the access token names, which every call is sent as. What the person's
    /// browser sign-in registered (an issued client id, a host id, the subject) is kept beside it, so signing in on a
    /// desktop later does not register anew.
    /// </summary>
    private async Task<ModelTokens> AcceptByCode(OpenAIPlanOptions settings, ModelSignInAttempt attempt, TokenAnswer answer, CancellationToken cancellation)
    {
        var identity = await Validate(settings, answer.IdToken, settings.Code.ClientId, nonce: null, cancellation);

        if ((PlanAccount.Of(answer.AccessToken) ?? PlanAccount.Of(answer.IdToken)) is not { } account)
        {
            throw new ModelSignInRefusedException();
        }

        Dictionary<string, string> details = new(StringComparer.Ordinal)
        {
            [FlowDetail] = CodeFlow,
            [AccountDetail] = account,
        };

        foreach (var kept in (string[])[ClientIdDetail, HostIdDetail, SubjectDetail])
        {
            if (attempt.Earlier.GetValueOrDefault(kept) is { } value)
            {
                details[kept] = value;
            }
        }

        return answer.ToTokens(time.GetUtcNow(), refreshToken: null) with
        {
            ExpiresAt = ExpiryOf(answer),
            AccountLabel = identity.Email,
            Details = details,
        };
    }

    /// <summary>
    /// A new token set for a sign-in by code. The refresh token rotates; an answer for another ChatGPT account than the
    /// one signed in ends the sign-in, so a person's calls never go out as someone else.
    /// </summary>
    private async Task<ModelTokens> RefreshByCode(OpenAIPlanCodeOptions settings, ModelTokens current, CancellationToken cancellation)
    {
        if (current.RefreshToken is not { Length: > 0 } refreshToken)
        {
            throw new SignInEndedException();
        }

        var body = new JsonObject { ["client_id"] = settings.ClientId, ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken };
        var answer = await PostToken(settings.TokenAddress, JsonContent(body), ended: true, cancellation);

        if (PlanAccount.Of(answer.AccessToken) is { } account && account != current.Details.GetValueOrDefault(AccountDetail))
        {
            await AbandonByCode(settings, answer.RefreshToken);
            throw new SignInEndedException();
        }

        return answer.ToTokens(time.GetUtcNow(), refreshToken) with
        {
            ExpiresAt = ExpiryOf(answer),
            AccountLabel = current.AccountLabel,
            Details = current.Details,
            Scopes = answer.Scopes.Count > 0 ? answer.Scopes : current.Scopes,
        };
    }

    private async Task RevokeByCode(OpenAIPlanCodeOptions settings, string? refreshToken, CancellationToken cancellation)
    {
        if (refreshToken is not { Length: > 0 })
        {
            return;
        }

        var body = new JsonObject { ["token"] = refreshToken, ["token_type_hint"] = "refresh_token", ["client_id"] = settings.ClientId };
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.RevokeAddress) { Content = JsonContent(body) };
        using var response = await Send(request, cancellation);

        if (!response.IsSuccessStatusCode)
        {
            throw ModelProviderException.FromStatus((int)response.StatusCode);
        }
    }

    /// <summary>Revokes a refresh token by code that will not be kept. A courtesy to the provider: a failure is logged by kind and passes.</summary>
    private async Task AbandonByCode(OpenAIPlanCodeOptions settings, string? refreshToken)
    {
        try
        {
            await RevokeByCode(settings, refreshToken, CancellationToken.None);
        }
        catch (Exception failure) when (failure is ModelProviderException or HttpRequestException or IOException or OperationCanceledException)
        {
            logger.LogInformation("A ChatGPT plan session by code that is not kept could not be revoked: {Failure}", failure.GetType().Name);
        }
    }

    /// <summary>When the access token runs out: the answer's <c>expires_in</c>, else the token's own <c>exp</c>, else an hour.</summary>
    private DateTimeOffset ExpiryOf(TokenAnswer answer) =>
        answer.ExpiresIn is { } seconds
            ? time.GetUtcNow().AddSeconds(seconds)
            : PlanAccount.ExpiryOf(answer.AccessToken) ?? time.GetUtcNow().AddHours(1);

    /// <summary>A number of seconds the provider sent as a number or as text, or null when it sent none.</summary>
    private static TimeSpan? Seconds(JsonElement body, string name)
    {
        if (body.ValueKind is not JsonValueKind.Object || !body.TryGetProperty(name, out var value))
        {
            return null;
        }

        var seconds = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) => number,
            _ => 0,
        };

        return seconds is > 0 and <= 300 ? TimeSpan.FromSeconds(seconds) : null;
    }

    private static string? Text(JsonElement body, string name) =>
        body.ValueKind is JsonValueKind.Object && body.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;

    private static HttpRequestMessage Json(Uri address, JsonObject body) =>
        new(HttpMethod.Post, address) { Content = JsonContent(body) };

    private static StringContent JsonContent(JsonObject body) =>
        new(body.ToJsonString(), Encoding.UTF8, "application/json");

    #endregion

    #region Identity

    /// <summary>
    /// Checks the ID token against the keys the provider publishes, with nothing taken from the token itself to find
    /// them: only RS256, the configured issuer, the issued client id as audience, an expiry, and the attempt's nonce. A
    /// sign-in by code sends no nonce (the provider's device flow takes none), so it has none to check.
    /// </summary>
    private async Task<Identity> Validate(OpenAIPlanOptions settings, string? idToken, string clientId, string? nonce, CancellationToken cancellation)
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

        if (!result.IsValid || result.SecurityToken is not JsonWebToken token || (nonce is not null && !HasNonce(token, nonce)) || string.IsNullOrEmpty(token.Subject))
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
