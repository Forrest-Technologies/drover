namespace Drover.Domain.Models;

/// <summary>
/// What a model can do. A generic endpoint defaults to tools and streaming and nothing else, so a bot is never
/// offered a feature the model lacks.
/// </summary>
public sealed record ModelCapabilities
{
    public int? ContextTokens { get; init; }

    public int? MaxOutputTokens { get; init; }

    public bool ToolCalling { get; init; } = true;

    public bool Streaming { get; init; } = true;

    public bool Vision { get; init; }

    public bool Reasoning { get; init; }

    public IReadOnlyList<string> ReasoningEfforts { get; init; } = [];

    public bool StructuredOutput { get; init; }

    public bool WebSearch { get; init; }
}
