namespace Drover.Contracts.Cards;

/// <summary>
/// One card a bot sends. The server validates this shape and stores it; the client draws what was stored.
/// <see cref="Fallback"/> is the card as plain text, written by the server, for a client that does not know the kind.
/// An unknown <c>kind</c> reads as this base record with its title and fallback.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(RepliesSpec), CardKinds.Replies)]
[JsonDerivedType(typeof(ChoicesSpec), CardKinds.Choices)]
[JsonDerivedType(typeof(FormSpec), CardKinds.Form)]
[JsonDerivedType(typeof(ChecklistSpec), CardKinds.Checklist)]
[JsonDerivedType(typeof(TableSpec), CardKinds.Table)]
[JsonDerivedType(typeof(BoardSpec), CardKinds.Board)]
public record CardSpec
{
    #region Properties

    [property: JsonPropertyName("title")]
    public required string Title { get; init; }

    [property: JsonPropertyName("text")]
    public string? Text { get; init; }

    [property: JsonPropertyName("fallback")]
    public required string Fallback { get; init; }

    #endregion
}

/// <summary>A few short answers under a reply. Pressing one sends its own label.</summary>
public sealed record RepliesSpec([property: JsonPropertyName("replies")] IReadOnlyList<string> Replies) : CardSpec;

/// <summary>A question with options and, always, the extra answer a person types.</summary>
public sealed record ChoicesSpec(
    [property: JsonPropertyName("choices")] IReadOnlyList<string> Choices,
    [property: JsonPropertyName("multiple")] bool Multiple) : CardSpec;

/// <summary>A short form of fields, in the order the bot wrote them.</summary>
public sealed record FormSpec([property: JsonPropertyName("fields")] IReadOnlyList<CardField> Fields) : CardSpec;

/// <summary>One field of a form. Its name is the key an answer carries, never a hidden value.</summary>
public sealed record CardField(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("required")] bool Required,
    [property: JsonPropertyName("min")] double? Min,
    [property: JsonPropertyName("max")] double? Max,
    [property: JsonPropertyName("options")] IReadOnlyList<string>? Options);

/// <summary>A read-only checklist the bot sets, one item with one state per row.</summary>
public sealed record ChecklistSpec([property: JsonPropertyName("items")] IReadOnlyList<CardItem> Items) : CardSpec;

/// <summary>One labelled state, used by a checklist item or a board state.</summary>
public sealed record CardItem(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("state")] string State);

/// <summary>A table drawn from its columns and rows of cells.</summary>
public sealed record TableSpec(
    [property: JsonPropertyName("columns")] IReadOnlyList<string> Columns,
    [property: JsonPropertyName("rows")] IReadOnlyList<IReadOnlyList<string>> Rows) : CardSpec;

/// <summary>A board of figures, states and rows that may link outside the app.</summary>
public sealed record BoardSpec(
    [property: JsonPropertyName("figures")] IReadOnlyList<CardFigure> Figures,
    [property: JsonPropertyName("states")] IReadOnlyList<CardItem> States,
    [property: JsonPropertyName("rows")] IReadOnlyList<CardRow> Rows) : CardSpec;

/// <summary>One figure of a board: a value and its label, with a state when the bot set one.</summary>
public sealed record CardFigure(
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("state")] string? State);

/// <summary>One row of a board, with a value and an optional address.</summary>
public sealed record CardRow(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("value")] string? Value,
    [property: JsonPropertyName("link")] string? Link);
