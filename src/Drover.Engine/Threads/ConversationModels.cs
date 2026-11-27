namespace Drover.Engine.Threads;

/// <summary>
/// The models a conversation with a bot may answer with in place of the bot's own. A bot names one model connection,
/// so its allowance is the models of that connection that are still offered and that fit the space's cost limits.
/// Limits only narrow: a model that could make one run cost more than the space lets a run tree cost is left out.
/// Runs in the tenant scope the caller opened.
/// </summary>
public sealed class ConversationModels(ModelConnectionRepository connections, PolicyService policies)
{
    #region Public Methods

    /// <summary>The models the bot's conversations may choose, by name.</summary>
    public async Task<List<ModelCatalogEntry>> ListAllowed(Bot bot, CancellationToken cancellationToken)
    {
        var catalog = await connections.ListCatalog(bot.ModelConnectionId, cancellationToken);
        var budget = await Budget(bot, cancellationToken);
        return [.. catalog.Where(entry => entry.State is AvailabilityState.Available && Fits(entry.EffectivePricing, budget))];
    }

    /// <summary>
    /// Why the bot's conversations may not answer with <paramref name="model"/>, in words for the person who chose it.
    /// Null when they may.
    /// </summary>
    public async Task<string?> FindProblem(Bot bot, string model, CancellationToken cancellationToken)
    {
        var entry = await connections.FindCatalogEntry(bot.ModelConnectionId, model, cancellationToken);

        if (entry is not { State: AvailabilityState.Available })
        {
            return $"{bot.Name} cannot answer with {model}: it is not one of the models on the bot's model connection.";
        }

        var budget = await Budget(bot, cancellationToken);
        return Fits(entry.EffectivePricing, budget)
            ? null
            : $"{entry.DisplayName} could cost more in one run than this space lets a run cost ({budget.MaxCost:0.00} {entry.EffectivePricing.Currency}).";
    }

    /// <summary>
    /// The model a run in the conversation answers with: the one the conversation chose while the bot may still use
    /// it, and the bot's own otherwise.
    /// </summary>
    public async Task<string> ModelFor(ChatThread? thread, Bot bot, CancellationToken cancellationToken) =>
        thread?.Model is { } chosen && chosen != bot.Model && await FindProblem(bot, chosen, cancellationToken) is null
            ? chosen
            : bot.Model;

    #endregion

    #region Private Methods

    /// <summary>The most tokens one of the bot's runs may use, and the most a run tree in the space may cost.</summary>
    private async Task<(int MaxTokens, decimal MaxCost)> Budget(Bot bot, CancellationToken cancellationToken)
    {
        var tokens = (await policies.GetRuns(bot.SpaceId, cancellationToken)).NarrowedBy(bot.RunLimits).MaxTokens;
        var cost = (await policies.GetDelegation(bot.SpaceId, cancellationToken)).MaxCost;
        return (tokens, cost);
    }

    /// <summary>
    /// A model fits when a run that used every token it may at the model's higher price would still cost no more than
    /// the tree may. A model with no known price is taken on trust, as the bot editor takes a model the catalog does
    /// not know.
    /// </summary>
    private static bool Fits(ModelPricing pricing, (int MaxTokens, decimal MaxCost) budget) =>
        Math.Max(pricing.InputPerMillion ?? 0, pricing.OutputPerMillion ?? 0) * budget.MaxTokens / 1_000_000m <= budget.MaxCost;

    #endregion
}
