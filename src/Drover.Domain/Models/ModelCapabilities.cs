namespace Drover.Domain.Models;

/// <summary>
/// What a model can do. A generic endpoint defaults to tools and streaming and nothing else, so a bot is never
/// offered a feature the model lacks.
/// </summary>
public sealed record ModelCapabilities
{
    #region Properties

    public int? ContextTokens { get; init; }

    public int? MaxOutputTokens { get; init; }

    public bool ToolCalling { get; init; } = true;

    public bool Streaming { get; init; } = true;

    /// <summary>Whether the model reads pictures. For a model whose provider does not say, see <see cref="VisionSetByOperator"/>.</summary>
    public bool Vision { get; init; }

    /// <summary>
    /// True when a space admin marked the model as reading pictures. A provider that does not know its models'
    /// capabilities reports none, so a refresh keeps this mark and <see cref="Vision"/> with it.
    /// </summary>
    public bool VisionSetByOperator { get; init; }

    public bool Reasoning { get; init; }

    public IReadOnlyList<string> ReasoningEfforts { get; init; } = [];

    public bool StructuredOutput { get; init; }

    public bool WebSearch { get; init; }

    public bool ImageGeneration { get; init; }

    #endregion
}
