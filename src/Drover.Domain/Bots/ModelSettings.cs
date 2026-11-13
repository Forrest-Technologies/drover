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

    #endregion
}
