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
public sealed record ResolvedToolEndpoint(ToolEndpoint? Endpoint, string? Problem, IReadOnlyList<string> Secrets);

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
    TimeProvider time,
    ILogger<ToolConnectionService> logger)
{
    #region Private Fields

    public const int MaxNameLength = 200;

    public const int MaxUrlLength = 2048;

    public const int MaxHeaderNameLength = 100;

    public const int MaxHeaderValueLength = 4096;

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

        // A header is a secret. Over http anyone on the way can read it, so it needs https. The one exception is a
        // space that may reach private addresses, where a tool server on the same machine or network is the point.
        if (request.AuthKind is ToolAuthKind.Header
            && new Uri(request.Url!.Trim()).Scheme is not "https"
            && !(await policy.GetEffective(spaceId, cancellationToken)).AllowPrivateAddresses)
        {
            return ToolConnectionChange.Invalid("A connection with a header needs an address that starts with https://, so the header is not sent where others can read it.");
        }

        var connection = new ToolConnection
        {
            Name = request.Name!.Trim(),
            Slug = await FreeSlug(request.Name!, cancellationToken),
            Url = request.Url!.Trim(),
            AuthKind = request.AuthKind,
            CredentialScope = CredentialScope.Space,
            Trust = request.Trust,
            ProtocolMode = request.ProtocolMode,
        };
        connections.Add(connection);

        if (request.AuthKind is ToolAuthKind.Header)
        {
            await StoreHeader(connection, request.HeaderName!, request.HeaderValue!, cancellationToken);
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

        if (connection.ProtocolMode != protocolMode)
        {
            // What was negotiated under the old setting says nothing about the new one.
            connection.ProtocolMode = protocolMode;
            connection.NegotiatedProtocol = null;
        }

        await work.Commit(cancellationToken);
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

        List<Guid> secrets = [.. (await connections.ListCredentials(connectionId, cancellationToken)).Select(credential => credential.SecretId)];
        await connections.Remove(connection, cancellationToken);

        foreach (var secretId in secrets)
        {
            await vault.Delete(secretId, cancellationToken);
        }

        await work.Commit(cancellationToken);
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
        else
        {
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

        if (connection.AuthKind is not ToolAuthKind.Header)
        {
            return new ResolvedToolEndpoint(null, $"The {connection.Name} connection uses a sign-in this server cannot do yet.", []);
        }

        var (name, value) = HeaderSecret.Read(await vault.Reveal(credential.SecretId, cancellationToken));
        return new ResolvedToolEndpoint(
            endpoint with { Headers = new Dictionary<string, string> { [name] = value }, CredentialKey = credential.Id },
            null,
            [value]);
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
            return (null, ToolSourceFailure.Unauthorized, resolved.Problem ?? "This server has no adapter for tool connections.");
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

        return request.AuthKind switch
        {
            ToolAuthKind.None => null,
            ToolAuthKind.Header => ValidateHeader(request.HeaderName, request.HeaderValue),
            _ => "Signing in to a tool server is not available yet. Use a header, or no credential.",
        };
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
