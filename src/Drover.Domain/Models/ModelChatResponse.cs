namespace Drover.Domain.Models;

public sealed record ModelChatResponse(IReadOnlyList<ModelChatMessage> Messages)
{
    #region Properties

    public string? Id { get; init; }

    public string? ModelId { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public string? FinishReason { get; init; }

    public ModelTokenUsage? Usage { get; init; }

    public IReadOnlyList<ModelBalance> Balances { get; init; } = [];

    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public JsonElement? ProviderData { get; init; }

    #endregion
}

public sealed record ModelChatUpdate(IReadOnlyList<ModelContent> Contents)
{
    #region Properties

    public string? ResponseId { get; init; }

    public string? MessageId { get; init; }

    public string? ModelId { get; init; }

    public string? Role { get; init; }

    public string? AuthorName { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public string? FinishReason { get; init; }

    public IReadOnlyList<ModelBalance> Balances { get; init; } = [];

    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public JsonElement? ProviderData { get; init; }

    #endregion
}
