namespace Drover.Tests.Common;

/// <summary>
/// The sign-in side of a test tool server: the documents a client discovers it by, an approval page that always
/// approves, a token endpoint, and client registration. Every code and token is made at random when it is issued,
/// so no credential is written anywhere.
/// </summary>
public sealed class SignInDesk(ToolServerSignIn spec)
{
    #region Private Fields

    private readonly ConcurrentDictionary<string, Grant> codes = new();

    private readonly ConcurrentDictionary<string, DateTimeOffset> accessTokens = new();

    private readonly ConcurrentDictionary<string, string> refreshTokens = new();

    private readonly ConcurrentDictionary<string, bool> clients = new();

    private readonly ConcurrentQueue<string> clientsSeen = new();

    private readonly ConcurrentQueue<string> issued = new();

    private int approvals;

    private int refreshes;

    private int registrations;

    private int tokenRequests;

    private int failRenewalsWith;

    private static readonly string[] scopes = ["tools"];

    private static readonly string[] grantTypes = ["authorization_code", "refresh_token"];

    private static readonly string[] responseTypes = ["code"];

    private static readonly string[] challengeMethods = ["S256"];

    private static readonly string[] clientAuthentication = ["none", "client_secret_post"];

    #endregion

    #region Properties

    public int Approvals => Volatile.Read(ref approvals);

    public int Refreshes => Volatile.Read(ref refreshes);

    public int Registrations => Volatile.Read(ref registrations);

    public int TokenRequests => Volatile.Read(ref tokenRequests);

    public IReadOnlyList<string> ClientsSeen => [.. clientsSeen];

    /// <summary>Every code and token this desk ever handed out, so a test can look for them where they must not be.</summary>
    public IReadOnlyList<string> Issued => [.. issued];

    #endregion

    #region Public Methods

    public static bool Owns(PathString path) =>
        path.StartsWithSegments("/.well-known", StringComparison.Ordinal) || path.StartsWithSegments("/desk", StringComparison.Ordinal);

