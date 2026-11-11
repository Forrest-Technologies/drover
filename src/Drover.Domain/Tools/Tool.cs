namespace Drover.Domain.Tools;

/// <summary>
/// One tool a bot can be granted: a built-in, or one a tool server lists. A tool that disappears from its server
/// keeps its row and its grants, marked missing.
/// </summary>
public sealed class Tool : ISpaceOwned
{
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

    public ToolSensitivity Sensitivity { get; set; } = ToolSensitivity.Write;

    public AvailabilityState State { get; set; } = AvailabilityState.Available;

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }
}
