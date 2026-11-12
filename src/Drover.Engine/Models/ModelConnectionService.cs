namespace Drover.Engine.Models;

/// <summary>
/// What a change to a connection came to. Both members null means the connection does not exist in this space;
/// a problem is a sentence for the person who made the request.
/// </summary>
public sealed record ConnectionChange(ModelConnection? Connection, string? Problem)
{
    public static ConnectionChange NotFound { get; } = new(null, null);

    public static ConnectionChange Invalid(string problem) => new(null, problem);
}

/// <summary>A test or a refresh that reached, or tried to reach, the provider.</summary>
public sealed record ProviderCheck<T>(T? Result, ModelFailure? Failure, string Message, ModelConnection Connection);

/// <summary>What a refresh found: why the provider could not answer, if it could not, and the stored catalog.</summary>
public sealed record ModelRefresh(ModelFailure? Failure, string? Message, IReadOnlyList<ModelCatalogEntry> Catalog);

/// <summary>
/// Model connections: create, replace the key, test, and discover models. Create, replace the key, and the reads
/// run in the tenant scope the endpoint filter opened. Test and refresh wait on the provider, so they open their
/// own short scopes: read what is needed and close, call the provider, then open a new scope to write. The key
/// is revealed only to be handed to the adapter, and nothing here logs or returns it.
/// </summary>
public sealed class ModelConnectionService(
    ModelConnectionRepository connections,
    ISecretVault vault,
    IEnumerable<IModelClientProvider> providers,
    PolicyService policy,
    TenantScope scope,
    TenantContext tenant,
    TimeProvider time,
    ILogger<ModelConnectionService> logger)
{
    #region Private Fields

    private const int MaxKeyLength = 512;

    private const int MaxStoreAttempts = 3;

    private const int MaxNameLength = 200;

    private const int MaxAddressLength = 2048;

    #endregion

    #region Reads

    public Task<List<ModelConnection>> ListConnections(CancellationToken cancellationToken) =>
        connections.ListConnections(cancellationToken);

    /// <summary>The stored catalog of one connection, or null when the connection does not exist in this space.</summary>
    public async Task<List<ModelCatalogEntry>?> ListModels(Guid connectionId, CancellationToken cancellationToken) =>
        await connections.FindConnection(connectionId, cancellationToken) is null
            ? null
            : await connections.ListCatalog(connectionId, cancellationToken);

    #endregion

    #region Create And Replace Key

    public async Task<ConnectionChange> Create(
        string kind,
        string name,
        string? baseUrl,
        string? apiKey,
        string? defaultModel,
        CancellationToken cancellationToken)
    {
        var provider = providers.FirstOrDefault(candidate => candidate.Kind == kind);
        var problem = provider is null ? "That kind of connection is not available on this server." : FindProblem(provider, name, baseUrl, apiKey, defaultModel);

        if (provider is null || problem is not null)
        {
            return ConnectionChange.Invalid(problem!);
        }

        if (provider.FixedAddress is null && await FindForbiddenAddress(baseUrl!, cancellationToken) is { } forbidden)
        {
            return ConnectionChange.Invalid(forbidden);
        }

        var key = apiKey?.Trim();
        var connection = new ModelConnection
        {
            Name = name.Trim(),
            Kind = provider.Kind,
            BaseUrl = provider.FixedAddress is null ? baseUrl!.Trim() : null,
            AuthKind = string.IsNullOrEmpty(key) ? ModelAuthKind.None : ModelAuthKind.ApiKey,
            DefaultModel = string.IsNullOrWhiteSpace(defaultModel) ? null : defaultModel.Trim(),
        };

        if (!string.IsNullOrEmpty(key))
        {
            connection.SecretId = await vault.Store(SecretKind.ModelKey, key, null, cancellationToken);
        }

        connections.Add(connection);
        return new ConnectionChange(connection, null);
    }

    /// <summary>Stores a new key and removes the old one. The connection goes back to pending until it is tested again.</summary>
    public async Task<ConnectionChange> ReplaceKey(Guid connectionId, string apiKey, CancellationToken cancellationToken)
    {
        if (FindKeyProblem(apiKey) is { } problem)
        {
            return ConnectionChange.Invalid(problem);
        }

        var connection = await connections.GetConnection(connectionId, cancellationToken);

        if (connection is null)
        {
            return ConnectionChange.NotFound;
        }

        var key = apiKey.Trim();

        // A new secret, not a changed one: a test still waiting on the provider can tell its key was replaced.
        var previous = connection.SecretId;
        connection.SecretId = await vault.Store(SecretKind.ModelKey, key, null, cancellationToken);

        if (previous is { } old)
        {
            await vault.Delete(old, cancellationToken);
        }

        connection.AuthKind = ModelAuthKind.ApiKey;
        connection.Status = ConnectionStatus.Pending;
        connection.Balance = null;
        connection.LastCheckedAt = null;
        return new ConnectionChange(connection, null);
    }

    #endregion

    #region Provider Calls

    /// <summary>Asks the provider whether the connection works, then records the answer and any balances it gave.</summary>
    public async Task<ProviderCheck<ConnectionTestResult>?> Test(Guid connectionId, CancellationToken cancellationToken)
    {
        var call = await Prepare(connectionId, cancellationToken);

        if (call is null)
        {
            return null;
        }

        var answer = await Ask(call, (provider, key, token) => provider.TestConnection(call.Endpoint!, key, token), cancellationToken);
        var outcome = answer.Value ?? ConnectionTestResult.Failed(answer.Failure ?? ModelFailure.Unreachable, answer.Message);
        var connection = await Record(call, outcome.Succeeded, outcome.Failure, outcome.Balances ?? [], cancellationToken);
        logger.LogInformation(
            "Model connection {ConnectionId} tested: succeeded {Succeeded}, failure {Failure}",
            connectionId,
            outcome.Succeeded,
            outcome.Failure);
        return new ProviderCheck<ConnectionTestResult>(outcome, outcome.Failure, outcome.Message, connection ?? call.Snapshot);
    }

    /// <summary>
    /// Asks the provider for its models and folds them into the catalog. When the provider cannot answer, the
    /// catalog stays as it was and the refresh says why. Either way it returns the stored catalog.
    /// </summary>
    public async Task<ModelRefresh?> RefreshModels(Guid connectionId, CancellationToken cancellationToken)
    {
        var call = await Prepare(connectionId, cancellationToken);

        if (call is null)
        {
            return null;
        }

        var asked = await Ask(call, (provider, key, token) => provider.ListModels(call.Endpoint!, key, token), cancellationToken);

        if (asked.Value is { } models && models.Count > policy.Deployment.MaxModelsPerConnection)
        {
            asked = new Answer<IReadOnlyList<ModelInfo>>(null, ModelFailure.Rejected, ModelProviderException.FromLimit("model list").Message);
        }

        if (asked.Value is { } listed)
        {
            await StoreCatalog(call, listed, cancellationToken);
            logger.LogInformation("Model connection {ConnectionId} listed {ModelCount} models", connectionId, listed.Count);
        }
        else
        {
            logger.LogInformation("Model connection {ConnectionId} could not list models: {Failure}", connectionId, asked.Failure);
        }

        await using var work = await scope.Begin(cancellationToken);
        var catalog = await connections.ListCatalog(connectionId, cancellationToken);
        await work.Commit(cancellationToken);
        return new ModelRefresh(asked.Failure, asked.Value is null ? asked.Message : null, catalog);
    }

    /// <summary>
    /// Records balances a provider reported, for example from the headers of a reply. Opens its own scope,
    /// because replies are handled outside a request.
    /// </summary>
    public async Task RecordBalances(Guid connectionId, IReadOnlyList<ModelBalance> balances, CancellationToken cancellationToken)
    {
        if (balances.Count == 0)
        {
            return;
        }

        await using var work = await scope.Begin(cancellationToken);

        if (await connections.GetConnection(connectionId, cancellationToken) is { } connection)
        {
            connection.Balance = new ConnectionBalance { Amounts = balances, ReadAt = time.GetUtcNow() };
            await work.Commit(cancellationToken);
        }
    }

    #endregion

    #region Provider Call Steps

    /// <summary>First scope: read the connection, reveal its key, and work out the space's rule. Then close.</summary>
    private async Task<PreparedCall?> Prepare(Guid connectionId, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        var connection = await connections.FindConnection(connectionId, cancellationToken);

        if (connection is null)
        {
            return null;
        }

        var spaceId = tenant.SpaceId ?? throw new InvalidOperationException("No space is set for this request.");
        var effective = await policy.GetEffective(spaceId, cancellationToken);
        var key = connection.SecretId is { } secretId ? await vault.Reveal(secretId, cancellationToken) : null;
        await work.Commit(cancellationToken);

        var provider = providers.FirstOrDefault(candidate => candidate.Kind == connection.Kind);
        var address = provider?.FixedAddress ?? (Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out var parsed) ? parsed : null);
        var limits = new DiscoveryLimits(policy.Deployment.MaxModelListBytes, policy.Deployment.MaxModelsPerConnection);
        var endpoint = address is null ? null : new ModelEndpoint(connection.Kind, address) { Limits = limits };
        return new PreparedCall(connection, provider, endpoint, key, effective);
    }

    /// <summary>No scope is open here. The adapter's requests carry the space's outbound rule.</summary>
    private static async Task<Answer<T>> Ask<T>(
        PreparedCall call,
        Func<IModelClientProvider, string?, CancellationToken, Task<T>> ask,
        CancellationToken cancellationToken)
    {
        if (call.Provider is null || call.Endpoint is null)
        {
            return new Answer<T>(default, ModelFailure.Rejected, "This server has no adapter for that connection.");
        }

        using var attempt = OutboundAttempt.Begin(call.Policy);

        try
        {
            return new Answer<T>(await ask(call.Provider, call.Key, cancellationToken), null, string.Empty);
        }
        catch (ModelProviderException failure)
        {
            return new Answer<T>(default, failure.Failure, failure.Message);
        }
    }

    /// <summary>
    /// Second scope: write the outcome. If the key or address changed while the provider was answering, the
    /// answer is about a connection that no longer exists in that form, so nothing is written.
    /// </summary>
    private async Task<ModelConnection?> Record(
        PreparedCall call,
        bool succeeded,
        ModelFailure? failure,
        IReadOnlyList<ModelBalance> balances,
        CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        var connection = await connections.GetConnection(call.Snapshot.Id, cancellationToken);

        if (connection is null || HasChanged(connection, call.Snapshot))
        {
            return null;
        }

        if (succeeded || failure is not ModelFailure.RetryLater)
        {
            connection.Status = succeeded ? ConnectionStatus.Ready : ConnectionStatus.Failed;
        }

        connection.LastCheckedAt = time.GetUtcNow();

        if (balances.Count > 0)
        {
            connection.Balance = new ConnectionBalance { Amounts = balances, ReadAt = connection.LastCheckedAt };
        }

        await work.Commit(cancellationToken);
        return connection;
    }

    /// <summary>
    /// Writes the catalog. Two refreshes of one connection can both find a model missing and both add it; the
    /// second save is refused by the primary key, so it starts again and finds the row the first one added.
    /// </summary>
    private async Task StoreCatalog(PreparedCall call, IReadOnlyList<ModelInfo> models, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await TryStoreCatalog(call, models, cancellationToken);
                return;
            }
            catch (Exception failure) when (ModelConnectionRepository.IsAlreadyStored(failure) && attempt < MaxStoreAttempts)
            {
                logger.LogInformation("Model catalog of {ConnectionId} changed while it was being written; reading it again", call.Snapshot.Id);
            }
        }
    }

    private async Task TryStoreCatalog(PreparedCall call, IReadOnlyList<ModelInfo> models, CancellationToken cancellationToken)
    {
        await using var work = await scope.Begin(cancellationToken);
        var connection = await connections.GetConnection(call.Snapshot.Id, cancellationToken);

        if (connection is null || HasChanged(connection, call.Snapshot))
        {
            return;
        }

        var existing = await connections.GetCatalog(connection.Id, cancellationToken);

        foreach (var entry in CatalogUpsert.Apply(connection.Id, existing, models, time.GetUtcNow()))
        {
            connections.Add(entry);
        }

        await work.Commit(cancellationToken);
    }

    private static bool HasChanged(ModelConnection now, ModelConnection before) =>
        now.SecretId != before.SecretId || now.BaseUrl != before.BaseUrl;

    #endregion

    #region Validation

    private static string? FindProblem(IModelClientProvider provider, string name, string? baseUrl, string? apiKey, string? defaultModel)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
        {
            return $"Give the connection a name of up to {MaxNameLength} characters.";
        }

        if (defaultModel is { Length: > CatalogUpsert.MaxModelIdLength })
        {
            return "The default model name is too long.";
        }

        if (provider.FixedAddress is null && FindAddressProblem(baseUrl) is { } address)
        {
            return address;
        }

        return string.IsNullOrWhiteSpace(apiKey) ? null : FindKeyProblem(apiKey);
    }

    /// <summary>
    /// A literal private address can be refused now. A name cannot: it is checked each time a connection is made,
    /// because what it resolves to can change.
    /// </summary>
    private async Task<string?> FindForbiddenAddress(string baseUrl, CancellationToken cancellationToken)
    {
        var host = new Uri(baseUrl.Trim()).IdnHost;

        if (!IPAddress.TryParse(host, out var literal) || !PrivateAddresses.Contains(literal))
        {
            return null;
        }

        var spaceId = tenant.SpaceId ?? throw new InvalidOperationException("No space is set for this request.");
        var effective = await policy.GetEffective(spaceId, cancellationToken);

        return effective.AllowPrivateAddresses
            ? null
            : "This server does not allow connections to private addresses, and that address is one. Use a public address, or ask the operator to allow private addresses.";
    }

    private static string? FindAddressProblem(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || baseUrl.Length > MaxAddressLength
            || !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var address)
            || address.Scheme is not ("http" or "https"))
        {
            return "Enter the address of the server, starting with http:// or https://.";
        }

        return address.UserInfo.Length > 0 || address.Query.Length > 0 || address.Fragment.Length > 0
            ? "Leave credentials, a query, and a fragment out of the address. Enter the key separately."
            : null;
    }

    private static string? FindKeyProblem(string apiKey) =>
        string.IsNullOrWhiteSpace(apiKey) || apiKey.Trim().Length > MaxKeyLength || apiKey.Trim().Any(character => char.IsWhiteSpace(character) || char.IsControl(character))
            ? "Paste the key without spaces or line breaks."
            : null;

    #endregion

    #region Nested Types

    private sealed record Answer<T>(T? Value, ModelFailure? Failure, string Message);

    private sealed record PreparedCall(
        ModelConnection Snapshot,
        IModelClientProvider? Provider,
        ModelEndpoint? Endpoint,
        string? Key,
        EffectivePolicy Policy);

    #endregion
}
