namespace Drover.Domain.Models;

/// <summary>
/// What a space admin says a model can do, member by member. A member that is null is not set: the provider's own
/// value stays in effect for it. <c>Streaming</c> and <c>WebSearch</c> are not offered, because nothing an admin
/// types changes how a model streams or whether it searches.
/// </summary>
public sealed record ModelCapabilityOverrides
{
    #region Properties

    public int? ContextTokens { get; init; }

    public int? MaxOutputTokens { get; init; }

    public bool? ToolCalling { get; init; }

    public bool? Vision { get; init; }

    public bool? Reasoning { get; init; }

    public IReadOnlyList<string>? ReasoningEfforts { get; init; }

    public bool? StructuredOutput { get; init; }

    public bool? ImageGeneration { get; init; }

    /// <summary>True when no member is set.</summary>
    public bool IsEmpty =>
        ContextTokens is null && MaxOutputTokens is null && ToolCalling is null && Vision is null && Reasoning is null
        && ReasoningEfforts is null && StructuredOutput is null && ImageGeneration is null;

    #endregion

    #region Public Methods

    /// <summary>The provider's capabilities with every member set here put over them.</summary>
    public ModelCapabilities ApplyTo(ModelCapabilities discovered) =>
        discovered with
        {
            ContextTokens = ContextTokens ?? discovered.ContextTokens,
            MaxOutputTokens = MaxOutputTokens ?? discovered.MaxOutputTokens,
            ToolCalling = ToolCalling ?? discovered.ToolCalling,
            Vision = Vision ?? discovered.Vision,
            Reasoning = Reasoning ?? discovered.Reasoning,
            ReasoningEfforts = Reasoning is false ? [] : ReasoningEfforts ?? discovered.ReasoningEfforts,
            StructuredOutput = StructuredOutput ?? discovered.StructuredOutput,
            ImageGeneration = ImageGeneration ?? discovered.ImageGeneration,
        };

    #endregion
}
