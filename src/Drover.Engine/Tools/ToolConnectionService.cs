namespace Drover.Engine.Tools;

/// <summary>What a person asks for when they add a tool connection. The header value is a secret and goes to the vault.</summary>
public sealed record NewToolConnection(string? Name, string? Url)
{
    #region Properties

    public ToolAuthKind AuthKind { get; init; } = ToolAuthKind.None;

    public string? HeaderName { get; init; }

    public string? HeaderValue { get; init; }

    public TrustLevel Trust { get; init; } = TrustLevel.Untrusted;

    public ProtocolMode ProtocolMode { get; init; } = ProtocolMode.Automatic;

    /// <summary>For a sign-in: a client id the tool server's operator gave out. Without one, this server names itself.</summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// The address of the sign-in service that gave out <see cref="ClientId"/>. Required with a client id: the
    /// client id and its secret are that service's, and go to no other.
    /// </summary>
    public string? Issuer { get; init; }

    /// <summary>The secret that goes with <see cref="ClientId"/>, when there is one. It goes to the vault.</summary>
    public string? ClientSecret { get; init; }

    /// <summary>For a sign-in: whether each person signs in themself (the default) or one sign-in serves the whole space.</summary>
    public CredentialScope? CredentialScope { get; init; }

    #endregion

    #region Overrides

    /// <summary>Says nothing: a record would otherwise print the header value and the client secret.</summary>
    public override string ToString() => nameof(NewToolConnection);

    #endregion
}

/// <summary>A change to a tool connection: the connection, or why nothing changed. Neither set means it was not found.</summary>
public sealed record ToolConnectionChange(ToolConnection? Connection, string? Problem)
{
    public static ToolConnectionChange NotFound { get; } = new(null, null);

    public static ToolConnectionChange Invalid(string problem) => new(null, problem);
}

/// <summary>
/// What asking a tool server for its tools came to. With a failure, the catalog is as it was and
/// <see cref="Message"/> says why in plain words.
/// </summary>
public sealed record ToolRefresh(ToolConnection Connection, IReadOnlyList<CatalogTool> Tools, ToolSourceFailure? Failure, string? Message, CatalogChange? Change);

/// <summary>A tool server as one call reaches it, built from rows read as the caller. Null endpoint means it cannot be called, and why.</summary>
public sealed record ResolvedToolEndpoint(ToolEndpoint? Endpoint, string? Problem, IReadOnlyCollection<string> Secrets);

