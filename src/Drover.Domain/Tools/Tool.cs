namespace Drover.Domain.Tools;

/// <summary>
/// One tool a bot can be granted: a built-in, or one a tool server lists. A tool that disappears from its server
/// keeps its row and its grants, marked missing.
/// </summary>
public sealed class Tool : ISpaceOwned
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public ToolSourceKind SourceKind { get; set; }

    public Guid? ToolConnectionId { get; set; }

    public required string Name { get; set; }

    public string? Title { get; set; }

    public string Description { get; set; } = string.Empty;

    public JsonElement InputSchema { get; set; }

    public JsonElement? Annotations { get; set; }

    /// <summary>A hash of the name, description, schema, and annotations. A change marks every grant for review.</summary>
    public required string DefinitionHash { get; set; }

    /// <summary>
    /// The definition as it was when a person last reviewed it, kept from the moment a tool server changes a tool
    /// somebody was granted until a person confirms the change. It is what the new definition is compared with.
    /// Null when nothing is waiting for review.
    /// </summary>
    public JsonElement? ReviewedDefinition { get; set; }

    public ToolSensitivity Sensitivity { get; set; } = ToolSensitivity.Write;

    public AvailabilityState State { get; set; } = AvailabilityState.Available;

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    #endregion

    #region Public Methods

    /// <summary>Interprets read-only hints from trusted MCP connections using the current tool definition.</summary>
    public ToolSensitivity SensitivityOn(ToolConnection? connection) =>
        SourceKind is ToolSourceKind.Mcp
            && connection?.Trust is TrustLevel.Trusted
            && ReviewedDefinition is null
            && Annotations is { ValueKind: JsonValueKind.Object } hints
            && hints.TryGetProperty("readOnlyHint", out var readOnly)
            && readOnly.ValueKind is JsonValueKind.True
            && (!hints.TryGetProperty("destructiveHint", out var destructive) || destructive.ValueKind is JsonValueKind.False)
                ? ToolSensitivity.Read
                : Sensitivity;

    #endregion
}
