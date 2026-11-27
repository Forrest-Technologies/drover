namespace Drover.Engine.Decisions;

/// <summary>The result of a change to the space's decision model. With neither a connection nor a problem, there is none.</summary>
public sealed record DecisionChange(DecisionConnection? Connection, string? Problem)
{
    public static DecisionChange NotFound { get; } = new(null, null);

    public static DecisionChange Invalid(string problem) => new(null, problem);
}

/// <summary>What the decision model screen reads: the connection, the rules it checks, the kinds on offer, and the server's rule.</summary>
public sealed record DecisionModelView(
    DecisionConnection? Connection,
    int JudgedRules,
    IReadOnlyList<IDecisionModelProvider> Kinds,
    bool JudgedRulesAllowed);

/// <summary>What a test found, and the connection as it stands after it.</summary>
public sealed record DecisionTest(ConnectionTestResult Result, DecisionConnection Connection);

/// <summary>
/// The space's one decision model: connected, re-keyed, tested and disconnected by an owner or admin. Set, re-key,
/// disconnect and read run in the tenant scope the endpoint filter opened. A test waits on the provider, so it opens
/// its own short scopes: read what is needed and close, ask the provider, then open a new scope to write, and write
/// nothing when the key, the kind, the address or the model changed meanwhile. The key goes to the vault and is never
/// returned, logged, or written to an audit entry.
/// </summary>
public sealed class DecisionConnectionService(
    DecisionConnectionRepository connections,
    ISecretVault vault,
    IEnumerable<IDecisionModelProvider> providers,
    PolicyService policy,
    AuditLog audit,
    TenantScope scope,
    TenantContext tenant,
    TimeProvider time,
    ILogger<DecisionConnectionService> logger)
{
    #region Private Fields

    public const int MaxKeyLength = 512;

    public const int MaxModelLength = 200;

    public const int MaxAddressLength = 2048;

    public const int MaxAccountLength = 200;

    public const string NotConnected = "This space has no decision model.";

    #endregion

    #region Public Methods

    /// <summary>The kinds this server offers, in registration order.</summary>
    public IReadOnlyList<IDecisionModelProvider> OfferedKinds() =>
        [.. providers.Where(provider => policy.Deployment.OffersDecisionModel(provider.Kind))];

    public async Task<DecisionModelView> Read(CancellationToken cancellationToken) =>
        new(
            await connections.FindConnection(cancellationToken),
            await connections.CountJudgedRules(cancellationToken),
            OfferedKinds(),
            policy.Deployment.AllowJudgedRules);

    /// <summary>
    /// Connects the space's decision model, or replaces the one it has. The connection is pending until it is tested.
    /// A key left out keeps the stored one only when the kind and where it points are unchanged: a key is never sent
    /// somewhere it was not given for.
    /// </summary>
    public async Task<DecisionChange> Set(SetDecisionModelRequest request, Guid actorUserId, CancellationToken cancellationToken)
    {
        var kind = request.Kind?.Trim() ?? string.Empty;

        if (providers.FirstOrDefault(provider => provider.Kind == kind) is not { } provider)
        {
            return DecisionChange.Invalid($"The kind {kind} is not available on this server.");
        }

        if (!policy.Deployment.OffersDecisionModel(kind))
        {
            return DecisionChange.Invalid($"The operator of this server has not offered {provider.DisplayName} (deployment rule DecisionModelKinds).");
        }

        var existing = await connections.GetConnection(cancellationToken);
        var address = provider.AddressRule is DecisionAddressRule.Address ? request.Address?.Trim() : null;
        var account = provider.AddressRule is DecisionAddressRule.Account ? request.Account?.Trim() : null;
        var key = string.IsNullOrWhiteSpace(request.ApiKey) ? null : request.ApiKey.Trim();
        var keepsKey = key is null && existing is { KeySecretId: not null } && existing.Kind == kind && existing.Address == address && existing.Account == account;

        if ((FindProblem(provider, request, address, account, key, keepsKey) ?? await FindForbiddenAddress(provider, address, account, cancellationToken)) is { } problem)
        {
            return DecisionChange.Invalid(problem);
        }

        var before = existing is null ? null : Snapshot(existing);
        var connection = existing ?? new DecisionConnection { Kind = kind, Model = string.Empty, CreatedByUserId = actorUserId };
        var oldKey = keepsKey ? null : existing?.KeySecretId;
        connection.Kind = kind;
        connection.Address = address;
        connection.Account = account;
        connection.Model = request.Model!.Trim();
        connection.InputPricePerMillion = request.InputPricePerMillion;
        connection.KeySecretId = keepsKey ? existing!.KeySecretId : key is null ? null : await vault.Store(SecretKind.DecisionKey, key, null, cancellationToken);
        MarkUntested(connection);

        if (oldKey is { } old)
        {
            await vault.Delete(old, cancellationToken);
        }

        if (existing is null)
        {
            connections.Add(connection);
        }

        audit.Record(actorUserId, "decision_model.connected", AuditTargets.DecisionModel, connection.Id, before, Snapshot(connection));
        logger.LogInformation("Decision model {ConnectionId} of kind {Kind} set", connection.Id, kind);
        return new DecisionChange(connection, null);
    }

    /// <summary>Stores a new key as a new secret and deletes the old one. The connection is pending until it is tested again.</summary>
    public async Task<DecisionChange> ReplaceKey(string? apiKey, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (FindKeyProblem(apiKey) is { } problem)
        {
            return DecisionChange.Invalid(problem);
        }

        if (await connections.GetConnection(cancellationToken) is not { } connection)
        {
            return DecisionChange.NotFound;
        }

        var previous = connection.KeySecretId;
        connection.KeySecretId = await vault.Store(SecretKind.DecisionKey, apiKey!.Trim(), null, cancellationToken);

        if (previous is { } old)
        {
            await vault.Delete(old, cancellationToken);
        }

        MarkUntested(connection);
        audit.Record(actorUserId, "decision_model.key_replaced", AuditTargets.DecisionModel, connection.Id, null, null);
        logger.LogInformation("Decision model {ConnectionId} has a new key", connection.Id);
        return new DecisionChange(connection, null);
    }

    /// <summary>Removes the space's decision model and its key. False when there is none.</summary>
    public async Task<bool> Disconnect(Guid actorUserId, CancellationToken cancellationToken)
    {
        if (await connections.GetConnection(cancellationToken) is not { } connection)
        {
            return false;
        }

        if (connection.KeySecretId is { } key)
        {
            await vault.Delete(key, cancellationToken);
        }

        connections.Remove(connection);
        audit.Record(actorUserId, "decision_model.disconnected", AuditTargets.DecisionModel, connection.Id, Snapshot(connection), null);
        logger.LogInformation("Decision model {ConnectionId} disconnected", connection.Id);
        return true;
    }

    /// <summary>
    /// Asks the provider the one fixed question. Records ready, or failed with the mapped reason; a rate limit leaves
    /// the status as it was. Null when the space has no decision model. No scope is open while the provider answers.
    /// </summary>
    public async Task<DecisionTest?> Test(CancellationToken cancellationToken)
    {
        if (await ReadForTest(cancellationToken) is not { } read)
        {
            return null;
        }

        var result = await Ask(read, cancellationToken);
        var connection = await Record(read.Connection, result, cancellationToken) ?? read.Connection;
        logger.LogInformation("Decision model {ConnectionId} tested: succeeded {Succeeded}, failure {Failure}", read.Connection.Id, result.Succeeded, result.Failure);
        return new DecisionTest(result, connection);
    }

    #endregion

    #region Validation

    private static string? FindProblem(
        IDecisionModelProvider provider,
        SetDecisionModelRequest request,
        string? address,
        string? account,
        string? key,
        bool keepsKey)
    {
        if (string.IsNullOrWhiteSpace(request.Model) || request.Model.Trim().Length > MaxModelLength)
        {
            return $"Name the model in up to {MaxModelLength} characters.";
        }

        if (provider.AddressRule is DecisionAddressRule.Address && FindAddressProblem(address) is { } addressProblem)
        {
            return addressProblem;
        }

        if (provider.AddressRule is DecisionAddressRule.Account
            && (string.IsNullOrWhiteSpace(account) || account.Length > MaxAccountLength || account.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_'))))
        {
            return $"Enter the {provider.DisplayName} account id, letters and digits only.";
        }

        if (key is not null && FindKeyProblem(key) is { } keyProblem)
        {
            return keyProblem;
        }

        if (provider.KeyRule is ModelKeyRule.Required && key is null && !keepsKey)
        {
            return $"{provider.DisplayName} needs a key. Paste it to connect.";
        }

        return request.InputPricePerMillion is < 0 or > 1_000_000 ? "Write the price of a million input tokens as a number from 0." : null;
    }

    private static string? FindAddressProblem(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Length > MaxAddressLength
            || !Uri.TryCreate(address, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https"))
        {
            return "Enter the address of the server, starting with http:// or https://.";
        }

        return parsed.UserInfo.Length > 0 || parsed.Query.Length > 0 || parsed.Fragment.Length > 0
            ? "Leave credentials, a query, and a fragment out of the address. Enter the key separately."
            : null;
    }

    private static string? FindKeyProblem(string? apiKey) =>
        string.IsNullOrWhiteSpace(apiKey) || apiKey.Trim().Length > MaxKeyLength || apiKey.Trim().Any(character => char.IsWhiteSpace(character) || char.IsControl(character))
            ? "Paste the key without spaces or line breaks."
            : null;

    /// <summary>
    /// A literal private address can be refused now. A name cannot: it is checked each time a call is made, because
    /// what it resolves to can change.
    /// </summary>
    private async Task<string?> FindForbiddenAddress(IDecisionModelProvider provider, string? address, string? account, CancellationToken cancellationToken)
    {
        if (provider.AddressFor(address, account) is not { } target
            || !IPAddress.TryParse(target.IdnHost, out var literal)
            || !PrivateAddresses.Contains(literal))
        {
            return null;
        }

        var spaceId = tenant.SpaceId ?? throw new InvalidOperationException("No space is set for this request.");
        return (await policy.GetEffective(spaceId, cancellationToken)).AllowPrivateAddresses
            ? null
            : "This server does not allow connections to private addresses, and that address is one. Use a public address, or ask the operator to allow private addresses.";
    }

    #endregion

    #region Testing

    /// <summary>First scope: the connection, its key, and the space's rule on private addresses. Closed when this returns.</summary>
    private async Task<ReadConnection?> ReadForTest(CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);

        if (await connections.FindConnection(cancellationToken) is not { } connection)
        {
            return null;
        }

        var spaceId = tenant.SpaceId ?? throw new InvalidOperationException("No space is set for this request.");
        var effective = await policy.GetEffective(spaceId, cancellationToken);
        var key = connection.KeySecretId is { } secret ? await vault.Reveal(secret, cancellationToken) : null;
        await work.Commit(cancellationToken);
        return new ReadConnection(connection, key, effective);
    }

    /// <summary>No scope is open here. The adapter's requests carry the space's outbound rule.</summary>
    private async Task<ConnectionTestResult> Ask(ReadConnection read, CancellationToken cancellationToken)
    {
        var connection = read.Connection;

        if (providers.FirstOrDefault(provider => provider.Kind == connection.Kind) is not { } provider)
        {
            return ConnectionTestResult.Failed(ModelFailure.Rejected, "This server has no adapter for that kind of decision model.");
        }

        if (!policy.Deployment.OffersDecisionModel(provider.Kind))
        {
            return ConnectionTestResult.Failed(ModelFailure.Rejected, $"The operator of this server has not offered {provider.DisplayName} (deployment rule DecisionModelKinds).");
        }

        if (provider.AddressFor(connection.Address, connection.Account) is not { } address)
        {
            return ConnectionTestResult.Failed(ModelFailure.Rejected, "The connection does not say where its provider is. Connect it again.");
        }

        using var attempt = OutboundAttempt.Begin(read.Policy);
        return await provider.TestConnection(new ModelEndpoint(provider.Kind, address), read.Key, connection.Model, cancellationToken);
    }

    /// <summary>
    /// Second scope: write the outcome. A connection that changed while the provider answered is another connection,
    /// so nothing is written about it.
    /// </summary>
    private async Task<DecisionConnection?> Record(DecisionConnection asked, ConnectionTestResult result, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);

        if (await connections.GetConnection(cancellationToken) is not { } connection || HasChanged(connection, asked))
        {
            return null;
        }

        if (result.Succeeded || result.Failure is not ModelFailure.RetryLater)
        {
            connection.Status = result.Succeeded ? ConnectionStatus.Ready : ConnectionStatus.Failed;
            connection.LastError = result.Succeeded ? null : result.Message;
        }

        connection.TestedAt = time.GetUtcNow();
        await work.Commit(cancellationToken);
        return connection;
    }

    private static bool HasChanged(DecisionConnection now, DecisionConnection asked) =>
        now.Id != asked.Id || now.KeySecretId != asked.KeySecretId || now.Kind != asked.Kind
        || now.Address != asked.Address || now.Account != asked.Account || now.Model != asked.Model;

    #endregion

    #region Private Methods

    private static void MarkUntested(DecisionConnection connection)
    {
        connection.Status = ConnectionStatus.Pending;
        connection.TestedAt = null;
        connection.LastError = null;
    }

    // What an audit entry keeps: never the key or its secret's id.
    private static JsonObject Snapshot(DecisionConnection connection) =>
        AuditLog.Snapshot(new
        {
            connection.Kind,
            connection.Address,
            connection.Account,
            connection.Model,
            HasKey = connection.KeySecretId is not null,
            connection.InputPricePerMillion,
        });

    #endregion

    #region Nested Types

    private sealed record ReadConnection(DecisionConnection Connection, string? Key, EffectivePolicy Policy);

    #endregion
}