/// <summary>
/// Tool connections of a space: adding one, keeping its credential in the vault, asking the server for its tools,
/// and removing it. Every method opens its own tenant scope and closes it before any wait on the tool server: no
/// transaction is ever open across a network call.
/// </summary>
public sealed class ToolConnectionService(
    ToolConnectionRepository connections,
    ToolCatalog catalog,
    ISecretVault vault,
    IEnumerable<IToolSource> sources,
    PolicyService policy,
    TenantScope scope,
    TenantContext tenant,
    IServiceScopeFactory scopes,
    IOptions<ToolSignInOptions> signInOptions,
    TimeProvider time,
    ILogger<ToolConnectionService> logger)
{
    #region Private Fields

    public const int MaxNameLength = 200;

    public const int MaxUrlLength = 2048;

    public const int MaxHeaderNameLength = 100;

    public const int MaxHeaderValueLength = 4096;

    public const int MaxClientIdLength = 2048;

    public const int MaxClientSecretLength = 4096;

    private const int MaxSlugLength = 24;

    private const int MaxErrorLength = 500;

    // Headers a connection may not set: they belong to the transport, or would redirect or disguise the request.
    private static readonly string[] reservedHeaders =
        ["host", "content-length", "content-type", "transfer-encoding", "connection", "cookie", "accept", "mcp-session-id", "mcp-protocol-version"];

    #endregion

    #region Reads

    public async Task<List<ToolConnection>> ListConnections(CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        return await connections.ListConnections(cancellationToken);
    }

    public async Task<ToolConnection?> FindConnection(Guid connectionId, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        return await connections.FindConnection(connectionId, cancellationToken);
    }

    /// <summary>The tools the connection has listed, or null when the connection is not in this space.</summary>
    public async Task<List<CatalogTool>?> ListTools(Guid connectionId, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);

        if (await connections.FindConnection(connectionId, cancellationToken) is null)
        {
            return null;
        }

        var tools = await connections.ListTools(connectionId, cancellationToken);
        var waiting = await connections.ListToolsNeedingReview([.. tools.Select(tool => tool.Id)], cancellationToken);
        return [.. tools.Select(tool => new CatalogTool(tool, waiting.Contains(tool.Id)))];
    }

    /// <summary>
    /// A person has read a tool's changed definition and accepts it. Every grant on the tool counts as current again,
    /// so the tool is offered to the bots that were granted it, and the definition kept for comparison is dropped.
    /// Null when the tool is not one of this connection's.
    /// </summary>
    public async Task<CatalogTool?> ConfirmReview(Guid connectionId, Guid toolId, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        var tool = await connections.FindConnection(connectionId, cancellationToken) is null
            ? null
            : await connections.GetTool(connectionId, toolId, cancellationToken);

        if (tool is null)
        {
            return null;
        }

        var confirmed = await connections.ConfirmReview(toolId, cancellationToken);
        tool.ReviewedDefinition = null;
        await work.Commit(cancellationToken);
        logger.LogInformation("The changed definition of tool {ToolId} was reviewed; {Grants} grants are current again", toolId, confirmed);
        return new CatalogTool(tool, false);
    }

    #endregion

    #region Changes

    /// <summary>
    /// Saves the connection and, for a header connection, its credential: the value goes to the vault as a secret
    /// of the whole space and the row keeps only the secret's id. The caller lists its tools next with
    /// <see cref="Refresh"/>.
    /// </summary>
    public async Task<ToolConnectionChange> Create(NewToolConnection request, CancellationToken cancellationToken)
    {
        if (Validate(request) is { } problem)
        {
            return ToolConnectionChange.Invalid(problem);
        }

        await using var work = await scope.Begin(cancellationToken);
        var spaceId = tenant.SpaceId ?? throw new InvalidOperationException("No space is set for this request.");

        // A header is a secret, and so is a token. Over http anyone on the way can read it, so it needs https. The
        // one exception is a space that may reach private addresses, where a tool server on the same machine or
        // network is the point.
        if (request.AuthKind is not ToolAuthKind.None
            && new Uri(request.Url!.Trim()).Scheme is not "https"
            && !(await policy.GetEffective(spaceId, cancellationToken)).AllowPrivateAddresses)
        {
            return ToolConnectionChange.Invalid("A connection with a header or a sign-in needs an address that starts with https://, so the credential is not sent where others can read it.");
        }

        if (request.AuthKind is ToolAuthKind.Oauth && signInOptions.Value.RedirectUrl is null)
        {
            return ToolConnectionChange.Invalid(ToolSignInOptions.MissingPublicUrl);
        }

        var connection = new ToolConnection
        {
            Name = request.Name!.Trim(),
            Slug = await FreeSlug(request.Name!, cancellationToken),
            Url = request.Url!.Trim(),
            AuthKind = request.AuthKind,
            CredentialScope = request.AuthKind is ToolAuthKind.Oauth ? request.CredentialScope ?? CredentialScope.PerUser : CredentialScope.Space,
            Trust = request.Trust,
            ProtocolMode = request.ProtocolMode,
        };
        connections.Add(connection);

        if (request.AuthKind is ToolAuthKind.Header)
        {
            await StoreHeader(connection, request.HeaderName!, request.HeaderValue!, cancellationToken);
        }

        if (request.AuthKind is ToolAuthKind.Oauth && !string.IsNullOrWhiteSpace(request.ClientId))
        {
            connection.Oauth = new OAuthMetadata
            {
                ClientId = request.ClientId.Trim(),
                Issuer = request.Issuer!.Trim(),
                ClientSecretId = string.IsNullOrEmpty(request.ClientSecret)
                    ? null
                    : await vault.Store(SecretKind.OauthClientSecret, request.ClientSecret, null, cancellationToken),
            };
        }

        await work.Commit(cancellationToken);
        logger.LogInformation("Tool connection {ConnectionId} added with {AuthKind} credentials", connection.Id, connection.AuthKind);
        return new ToolConnectionChange(connection, null);
    }

    /// <summary>Replaces the header of a header connection. The old value is deleted from the vault.</summary>
    public async Task<ToolConnectionChange> ReplaceHeader(Guid connectionId, string? headerName, string? headerValue, CancellationToken cancellationToken)
    {
        if (ValidateHeader(headerName, headerValue) is { } problem)
        {
            return ToolConnectionChange.Invalid(problem);
        }

        await using var work = await scope.Begin(cancellationToken);
        var connection = await connections.GetConnection(connectionId, cancellationToken);

        if (connection is null)
        {
            return ToolConnectionChange.NotFound;
        }

        if (connection.AuthKind is not ToolAuthKind.Header)
        {
            return ToolConnectionChange.Invalid("This connection does not use a header.");
        }

        foreach (var old in await connections.ListCredentials(connectionId, cancellationToken))
        {
            await vault.Replace(old.SecretId, HeaderSecret.Write(headerName!, headerValue!), cancellationToken);
            old.Status = CredentialStatus.Active;
        }

        await work.Commit(cancellationToken);
        await Forget(connectionId, cancellationToken);
        logger.LogInformation("The header of tool connection {ConnectionId} was replaced", connectionId);
        return new ToolConnectionChange(connection, null);
    }

    /// <summary>Changes how much the connection's results are trusted and which protocol generation it may use.</summary>
    public async Task<ToolConnectionChange> Update(Guid connectionId, TrustLevel trust, ProtocolMode protocolMode, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        var connection = await connections.GetConnection(connectionId, cancellationToken);

        if (connection is null)
        {
            return ToolConnectionChange.NotFound;
        }

        connection.Trust = trust;
        var protocolChanged = connection.ProtocolMode != protocolMode;

        if (protocolChanged)
        {
            // What was negotiated under the old setting says nothing about the new one.
            connection.ProtocolMode = protocolMode;
            connection.NegotiatedProtocol = null;
        }

        await work.Commit(cancellationToken);

        if (protocolChanged)
        {
            await Forget(connectionId, cancellationToken);
        }

        return new ToolConnectionChange(connection, null);
    }

    /// <summary>Removes the connection, its tools, the grants on them, and its credentials, secrets included. False when it is not in this space.</summary>
    public async Task<bool> Remove(Guid connectionId, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        var connection = await connections.GetConnection(connectionId, cancellationToken);

        if (connection is null)
        {
            return false;
        }

        // The credentials this transaction can see: the space's and the caller's own. Another person's sign-in to a
        // connection where each signs in themself is invisible here; it stays in the vault, and is of no use, since
        // a removed connection is never resolved again.
        List<Guid> secrets = [.. (await connections.ListCredentials(connectionId, cancellationToken)).Select(credential => credential.SecretId)];

        if (connection.Oauth?.ClientSecretId is { } clientSecretId)
        {
            secrets.Add(clientSecretId);
        }

        await connections.Remove(connection, cancellationToken);

        foreach (var secretId in secrets)
        {
            await vault.Delete(secretId, cancellationToken);
        }

        await work.Commit(cancellationToken);
        await Forget(connectionId, cancellationToken);
        logger.LogInformation("Tool connection {ConnectionId} removed", connectionId);
        return true;
    }

    #endregion

    #region Listing Tools

    /// <summary>
    /// Asks the server for its tools and folds them into the catalog. When the server cannot answer, the catalog
    /// stays as it was and the connection records why. Null when the connection is not in this space.
    /// </summary>
    public async Task<ToolRefresh?> Refresh(Guid connectionId, CancellationToken cancellationToken)
    {
        ToolConnection? connection;
        ResolvedToolEndpoint resolved;
        EffectivePolicy effective;

        await using (var work = await scope.Begin(cancellationToken))
        {
            connection = await connections.FindConnection(connectionId, cancellationToken);

            if (connection is null)
            {
                return null;
            }

            resolved = await Resolve(connection, tenant.UserId ?? Audience.Everyone, cancellationToken);
            effective = await policy.GetEffective(connection.SpaceId, cancellationToken);
            await work.Commit(cancellationToken);
        }

        // No scope is open from here until the answer is stored.
        var (listing, failure, message) = await Ask(connection, resolved, effective, cancellationToken);

        return await Record(connectionId, listing, failure, message, cancellationToken);
    }

    /// <summary>
    /// Stores what a tool server answered when asked for its tools: the catalog and the connection's state after a
    /// listing, or why there is none. Opens its own tenant scope. Null when the connection is not in this space.
    /// </summary>
    public async Task<ToolRefresh?> Record(Guid connectionId, ToolListing? listing, ToolSourceFailure? failure, string? message, CancellationToken cancellationToken)
    {
        await using var storing = await scope.Begin(cancellationToken);
        var row = await connections.GetConnection(connectionId, cancellationToken);

        if (row is null)
        {
            return null;
        }

        CatalogChange? change = null;

        if (listing is not null)
        {
            change = await catalog.FoldServerTools(row, listing.Tools, policy.Deployment.MaxToolsPerConnection, cancellationToken);
            row.Status = ConnectionStatus.Ready;
            row.NegotiatedProtocol = listing.IsStateless ? $"stateless {listing.ProtocolVersion}" : $"session-based {listing.ProtocolVersion}";
            row.LastListedAt = time.GetUtcNow();
            row.LastError = null;
        }
        else if (failure is not ToolSourceFailure.SignInRequired)
        {
            // That one person has not signed in says nothing about the connection.
            row.Status = ConnectionStatus.Failed;
            row.LastError = message;
        }

        await storing.Commit(cancellationToken);
        var tools = await ListTools(connectionId, cancellationToken) ?? [];
        logger.LogInformation(
            "Tool connection {ConnectionId} listed: {Outcome}, {Added} added, {Changed} changed, {Missing} missing, {ForReview} grants for review",
            connectionId,
            failure?.ToString() ?? "ok",
            change?.Added ?? 0,
            change?.Changed ?? 0,
            change?.Missing ?? 0,
            change?.GrantsForReview ?? 0);
        return new ToolRefresh(row, tools, failure, message, change);
    }

    /// <summary>
    /// Builds what a call needs to reach the connection's server, as the caller: the credential is read in the
    /// caller's transaction, so the database returns a person's own credential or the space's, and nobody else's.
    /// Must be called inside a tenant scope. The secrets are returned so the caller can scrub them from results.
    /// </summary>
    public async Task<ResolvedToolEndpoint> Resolve(ToolConnection connection, Guid principalUserId, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(connection.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            return new ResolvedToolEndpoint(null, "The connection's address is not a web address.", []);
        }

        var endpoint = new ToolEndpoint(connection.Id, url)
        {
            Transport = connection.Transport,
            ProtocolMode = connection.ProtocolMode,
            Limits = new ToolServerLimits(policy.Deployment.MaxToolResponseBytes, policy.Deployment.MaxToolsPerConnection),
        };

        if (connection.AuthKind is ToolAuthKind.None)
        {
            return new ResolvedToolEndpoint(endpoint, null, []);
        }

        // One audience, by the connection's scope, and no second try with the other.
        var audience = connection.CredentialScope is CredentialScope.Space ? Audience.Everyone : principalUserId;
        var credential = await connections.GetCredential(connection.Id, audience, cancellationToken);

        if (credential is not { Status: CredentialStatus.Active })
        {
            return new ResolvedToolEndpoint(
                null,
                connection.CredentialScope is CredentialScope.Space
                    ? $"The {connection.Name} connection has no working credential. An admin must set it again."
                    : $"The {connection.Name} connection needs you to sign in.",
                []);
        }

        if (connection.AuthKind is ToolAuthKind.Oauth)
        {
            var signIn = await SignInFor(connection, principalUserId, cancellationToken);
            return signIn.SignIn is null
                ? new ResolvedToolEndpoint(null, signIn.Problem, [])
                : new ResolvedToolEndpoint(endpoint with { SignIn = signIn.SignIn, CredentialKey = credential.Id }, null, signIn.Secrets);
        }

        var (name, value) = HeaderSecret.Read(await vault.Reveal(credential.SecretId, cancellationToken));
        return new ResolvedToolEndpoint(
            endpoint with { Headers = new Dictionary<string, string> { [name] = value }, CredentialKey = credential.Id },
            null,
            [value]);
    }

    /// <summary>
    /// What a tool source needs to act with the tokens of one credential of a sign-in connection: who this server is
    /// to the tool server, and where the tokens are read and written. Must be called inside a tenant scope; the
    /// reading and writing happen later, in short transactions of their own as <paramref name="actingUserId"/>, so
    /// the database hands out that person's tokens or the space's and nobody else's. Nobody is asked to sign in.
    /// </summary>
    public async Task<(ToolSignIn? SignIn, string? Problem, IReadOnlyCollection<string> Secrets)> SignInFor(
        ToolConnection connection,
        Guid actingUserId,
        CancellationToken cancellationToken)
    {
        if (signInOptions.Value.RedirectUrl is not { } redirect)
        {
            return (null, ToolSignInOptions.MissingPublicUrl, []);
        }

        // A client id is one sign-in service's. Without knowing which, it could be handed to any the tool server names.
        Uri? issuer = null;

        if (connection.Oauth?.ClientId is not null && !Uri.TryCreate(connection.Oauth.Issuer, UriKind.Absolute, out issuer))
        {
            return (null, $"The {connection.Name} connection has a client id and no sign-in service to use it with. Remove it and add it again.", []);
        }

        var audience = connection.CredentialScope is CredentialScope.Space ? Audience.Everyone : actingUserId;
        var kept = new KeptToolTokens(new RunTenant(scopes, connection.SpaceId, actingUserId), connection.Id, audience);
        var clientSecret = connection.Oauth?.ClientSecretId is { } secretId ? await vault.Reveal(secretId, cancellationToken) : null;
        kept.Note(clientSecret);
        return (
            new ToolSignIn(redirect, kept.Read, kept.Write)
            {
                ClientId = connection.Oauth?.ClientId,
                ClientSecret = clientSecret,
                Issuer = issuer,
                ClientDocumentUrl = signInOptions.Value.ClientDocumentUrl,
            },
            null,
            kept.Secrets);
    }

    /// <summary>
    /// Records that a credential's tokens stopped working and could not be renewed, so the next use says "sign in
    /// again" at once and does not try the dead tokens. Opens its own tenant scope, as the person the run acts for.
    /// </summary>
    public async Task MarkNeedsReconnect(Guid connectionId, Guid credentialId, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        var credential = (await connections.ListCredentials(connectionId, cancellationToken)).SingleOrDefault(found => found.Id == credentialId);

        if (credential is not null)
        {
            credential.Status = CredentialStatus.NeedsReconnect;
            await work.Commit(cancellationToken);
            logger.LogInformation("The sign-in of tool connection {ConnectionId} needs to be made again", connectionId);
        }
    }

    /// <summary>
    /// Forgets the caller's sign-in to the connection, or the space's when one sign-in serves everyone: the tokens
    /// are deleted from the vault. False when there was none.
    /// </summary>
    public async Task<bool> SignOut(Guid connectionId, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        var connection = await connections.FindConnection(connectionId, cancellationToken);
        var userId = tenant.UserId ?? throw new InvalidOperationException("No user is set for this request.");
        var credential = connection is { AuthKind: ToolAuthKind.Oauth }
            ? await connections.GetCredential(connectionId, connection.CredentialScope is CredentialScope.Space ? Audience.Everyone : userId, cancellationToken)
            : null;

        if (credential is null)
        {
            return false;
        }

        connections.Remove(credential);
        await vault.Delete(credential.SecretId, cancellationToken);
        await work.Commit(cancellationToken);
        await Forget(connectionId, cancellationToken);
        logger.LogInformation("A sign-in of tool connection {ConnectionId} was removed", connectionId);
        return true;
    }

    /// <summary>
    /// For each sign-in connection, how the caller's own sign-in stands (or the space's, when one serves everyone).
    /// A connection with no entry has none.
    /// </summary>
    public async Task<Dictionary<Guid, CredentialStatus>> ListSignIns(IReadOnlyCollection<ToolConnection> listed, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        var userId = tenant.UserId ?? Audience.Everyone;
        var signIns = listed.Where(connection => connection.AuthKind is ToolAuthKind.Oauth).ToDictionary(connection => connection.Id);
        var credentials = await connections.ListCredentials([.. signIns.Keys], cancellationToken);
        return credentials
            .Where(credential => credential.AudienceUserId == (signIns[credential.ToolConnectionId].CredentialScope is CredentialScope.Space ? Audience.Everyone : userId))
            .ToDictionary(credential => credential.ToolConnectionId, credential => credential.Status);
    }

    /// <summary>Calls the server with no transaction open, under the space's outbound rule.</summary>
    private async Task<(ToolListing? Listing, ToolSourceFailure? Failure, string? Message)> Ask(
        ToolConnection connection,
        ResolvedToolEndpoint resolved,
        EffectivePolicy effective,
        CancellationToken cancellationToken)
    {
        if (resolved.Endpoint is null || sources.FirstOrDefault(source => source.Kind is ToolSourceKind.Mcp) is not { } source)
        {
            return (
                null,
                connection.AuthKind is ToolAuthKind.Oauth && resolved.Problem is not null ? ToolSourceFailure.SignInRequired : ToolSourceFailure.Unauthorized,
                resolved.Problem ?? "This server has no adapter for tool connections.");
        }

        using var attempt = OutboundAttempt.Begin(effective);

        try
        {
            return (await source.ListTools(resolved.Endpoint, cancellationToken), null, null);
        }
        catch (ToolSourceException failure)
        {
            logger.LogInformation("Tool connection {ConnectionId} could not list its tools: {Failure}", connection.Id, failure.Failure);
            return (null, failure.Failure, Shorten(failure.Message));
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Tells every tool source to let go of what it keeps open for the connection. Called after the change is
    /// committed, with no transaction open: from here on a call resolves the new state, and must not be served by a
    /// session that was opened under the old one.
    /// </summary>
    private async Task Forget(Guid connectionId, CancellationToken cancellationToken)
    {
        foreach (var source in sources)
        {
            await source.Forget(connectionId, cancellationToken);
        }
    }

    private async Task StoreHeader(ToolConnection connection, string name, string value, CancellationToken cancellationToken)
    {
        var secretId = await vault.Store(SecretKind.ToolHeader, HeaderSecret.Write(name, value), null, cancellationToken);
        connections.Add(new ToolConnectionCredential { ToolConnectionId = connection.Id, SecretId = secretId, AudienceUserId = Audience.Everyone });
    }

    private static string? Validate(NewToolConnection request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > MaxNameLength)
        {
            return $"Give the connection a name of up to {MaxNameLength} characters.";
        }

        if (string.IsNullOrWhiteSpace(request.Url) || request.Url.Length > MaxUrlLength
            || !Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(url.UserInfo))
        {
            return "Enter the server's web address, starting with https:// or http://, with no user name or password in it.";
        }

        // An address is stored as it is and shown to every member. A key in it ("?api_key=...") would be too, so
        // nothing after the path is taken. The message does not repeat the address.
        if (!string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment) || request.Url.Contains('?', StringComparison.Ordinal) || request.Url.Contains('#', StringComparison.Ordinal))
        {
            return "Enter the server's address with nothing after the path: no \"?\" and no \"#\". If the server wants a key, add it as a header, which is kept secret.";
        }

        return request.AuthKind switch
        {
            ToolAuthKind.None => null,
            ToolAuthKind.Header => ValidateHeader(request.HeaderName, request.HeaderValue),
            ToolAuthKind.Oauth => ValidateClient(request.ClientId, request.ClientSecret, request.Issuer, url),
            _ => "This server does not know that kind of credential.",
        };
    }

    private static string? ValidateClient(string? clientId, string? clientSecret, string? issuer, Uri connectionUrl)
    {
        var hasClient = !string.IsNullOrWhiteSpace(clientId);

        if (hasClient != !string.IsNullOrWhiteSpace(issuer))
        {
            return hasClient
                ? "A client id needs the address of the sign-in service that gave it out, so it is never handed to another."
                : "A sign-in service is entered together with the client id it gave out.";
        }

        // The service's address is https, or http only where the connection itself is.
        if (hasClient
            && (issuer!.Length > MaxUrlLength
                || !Uri.TryCreate(issuer.Trim(), UriKind.Absolute, out var service)
                || !(service.Scheme is "https" || (service.Scheme is "http" && connectionUrl.Scheme is "http"))
                || !string.IsNullOrEmpty(service.UserInfo) || !string.IsNullOrEmpty(service.Query) || !string.IsNullOrEmpty(service.Fragment)))
        {
            return "Enter the sign-in service's web address, starting with https://, with nothing after the path.";
        }

        if (clientId is { Length: > MaxClientIdLength } || clientId?.Any(char.IsControl) is true)
        {
            return $"Enter a client id of up to {MaxClientIdLength} characters, on one line, or leave it empty.";
        }

        if (!string.IsNullOrEmpty(clientSecret) && string.IsNullOrWhiteSpace(clientId))
        {
            return "A client secret needs the client id it belongs to.";
        }

        return clientSecret is { Length: > MaxClientSecretLength } || clientSecret?.Any(char.IsControl) is true
            ? $"Enter a client secret of up to {MaxClientSecretLength} characters, on one line."
            : null;
    }

    private static string? ValidateHeader(string? name, string? value)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxHeaderNameLength || !name.All(IsHeaderNameCharacter)
            || reservedHeaders.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return "Enter a header name such as Authorization or X-Api-Key.";
        }

        return string.IsNullOrEmpty(value) || value.Length > MaxHeaderValueLength || value.Any(char.IsControl)
            ? $"Enter a header value of up to {MaxHeaderValueLength} characters, on one line."
            : null;
    }

    private static bool IsHeaderNameCharacter(char character) => char.IsAsciiLetterOrDigit(character) || character is '-' or '_';

    /// <summary>
    /// A short name for the connection, made of letters, digits, and underscores, that no other connection in the
    /// space has. It prefixes the names the model sees, so one server cannot take another's names or a built-in's.
    /// </summary>
    private async Task<string> FreeSlug(string name, CancellationToken cancellationToken)
    {
        var letters = new string([.. name.Trim().ToLowerInvariant().Select(character => char.IsAsciiLetterOrDigit(character) ? character : '_')]);
        var stem = letters.Trim('_');
        stem = stem.Length == 0 ? "server" : stem[..Math.Min(stem.Length, MaxSlugLength)];

        while (stem.Contains("__", StringComparison.Ordinal))
        {
            stem = stem.Replace("__", "_", StringComparison.Ordinal);
        }

        var slug = stem;

        for (var suffix = 2; await connections.IsSlugTaken(slug, cancellationToken); suffix++)
        {
            slug = $"{stem}_{suffix}";
        }

        return slug;
    }

    private static string Shorten(string message) => message.Length <= MaxErrorLength ? message : message[..MaxErrorLength];

    #endregion
}

/// <summary>How a header credential is kept in the vault: the name and the value together, as one secret.</summary>
public static class HeaderSecret
{
    #region Public Methods

    public static string Write(string name, string value) => JsonSerializer.Serialize(new Stored(name.Trim(), value));

    public static (string Name, string Value) Read(string secret)
    {
        var stored = JsonSerializer.Deserialize<Stored>(secret) ?? throw new JsonException("The stored header is empty.");
        return (stored.Name, stored.Value);
    }

    #endregion

    #region Nested Types

    private sealed record Stored(string Name, string Value);

    #endregion
}