    /// <summary>Whether the request carries an access token this desk issued and has not forgotten.</summary>
    public bool Admits(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.Ordinal)
            && accessTokens.TryGetValue(header["Bearer ".Length..], out var expires)
            && expires > DateTimeOffset.UtcNow;
    }

    public static void Challenge(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{Origin(context.Request)}/.well-known/oauth-protected-resource\"";
    }

    public void FailRenewals(int? status) => Volatile.Write(ref failRenewalsWith, status ?? 0);

    public void ExpireAccessTokens() => accessTokens.Clear();

    public void WithdrawConsent()
    {
        accessTokens.Clear();
        refreshTokens.Clear();
    }

    public void Map(WebApplication app)
    {
        app.MapGet("/.well-known/oauth-protected-resource/{**rest}", DescribeResource);
        app.MapGet("/.well-known/oauth-authorization-server/{**rest}", DescribeDesk);
        app.MapPost("/desk/token", IssueTokens);
        app.MapPost("/desk/register", Register);
    }

    #endregion

    #region Private Methods

    private static string Origin(HttpRequest request) => $"{request.Scheme}://{request.Host}";

    private static Task Write(HttpContext context, int status, Dictionary<string, object> body)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsync(JsonSerializer.Serialize(body), context.RequestAborted);
    }

    private static Task Refuse(HttpContext context, string error) =>
        Write(context, StatusCodes.Status400BadRequest, new Dictionary<string, object> { ["error"] = error });

    private string NewToken()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        issued.Enqueue(token);
        return token;
    }

    private static Task DescribeResource(HttpContext context) =>
        Write(
            context,
            StatusCodes.Status200OK,
            new Dictionary<string, object>
            {
                ["resource"] = Origin(context.Request),
                ["authorization_servers"] = new[] { Origin(context.Request) },
                ["scopes_supported"] = scopes,
            });

    private Task DescribeDesk(HttpContext context)
    {
        var origin = Origin(context.Request);
        Dictionary<string, object> document = new()
        {
            ["issuer"] = origin,
            ["authorization_endpoint"] = $"{origin}/desk/authorize",
            ["token_endpoint"] = $"{origin}/desk/token",
            ["response_types_supported"] = responseTypes,
            ["grant_types_supported"] = grantTypes,
            ["code_challenge_methods_supported"] = challengeMethods,
            ["token_endpoint_auth_methods_supported"] = clientAuthentication,
            ["authorization_response_iss_parameter_supported"] = true,
            ["client_id_metadata_document_supported"] = spec.ReadsClientDocuments,
            ["scopes_supported"] = scopes,
        };

        if (spec.RegistersClients)
        {
            document["registration_endpoint"] = $"{origin}/desk/register";
        }

        return Write(context, StatusCodes.Status200OK, document);
    }

    private bool Knows(string clientId) =>
        clientId == spec.KnownClientId
        || clients.ContainsKey(clientId)
        || (spec.ReadsClientDocuments && clientId.StartsWith("https://", StringComparison.Ordinal));

    /// <summary>The approval page with a person who always approves. Refuses, as the page would, a request it cannot honor.</summary>
    public SignInAnswer Approve(Uri authorizationUrl)
    {
        var query = authorizationUrl.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair.Length > 1 ? pair[1].Replace('+', ' ') : string.Empty));
        var clientId = query.GetValueOrDefault("client_id", string.Empty);
        var redirect = query.GetValueOrDefault("redirect_uri", string.Empty);
        var challenge = query.GetValueOrDefault("code_challenge", string.Empty);
        clientsSeen.Enqueue(clientId);

        if (authorizationUrl.AbsolutePath != "/desk/authorize" || !Knows(clientId) || redirect.Length == 0 || challenge.Length == 0
            || query.GetValueOrDefault("code_challenge_method") != "S256" || query.GetValueOrDefault("response_type") != "code")
        {
            throw new InvalidOperationException("The approval page refused the request.");
        }

        var code = NewToken();
        codes[code] = new Grant(clientId, redirect, challenge);
        Interlocked.Increment(ref approvals);
        return new SignInAnswer(code, query.GetValueOrDefault("state"), authorizationUrl.GetLeftPart(UriPartial.Authority));
    }

    private async Task IssueTokens(HttpContext context)
    {
        Interlocked.Increment(ref tokenRequests);
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var clientId = form["client_id"].ToString();

        if (form["grant_type"] == "refresh_token" && Volatile.Read(ref failRenewalsWith) is var failing and not 0)
        {
            // Down, not refusing: the refresh token is not spent and is as good afterwards as before.
            context.Response.StatusCode = failing;
            return;
        }

        switch (form["grant_type"].ToString())
        {
            case "authorization_code":
                if (!codes.TryRemove(form["code"].ToString(), out var grant)
                    || grant.ClientId != clientId
                    || grant.RedirectUrl != form["redirect_uri"].ToString()
                    || grant.Challenge != Challenge(form["code_verifier"].ToString()))
                {
                    await Refuse(context, "invalid_grant");
                    return;
                }

                break;

            case "refresh_token":
                // A refresh token works once. Two clients refreshing with the same one cannot both succeed.
                if (!refreshTokens.TryRemove(form["refresh_token"].ToString(), out var owner) || owner != clientId)
                {
                    await Refuse(context, "invalid_grant");
                    return;
                }

                Interlocked.Increment(ref refreshes);
                break;

            default:
                await Refuse(context, "unsupported_grant_type");
                return;
        }

        var access = NewToken();
        var refresh = NewToken();
        accessTokens[access] = DateTimeOffset.UtcNow + spec.TokenLifetime;
        refreshTokens[refresh] = clientId;
        await Write(
            context,
            StatusCodes.Status200OK,
            new Dictionary<string, object>
            {
                ["access_token"] = access,
                ["token_type"] = "Bearer",
                ["expires_in"] = (int)spec.TokenLifetime.TotalSeconds,
                ["refresh_token"] = refresh,
                ["scope"] = "tools",
            });
    }

    private async Task Register(HttpContext context)
    {
        if (!spec.RegistersClients)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        using var request = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
        // A client id is a name, not a credential, so it is not among the secrets this desk remembers issuing.
        var clientId = $"registered-{Guid.CreateVersion7():N}";
        clients[clientId] = true;
        Interlocked.Increment(ref registrations);
        await Write(
            context,
            StatusCodes.Status201Created,
            new Dictionary<string, object>
            {
                ["client_id"] = clientId,
                ["redirect_uris"] = request.RootElement.GetProperty("redirect_uris").Clone(),
                ["token_endpoint_auth_method"] = "none",
                ["grant_types"] = grantTypes,
                ["response_types"] = responseTypes,
            });
    }

    private static string Challenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    #endregion

    #region Nested Types

    private sealed record Grant(string ClientId, string RedirectUrl, string Challenge);

    #endregion
}
