namespace Drover.Contracts.Cards;

/// <summary>
/// What a person chose and typed on a card. It names the call and carries indexes and typed words, never a value a
/// client could use to make a press say something other than the card showed. The server writes the words from the
/// stored card through <see cref="CardText.Words"/>.
/// </summary>
public sealed record CardAnswer(
    [property: JsonPropertyName("toolCallId")] Guid ToolCallId,
    [property: JsonPropertyName("choices")] IReadOnlyList<int>? Choices = null,
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("values")] IReadOnlyDictionary<string, string>? Values = null);
