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

/// <summary>What marking a model as reading pictures came to: the changed catalog row, or a sentence for the admin.</summary>
public sealed record ModelVisionChange(ModelCatalogEntry? Entry, string? Problem);

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
    ModelProviderCatalog catalog,
    PolicyService policy,
    AuditLog audit,
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

    private const string ModelSettingsChanged = "model.settings_changed";

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
        var provider = catalog.IsOffered(kind) ? catalog.Find(kind) : null;
        var problem = provider is null ? catalog.NotOffered(kind) : FindProblem(provider, name, baseUrl, apiKey, defaultModel);

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

    /// <summary>
    /// Records that a space admin says a model of a compatible connection reads pictures, or clears that. Only a
    /// compatible endpoint takes it: a provider that reports its own capabilities keeps its own value. Null when the
    /// connection or the model is not in this space's catalog; a sentence when the connection's kind takes no mark.
    /// </summary>
    public async Task<ModelVisionChange?> SetVision(Guid connectionId, string model, bool readsImages, CancellationToken cancellationToken)
    {
        if (await connections.FindConnection(connectionId, cancellationToken) is not { } connection)
        {
            return null;
        }

        if (connection.Kind != ModelConnectionKinds.Compatible)
        {
            return new ModelVisionChange(null, "Only the models of a compatible endpoint can be marked as reading pictures. This provider reports its own.");
        }

        if (await connections.GetCatalogEntryForUpdate(connectionId, model, cancellationToken) is not { } entry)
        {
            return null;
        }

        entry.OperatorCapabilities = ModelSettingsRules.Normalize(
            (entry.OperatorCapabilities ?? new()) with { Vision = readsImages ? true : null });
        logger.LogInformation("Model connection {ConnectionId} marked a model as reading pictures: {ReadsImages}", connectionId, readsImages);
        return new ModelVisionChange(entry, null);
    }

    /// <summary>
    /// Records what a space admin says one model can do and costs, on any connection. The request is the whole set:
    /// a member that is null is cleared, and the provider's value is in effect for it. A model the catalog does not
    /// have is added, which is how an admin adds a model on a server that lists none. Null when the connection is not
    /// in this space; a sentence when a value is wrong or the connection already holds as many models as it may.
    /// </summary>
    public async Task<ModelVisionChange?> SetSettings(
        Guid connectionId,
        string model,
        ModelCapabilityOverrides abilities,
        ModelPricing prices,
        string? currency,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        if (await connections.FindConnection(connectionId, cancellationToken) is null)
        {
            return null;
        }

        var name = model.Trim();

        if (name.Length > CatalogUpsert.MaxModelIdLength)
        {
            return new ModelVisionChange(null, "The model name is too long.");
        }

        var found = await GetOrAddSettingsEntry(connectionId, name, cancellationToken);

        if (found.Problem is { } refused)
        {
            return new ModelVisionChange(null, refused);
        }

        var stored = found.Entry!;
        var tidy = abilities with { ReasoningEfforts = abilities.ReasoningEfforts?.Select(effort => effort.Trim().ToLowerInvariant()).ToList() };
        var problem = ModelSettingsRules.FindProblem(tidy, tidy.Reasoning ?? stored.Capabilities.Reasoning)
            ?? ModelSettingsRules.FindProblem(prices, currency);

        if (problem is not null)
        {
            return new ModelVisionChange(null, problem);
        }

        var before = OperatorValues(stored);
        stored.OperatorCapabilities = ModelSettingsRules.Normalize(tidy);
        stored.OperatorPricing = HasPrice(prices) ? prices with { Currency = currency?.ToUpperInvariant() ?? stored.Pricing.Currency } : null;
        audit.Record(actorUserId, ModelSettingsChanged, AuditTargets.ModelConnection, connectionId, before, OperatorValues(stored), About(name));
        return new ModelVisionChange(stored, null);
    }

    /// <summary>
    /// Clears everything an admin set for one model, so the provider's values are in effect again. The row stays.
    /// Null when the connection or the model is not in this space's catalog.
    /// </summary>
    public async Task<ModelCatalogEntry?> ClearSettings(Guid connectionId, string model, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (await connections.FindConnection(connectionId, cancellationToken) is null
            || await connections.GetCatalogEntryForUpdate(connectionId, model.Trim(), cancellationToken) is not { } entry)
        {
            return null;
        }

        var before = OperatorValues(entry);
        entry.OperatorCapabilities = null;
        entry.OperatorPricing = null;
        audit.Record(actorUserId, ModelSettingsChanged, AuditTargets.ModelConnection, connectionId, before, OperatorValues(entry), About(model.Trim()));
        return entry;
    }

    /// <summary>
    /// Records what a space admin says a model of a compatible connection does with speech: writes it down, reads
    /// aloud, holds live calls. All three false clears the mark, and the next refresh goes back to what the model's
    /// name implies. Null when the connection or the model is not in this space's catalog; a sentence when the
    /// connection's kind takes no mark.
    /// </summary>
    public async Task<ModelVisionChange?> SetVoice(
        Guid connectionId,
        string model,
        bool speechToText,
        bool textToSpeech,
        bool realtimeVoice,
        CancellationToken cancellationToken)
    {
        if (await connections.FindConnection(connectionId, cancellationToken) is not { } connection)
        {
            return null;
        }

        if (connection.Kind != ModelConnectionKinds.Compatible)
        {
            return new ModelVisionChange(null, "Only the models of a compatible endpoint can be marked for speech. This provider reports its own.");
        }

        if (await connections.GetCatalogEntryForUpdate(connectionId, model, cancellationToken) is not { } entry)
        {
            return null;
        }

        entry.Capabilities = entry.Capabilities with
        {
            SpeechToText = speechToText,
            TextToSpeech = textToSpeech,
            RealtimeVoice = realtimeVoice,
            VoiceSetByOperator = speechToText || textToSpeech || realtimeVoice,
        };
        logger.LogInformation(
            "Model connection {ConnectionId} marked a model for speech: listens {Listens}, speaks {Speaks}, live {Live}",
            connectionId,
            speechToText,
            textToSpeech,
            realtimeVoice);
        return new ModelVisionChange(entry, null);
    }

    #endregion

    #region Provider Calls

    /// <summary>
    /// Asks the provider whether the connection works, records the answer and any balances it gave, and on a pass
    /// stocks the catalog, so the Models screen lists the provider's models without a Refresh of its own.
    /// </summary>
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

        if (connection is not null && outcome.Succeeded)
        {
            try
            {
                await StockCatalog(call, cancellationToken);
            }
            catch (Exception problem) when (problem is not OperationCanceledException)
            {
                // The test result is already given: a catalog that could not be written must not fail it.
                logger.LogWarning(problem, "The model catalog of {ConnectionId} could not be stored after its passing test", connectionId);
            }
        }

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

        var asked = await AskCatalog(call, cancellationToken);

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
        var filled = (await connections.ListCatalog(connectionId, cancellationToken))
            .Where(entry => entry.Capabilities.ContextTokens is not null)
            .Select(entry => entry.Model)
            .ToHashSet(StringComparer.Ordinal);
        await work.Commit(cancellationToken);

        var resolution = catalog.Resolve(connection.Kind);
        var provider = resolution.Provider;
        var address = provider?.FixedAddress ?? (Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out var parsed) ? parsed : null);
        var limits = new DiscoveryLimits(policy.Deployment.MaxModelListBytes, policy.Deployment.MaxModelsPerConnection);
        var endpoint = address is null ? null : new ModelEndpoint(connection.Kind, address) { Limits = limits, Filled = filled };
        return new PreparedCall(connection, provider, endpoint, key, effective, resolution.Refusal);
    }

    /// <summary>No scope is open here. The adapter's requests carry the space's outbound rule.</summary>
    private static async Task<Answer<T>> Ask<T>(
        PreparedCall call,
        Func<IModelClientProvider, string?, CancellationToken, Task<T>> ask,
        CancellationToken cancellationToken)
    {
        if (call.Provider is null || call.Endpoint is null)
        {
            return new Answer<T>(default, ModelFailure.Rejected, call.Refusal ?? "This server has no adapter for that connection.");
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

    /// <summary>Asks the provider for its models, with the deployment's cap on how many one connection may list.</summary>
    private async Task<Answer<IReadOnlyList<ModelInfo>>> AskCatalog(PreparedCall call, CancellationToken cancellationToken)
    {
        var asked = await Ask(call, (provider, key, token) => provider.ListModels(call.Endpoint!, key, token), cancellationToken);
        return asked.Value is { } models && models.Count > policy.Deployment.MaxModelsPerConnection
            ? new Answer<IReadOnlyList<ModelInfo>>(null, ModelFailure.Rejected, ModelProviderException.FromLimit("model list").Message)
            : asked;
    }

    /// <summary>
    /// Stocks the catalog after a passing test. A provider that cannot answer leaves the catalog as it was: the
    /// test's result was already given and does not change for the listing.
    /// </summary>
    private async Task StockCatalog(PreparedCall call, CancellationToken cancellationToken)
    {
        var asked = await AskCatalog(call, cancellationToken);

        if (asked.Value is { } listed)
        {
            await StoreCatalog(call, listed, cancellationToken);
            logger.LogInformation("Model connection {ConnectionId} listed {ModelCount} models on its test", call.Snapshot.Id, listed.Count);
        }
        else
        {
            logger.LogInformation("Model connection {ConnectionId} passed its test but could not list models: {Failure}", call.Snapshot.Id, asked.Failure);
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
        var connection = await connections.FindConnection(call.Snapshot.Id, cancellationToken);

        if (connection is null || HasChanged(connection, call.Snapshot))
        {
            return;
        }

        var existing = await connections.GetCatalogForUpdate(connection.Id, cancellationToken);

        foreach (var entry in CatalogUpsert.Apply(connection.Id, existing, models, time.GetUtcNow()))
        {
            connections.Add(entry);
        }

        await work.Commit(cancellationToken);
    }

    private static bool HasChanged(ModelConnection now, ModelConnection before) =>
        now.SecretId != before.SecretId || now.BaseUrl != before.BaseUrl;

    #endregion

    #region Model Settings

    /// <summary>The locked row of the model, or a new one when the catalog has none and has room for one.</summary>
    private async Task<(ModelCatalogEntry? Entry, string? Problem)> GetOrAddSettingsEntry(Guid connectionId, string model, CancellationToken cancellationToken)
    {
        if (await connections.GetCatalogEntryForUpdate(connectionId, model, cancellationToken) is { } existing)
        {
            return (existing, null);
        }

        if (await connections.CountCatalog(connectionId, cancellationToken) >= policy.Deployment.MaxModelsPerConnection)
        {
            return (null, $"This connection already holds {policy.Deployment.MaxModelsPerConnection} models, the most this server keeps for one connection.");
        }

        var added = new ModelCatalogEntry
        {
            ModelConnectionId = connectionId,
            Model = model,
            DisplayName = model,
            State = AvailabilityState.Available,
            LastSeenAt = time.GetUtcNow(),
        };
        connections.Add(added);
        return (added, null);
    }

    private static bool HasPrice(ModelPricing prices) =>
        prices.InputPerMillion is not null || prices.OutputPerMillion is not null || prices.CachedInputPerMillion is not null;

    /// <summary>What an admin set, one member to a name, so the audit row says which members changed.</summary>
    private static JsonObject OperatorValues(ModelCatalogEntry entry) =>
        AuditLog.Snapshot(new
        {
            entry.OperatorCapabilities?.ContextTokens,
            entry.OperatorCapabilities?.MaxOutputTokens,
            entry.OperatorCapabilities?.ToolCalling,
            entry.OperatorCapabilities?.Vision,
            entry.OperatorCapabilities?.Reasoning,
            entry.OperatorCapabilities?.ReasoningEfforts,
            entry.OperatorCapabilities?.StructuredOutput,
            entry.OperatorCapabilities?.ImageGeneration,
            entry.OperatorPricing?.InputPerMillion,
            entry.OperatorPricing?.OutputPerMillion,
            entry.OperatorPricing?.CachedInputPerMillion,
            PriceCurrency = entry.OperatorPricing?.Currency,
        });

    private static JsonObject About(string model) => new() { ["model"] = model };

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
        EffectivePolicy Policy,
        string? Refusal);

    #endregion
}
