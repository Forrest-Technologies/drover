namespace Drover.Domain.Agents;

/// <summary>
/// One tool call the model asked for. <see cref="ToolId"/> is null for a tool the run's definitions do not name,
/// such as one a framework capability added. <see cref="Approved"/> is true when a human approved this call.
/// </summary>
public sealed record ToolCallRequest
{
    #region Properties

    public required Guid RunId { get; init; }

    public required string CallId { get; init; }

    public required string ToolName { get; init; }

    public Guid? ToolId { get; init; }

    public required JsonElement Arguments { get; init; }

    public bool Approved { get; init; }

    #endregion
}
