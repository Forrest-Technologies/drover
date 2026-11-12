namespace Drover.Domain.Models;

public sealed record ModelChatRequest(IReadOnlyList<ModelChatMessage> Messages, ModelChatOptions Options);

public sealed record ModelChatMessage(string Role, IReadOnlyList<ModelContent> Contents)
{
    public string? Id { get; init; }

    public string? AuthorName { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
}

public sealed record ModelTool(string Name, string Description, JsonElement Schema);

public sealed record ModelChatOptions
{
    #region Properties

    public string? ModelId { get; init; }

    public string? Instructions { get; init; }

    public int? MaxOutputTokens { get; init; }

    public float? Temperature { get; init; }

    public float? TopP { get; init; }

    public int? TopK { get; init; }

    public float? FrequencyPenalty { get; init; }

    public float? PresencePenalty { get; init; }

    public long? Seed { get; init; }

    public IReadOnlyList<string>? StopSequences { get; init; }

    public IReadOnlyList<ModelTool>? Tools { get; init; }

    public string? ToolMode { get; init; }

    public string? RequiredToolName { get; init; }

    public bool? AllowMultipleToolCalls { get; init; }

    public string? ReasoningEffort { get; init; }

    public string? ResponseFormat { get; init; }

    public JsonElement? ResponseSchema { get; init; }

    public string? ResponseSchemaName { get; init; }

    public string? ResponseSchemaDescription { get; init; }

    #endregion
}
