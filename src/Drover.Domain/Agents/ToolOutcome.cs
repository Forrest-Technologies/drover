namespace Drover.Domain.Agents;

/// <summary>
/// The result of one tool call, after redaction. <see cref="Result"/> is what history keeps. <see cref="Images"/>
/// travel only to the model's next call and are never stored: a picture cannot be redacted, and history is text.
/// </summary>
public sealed record ToolOutcome(JsonElement Result, bool IsError = false)
{
    #region Properties

    public IReadOnlyList<ToolImage> Images { get; init; } = [];

    #endregion
}
