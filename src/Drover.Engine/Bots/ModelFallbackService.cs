namespace Drover.Engine.Bots;

/// <summary>Stores fallback model lists and describes the effective model chain for a bot.</summary>
public sealed class ModelFallbackService(
    BotRepository bots,
    ModelConnectionRepository connections,
    ToolRepository tools,
    AccountRepository accounts,
    AuditLog audit)
{
    #region Private Fields

    public const int MaxFallbacks = 5;

    #endregion

    #region Public Methods

    public async Task<string?> FindProblem(
        IReadOnlyList<ModelFallback> fallbacks,
        ModelFallback? primary,
        CancellationToken cancellationToken)
    {
        if (FindListProblem(fallbacks, primary) is { } listProblem)
        {
            return listProblem;
        }

        foreach (var fallback in fallbacks)
        {
            var model = fallback.Model.Trim();

            if (await connections.FindConnection(fallback.ConnectionId, cancellationToken) is null)
            {
                return "Choose fallback connections from this space.";
            }

            var catalog = await connections.FindCatalogEntry(fallback.ConnectionId, model, cancellationToken);

            if (catalog is null)
            {
                return $"The model {model} is not in that connection's catalog.";
            }

            if (catalog.State is AvailabilityState.Missing)
            {
                return $"The model {catalog.DisplayName} is missing from its provider.";
            }
        }

        return null;
    }

    public static string? FindListProblem(IReadOnlyList<ModelFallback> fallbacks, ModelFallback? primary)
    {
        if (fallbacks.Count > MaxFallbacks)
        {
            return $"Choose at most {MaxFallbacks} fallback models.";
        }

        HashSet<(Guid ConnectionId, string Model)> seen = [];

        foreach (var fallback in fallbacks)
        {
            var model = fallback.Model?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(model) || model.Length > BotService.MaxModelLength)
            {
                return "Choose a model for every fallback.";
            }

            var key = (fallback.ConnectionId, model);

            if (primary is not null && key == (primary.ConnectionId, primary.Model.Trim()))
            {
                return "A fallback model cannot repeat the primary model.";
            }

            if (!seen.Add(key))
            {
                return "A fallback model can appear only once.";
            }
        }

        return null;
    }

    public static IReadOnlyList<ModelFallback> ResolveFallbacks(ModelSettings settings, SpaceSettings? space) =>
        settings.Fallbacks.Count > 0
            ? settings.Fallbacks
            : settings.UseSpaceFallbacks
                ? space?.ModelFallbacks ?? []
                : [];

    public async Task<SpaceModelFallbacks?> GetSpaceFallbacks(Guid spaceId, CancellationToken cancellationToken) =>
        await accounts.GetSpace(spaceId, cancellationToken) is { } space
            ? new SpaceModelFallbacks(ToBodies(space.Settings.ModelFallbacks))
            : null;

    public async Task<(SpaceModelFallbacks? Fallbacks, string? Problem)> UpdateSpaceFallbacks(
        Guid spaceId,
        IReadOnlyList<ModelFallbackBody> requested,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var fallbacks = requested.Select(ToFallback).ToList();

        if (await FindProblem(fallbacks, null, cancellationToken) is { } problem)
        {
            return (null, problem);
        }

        if (await accounts.GetSpaceForUpdate(spaceId, cancellationToken) is not { } space)
        {
            return (null, null);
        }

        var before = AuditLog.Snapshot(new { space.Settings.ModelFallbacks });
        space.Settings = space.Settings with { ModelFallbacks = Normalize(fallbacks) };
        var after = AuditLog.Snapshot(new { space.Settings.ModelFallbacks });
        audit.Record(actorUserId, "space.updated", AuditTargets.Space, space.Id, before, after);
        return (new SpaceModelFallbacks(ToBodies(space.Settings.ModelFallbacks)), null);
    }

    public async Task<BotModelChain?> GetChain(Guid botId, CancellationToken cancellationToken)
    {
        if (await bots.FindBot(botId, cancellationToken) is not { Status: not BotStatus.Removed } bot)
        {
            return null;
        }

        var space = await accounts.GetSpace(bot.SpaceId, cancellationToken);
        var fallbacks = ResolveFallbacks(bot.ModelSettings, space?.Settings);
        var requested = new[] { new ModelFallback(bot.ModelConnectionId, bot.Model) }.Concat(fallbacks).ToList();
        var primaryCatalog = await connections.FindCatalogEntry(bot.ModelConnectionId, bot.Model, cancellationToken);
        var grantedTools = (await tools.ListEffectiveGrants(bot.Id, cancellationToken)).Count;
        List<ModelChainEntry> chain = [];

        foreach (var selected in requested)
        {
            if (await connections.FindConnection(selected.ConnectionId, cancellationToken) is not { } connection)
            {
                continue;
            }

            var catalog = await connections.FindCatalogEntry(selected.ConnectionId, selected.Model, cancellationToken);

            if (catalog is { State: AvailabilityState.Missing })
            {
                continue;
            }

            var capabilities = catalog?.Capabilities ?? new ModelCapabilities();
            var pricing = catalog?.Pricing ?? new ModelPricing();
            chain.Add(new ModelChainEntry(
                connection.Id,
                connection.Name,
                Words.Of(connection.Status),
                selected.Model,
                catalog?.DisplayName ?? selected.Model,
                new ModelChainCapabilities(
                    capabilities.ToolCalling,
                    capabilities.Vision,
                    capabilities.ContextTokens,
                    capabilities.StructuredOutput),
                new ModelChainPrices(pricing.InputPerMillion, pricing.OutputPerMillion, pricing.Currency),
                catalog?.PrivacyMode,
                FindMismatches(bot, capabilities, primaryCatalog?.Capabilities, grantedTools)));
        }

        return new BotModelChain(chain);
    }

    #endregion

    #region Private Methods

    private static IReadOnlyList<ModelFallback> Normalize(IEnumerable<ModelFallback> fallbacks) =>
        [.. fallbacks.Select(fallback => fallback with { Model = fallback.Model.Trim() })];

    private static ModelFallback ToFallback(ModelFallbackBody body) => new(body.ConnectionId, body.Model ?? string.Empty);

    private static IReadOnlyList<ModelFallbackBody> ToBodies(IEnumerable<ModelFallback> fallbacks) =>
        [.. fallbacks.Select(fallback => new ModelFallbackBody(fallback.ConnectionId, fallback.Model))];

    private static List<string> FindMismatches(
        Bot bot,
        ModelCapabilities capabilities,
        ModelCapabilities? primary,
        int grantedTools)
    {
        List<string> mismatches = [];

        if (grantedTools > 0 && !capabilities.ToolCalling)
        {
            mismatches.Add($"Cannot call tools, so {bot.Name}'s {grantedTools} granted tools are not available on it.");
        }

        if (primary?.Vision is true && !capabilities.Vision)
        {
            mismatches.Add("Cannot read images.");
        }

        if (bot.ModelSettings.StructuredOutput && !capabilities.StructuredOutput)
        {
            mismatches.Add("Cannot produce the structured output this bot asks for.");
        }

        if (bot.ModelSettings.WebSearch is not WebSearchMode.Off && !capabilities.WebSearch)
        {
            mismatches.Add("Cannot use the built-in web search this bot asks for.");
        }

        if (bot.ModelSettings.MaxOutputTokens is { } wanted
            && capabilities.MaxOutputTokens is { } offered
            && wanted > offered)
        {
            mismatches.Add($"Writes at most {offered:N0} tokens; {bot.Name} asks for up to {wanted:N0}.");
        }

        if (primary?.ContextTokens is { } primaryContext
            && capabilities.ContextTokens is { } fallbackContext
            && fallbackContext < primaryContext)
        {
            mismatches.Add($"Holds {fallbackContext:N0} tokens; {bot.Name}'s primary model holds more.");
        }

        return mismatches;
    }

    #endregion
}
