namespace Drover.Domain.Bots;

/// <summary>Per-bot model options, in Drover's neutral names. An adapter maps them to its provider's.</summary>
public sealed record ModelSettings
{
    #region Properties

    public double? Temperature { get; init; }

    public int? MaxOutputTokens { get; init; }

    public string? ReasoningEffort { get; init; }

    public WebSearchMode WebSearch { get; init; } = WebSearchMode.Off;

    /// <summary>The bot answers as a JSON object, on a model that supports structured output.</summary>
    public bool StructuredOutput { get; init; }

    /// <summary>Ordered models to try after the primary model.</summary>
    public IReadOnlyList<ModelFallback> Fallbacks { get; init; } = [];

    /// <summary>Uses the space fallback list when this bot has no list of its own.</summary>
    public bool UseSpaceFallbacks { get; init; } = true;

    #endregion
}


/// <summary>A model on one connection of the bot's space.</summary>
public sealed record ModelFallback(Guid ConnectionId, string Model);
