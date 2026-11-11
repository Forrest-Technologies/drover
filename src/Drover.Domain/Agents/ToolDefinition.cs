namespace Drover.Domain.Agents;

/// <summary>A granted tool as the runtime offers it to the model.</summary>
public sealed record ToolDefinition
{
    public required Guid ToolId { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    public required JsonElement InputSchema { get; init; }

    public ToolSensitivity Sensitivity { get; init; } = ToolSensitivity.Write;

    /// <summary>The effective grant is <c>ask</c>: the run stops for a human before the tool runs.</summary>
    public bool RequiresApproval { get; init; }
}
