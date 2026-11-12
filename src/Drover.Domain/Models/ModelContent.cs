namespace Drover.Domain.Models;

public abstract record ModelContent
{
    public IReadOnlyList<ModelCitation> Citations { get; init; } = [];
}

public sealed record ModelText(string Text) : ModelContent;

public sealed record ModelReasoning(string Text, bool IsUnavailable = false, string? ProtectedData = null) : ModelContent;

public sealed record ModelToolCall(string CallId, string Name, JsonElement Arguments) : ModelContent;

public sealed record ModelToolResult(string CallId, JsonElement Result, bool HasError = false) : ModelContent;

public sealed record ModelData(ReadOnlyMemory<byte> Data, string MediaType) : ModelContent;

public sealed record ModelUri(Uri Uri, string MediaType) : ModelContent;

public sealed record ModelTokenUsage(long? InputTokens, long? OutputTokens, long? TotalTokens) : ModelContent
{
    #region Properties

    public long? CachedInputTokens { get; init; }

    public long? ReasoningTokens { get; init; }

    public IReadOnlyDictionary<string, long>? AdditionalCounts { get; init; }

    #endregion
}

public sealed record ModelCitation(string? Title, Uri? Url);
