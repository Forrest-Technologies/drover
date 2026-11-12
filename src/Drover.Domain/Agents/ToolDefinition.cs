namespace Drover.Domain.Agents;

/// <summary>A granted tool as the runtime offers it to the model.</summary>
public sealed record ToolDefinition
{
    #region Properties

    public required Guid ToolId { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    public required JsonElement InputSchema { get; init; }

    public ToolSensitivity Sensitivity { get; init; } = ToolSensitivity.Write;

    /// <summary>The effective grant is <c>ask</c>: the run stops for a human before the tool runs.</summary>
    public bool RequiresApproval { get; init; }

    /// <summary>
    /// Whether a call cut off by a restart may run again. Only a read-only tool may: anything else might already
    /// have happened, so the model is told the outcome is unknown.
    /// </summary>
    public bool MayRunAgainAfterInterruption => Sensitivity is ToolSensitivity.Read;

    #endregion
}
